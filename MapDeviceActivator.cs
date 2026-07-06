using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Microsoft.CSharp.RuntimeBinder;
using SharpDX;

namespace MapDeviceActivator;

public class MapDeviceActivator : BaseSettingsPlugin<MapDeviceActivatorSettings>
{
    internal static MapDeviceActivator Instance;
    private readonly Scheduler scheduler = new();
    private bool _activated = false;
    private readonly string[] _scarabFilters = new string[5];
    private List<string> _scarabOptions = ["None"];
    private DateTime _lastScarabRefresh = DateTime.MinValue;
    private static readonly string[] FallbackScarabOptions = ["None"];
    private static readonly Regex PercentRegex = new(@"(\d+)%", RegexOptions.Compiled);

    public override bool Initialise()
    {
        Instance ??= this;
        RefreshScarabOptions();
        return base.Initialise();
    }

    public override void DrawSettings()
    {
        RefreshScarabOptionsIfNeeded();

        ImGui.Text("Map requirements");

        var mapTier = Settings.MapTier.Value;
        if (ImGui.SliderInt("Tier", ref mapTier, Settings.MapTier.Min, Settings.MapTier.Max))
            Settings.MapTier.Value = mapTier;

        DrawListDropdown("Rarity", Settings.MapRarity);

        var minimumMapQuantity = Settings.MinimumMapQuantity.Value;
        if (ImGui.SliderInt("Minimum Quantity", ref minimumMapQuantity, Settings.MinimumMapQuantity.Min, Settings.MinimumMapQuantity.Max))
            Settings.MinimumMapQuantity.Value = minimumMapQuantity;

        var minimumMapPackSize = Settings.MinimumMapPackSize.Value;
        if (ImGui.SliderInt("Minimum Pack Size", ref minimumMapPackSize, Settings.MinimumMapPackSize.Min, Settings.MinimumMapPackSize.Max))
            Settings.MinimumMapPackSize.Value = minimumMapPackSize;

        var requireMoreScarabs = Settings.RequireMoreScarabs.Value;
        if (ImGui.Checkbox("Require More Scarabs", ref requireMoreScarabs))
            Settings.RequireMoreScarabs.Value = requireMoreScarabs;

        var requireMoreCurrency = Settings.RequireMoreCurrency.Value;
        if (ImGui.Checkbox("Require More Currency", ref requireMoreCurrency))
            Settings.RequireMoreCurrency.Value = requireMoreCurrency;

        var atlasMapName = Settings.AtlasMapName.Value;
        if (ImGui.InputTextWithHint("Atlas Map Selection", "Leave empty to keep current selection", ref atlasMapName, 100))
            Settings.AtlasMapName.Value = atlasMapName;

        ImGui.Separator();

        ImGui.Text("Required map device scarabs");
        DrawScarabDropdown("Scarab 1", Settings.Scarab1, 0);
        DrawScarabDropdown("Scarab 2", Settings.Scarab2, 1);
        DrawScarabDropdown("Scarab 3", Settings.Scarab3, 2);
        DrawScarabDropdown("Scarab 4", Settings.Scarab4, 3);
        DrawScarabDropdown("Scarab 5", Settings.Scarab5, 4);
    }

    public override Job Tick()
    {
        var mapDeviceWindow = GameController.IngameState.IngameUi.MapDeviceWindow;
        if (mapDeviceWindow is { IsVisible: true })
        {
            scheduler.Run();
        }
        else
        {
            if (scheduler.CurrentTask != null || scheduler.Tasks.Count > 0)
                scheduler.StopAllRoutines();

            _activated = false;
        }
        return null;
    }

    public override void Render()
    {
        if (scheduler.CurrentTask != null || scheduler.Tasks.Count > 0)
            return;

        if (_activated)
            return;

        var mapDeviceWindow = GameController.IngameState.IngameUi.MapDeviceWindow;
        if (mapDeviceWindow == null || !mapDeviceWindow.IsVisible)
            return;

        var matchingMap = FindMatchingMapInInventory();
        if (matchingMap == null)
        {
            DebugWindow.LogMsg("MapDeviceActivator: No inventory map matched the current map settings.", 5);
            return;
        }

        var initialMapRect = matchingMap.GetClientRect();
        if (initialMapRect.Size == Size2F.Zero || initialMapRect.Height <= 0 || initialMapRect.Width <= 0)
            return;

        _activated = true;
        scheduler.AddTask(CtrlClickMapThenActivate(mapDeviceWindow), "ActivateMap");
    }

    private ServerInventory.InventSlotItem FindMatchingMapInInventory()
    {
        var playerInventories = GameController?.Game?.IngameState?.ServerData?.PlayerInventories;
        if (playerInventories == null)
            return null;

        var firstInventory = playerInventories.FirstOrDefault();
        if (firstInventory?.Inventory?.InventorySlotItems == null)
            return null;

        foreach (var item in firstInventory.Inventory.InventorySlotItems)
        {
            if (item?.Item == null || !item.Item.IsValid)
                continue;

            if (!item.Item.TryGetComponent<MapKey>(out var mapKey))
                continue;

            if (mapKey.Tier != Settings.MapTier.Value)
                continue;

            if (!item.Item.TryGetComponent<Mods>(out var mods))
                continue;

            if (!MatchesMapRarity(mods.ItemRarity))
                continue;

            if (GetMapStatValue(mods, "quantity") < Settings.MinimumMapQuantity.Value)
                continue;

            if (GetMapStatValue(mods, "pack", "size") < Settings.MinimumMapPackSize.Value)
                continue;

            if (Settings.RequireMoreScarabs.Value && !HasMapStat(mods, "scarab"))
                continue;

            if (Settings.RequireMoreCurrency.Value && !HasMapStat(mods, "currency"))
                continue;

            return item;
        }

        return null;
    }

    private async SyncTask<bool> CtrlClickMapThenActivate(dynamic mapDeviceWindow)
    {
        var map = FindMatchingMapInInventory();
        if (map == null)
        {
            DebugWindow.LogMsg("MapDeviceActivator: Matching map disappeared before click.", 5);
            return false;
        }

        var mapRect = map.GetClientRect();
        if (mapRect.Size == Size2F.Zero || mapRect.Height <= 0 || mapRect.Width <= 0)
            return false;

        await InputAsync.HoldCtrl();
        await InputAsync.ClickElement(mapRect);
        await InputAsync.ReleaseCtrl();

        if (!await InputAsync.Wait(() => MapDeviceHasMapAndRequiredScarabs(mapDeviceWindow), 1000, "Timed out waiting for map device slots to contain a map and required scarabs."))
            return false;

        if (!await EnsureAtlasMapSelected(mapDeviceWindow))
            return false;

        await InputAsync.ClickElement(mapDeviceWindow.ActivateButton.GetClientRectCache);
        return true;
    }

    private async SyncTask<bool> EnsureAtlasMapSelected(object mapDeviceWindow)
    {
        var atlasMapName = Settings.AtlasMapName.Value;
        if (string.IsNullOrWhiteSpace(atlasMapName) || IsAtlasMapSelected(atlasMapName))
            return true;

        if (!await InputAsync.Wait(() => FindAtlasMapElement(atlasMapName) != null, 5000, $"Timed out waiting for atlas map tooltip name: {atlasMapName}"))
            return false;

        var atlasMap = FindAtlasMapElement(atlasMapName);
        if (atlasMap == null)
            return false;

        var atlasMapRect = GetAtlasMapRect(atlasMap);
        if (atlasMapRect.Size == Size2F.Zero || atlasMapRect.Height <= 0 || atlasMapRect.Width <= 0)
            return false;

        await InputAsync.ClickElement(atlasMapRect);
        return true;
    }

    private bool IsAtlasMapSelected(string atlasMapName)
    {
        return GetAtlasMapElements()
            .Where(IsSelectedAtlasMapElement)
            .Any(x => AtlasMapNameMatches(GetAtlasMapName(x), atlasMapName));
    }

    private object FindAtlasMapElement(string atlasMapName)
    {
        return GetAtlasMapElements()
            .FirstOrDefault(x => AtlasMapNameMatches(GetAtlasMapName(x), atlasMapName));
    }

    private IEnumerable<object> GetAtlasMapElements()
    {
        dynamic atlas = GameController?.IngameState?.IngameUi?.Atlas;
        var innerAtlas = GetDynamicValue(() => atlas.InnerAtlas);
        if (innerAtlas is not IEnumerable children)
            yield break;

        foreach (var child in children)
        {
            if (Math.Abs(GetDynamicFloat(() => ((dynamic)child).Height) - 53f) < 0.5f)
                yield return child;
        }
    }

    private static string GetAtlasMapName(object atlasMapElement)
    {
        dynamic map = atlasMapElement;
        return GetDynamicValue(() => map.Tooltip.Children[1].Children[0].Text) as string ?? string.Empty;
    }

    private static bool AtlasMapNameMatches(string actualName, string configuredName)
    {
        var actual = NormalizeAtlasMapName(actualName);
        var configured = NormalizeAtlasMapName(configuredName);

        return !string.IsNullOrWhiteSpace(actual) &&
               !string.IsNullOrWhiteSpace(configured) &&
               (string.Equals(actual, configured, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(RemoveMapSuffix(actual), RemoveMapSuffix(configured), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAtlasMapName(string name)
    {
        return Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
    }

    private static string RemoveMapSuffix(string name)
    {
        return name.EndsWith(" Map", StringComparison.OrdinalIgnoreCase) ? name[..^4].Trim() : name;
    }

    private static RectangleF GetAtlasMapRect(object atlasMapElement)
    {
        dynamic map = atlasMapElement;
        return (RectangleF)(GetDynamicValue(() => map.GetClientRectCache) ?? GetDynamicValue(() => map.GetClientRect()) ?? RectangleF.Empty);
    }

    private static bool IsSelectedAtlasMapElement(object atlasMapElement)
    {
        dynamic map = atlasMapElement;
        return GetDynamicBool(() => map.IsSelected) ||
               GetDynamicBool(() => map.Selected) ||
               GetDynamicBool(() => map.IsActive);
    }

    private bool MapDeviceHasMapAndRequiredScarabs(object mapDeviceWindow)
    {
        var items = GetMapDeviceItems(mapDeviceWindow).ToList();
        if (!items.Any(IsMap))
            return false;

        var requiredScarabs = GetRequiredScarabs().ToList();
        if (requiredScarabs.Count == 0)
            return true;

        var scarabs = items.Where(IsScarab).Select(GetBaseName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (scarabs.Count == 0)
            return false;

        foreach (var requiredScarab in requiredScarabs)
        {
            var index = scarabs.FindIndex(x => string.Equals(x, requiredScarab, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            scarabs.RemoveAt(index);
        }

        return true;
    }

    private static IEnumerable<Entity> GetMapDeviceItems(object mapDeviceWindow)
    {
        dynamic window = mapDeviceWindow;

        foreach (var item in GetItems(GetDynamicValue(() => window.MapSlot)))
            yield return item;

        foreach (var item in GetItemsSkippingFirst(GetDynamicValue(() => window.ScarabSlots)))
            yield return item;

        foreach (var item in GetItems(GetDynamicValue(() => window.Inventory.InventorySlotItems)))
            yield return item;

        foreach (var item in GetItems(GetDynamicValue(() => window.MapDeviceInventory.InventorySlotItems)))
            yield return item;

        foreach (var item in GetItems(GetDynamicValue(() => window.InventorySlotItems)))
            yield return item;

        foreach (var item in GetItems(GetDynamicValue(() => window.Items)))
            yield return item;
    }

    private static object GetDynamicValue(Func<object> getter)
    {
        try
        {
            return getter();
        }
        catch (RuntimeBinderException)
        {
            return null;
        }
    }

    private static bool GetDynamicBool(Func<object> getter)
    {
        return GetDynamicValue(getter) is true;
    }

    private static float GetDynamicFloat(Func<object> getter)
    {
        return GetDynamicValue(getter) switch
        {
            float value => value,
            double value => (float)value,
            int value => value,
            _ => 0
        };
    }

    private static IEnumerable<Entity> GetItems(object items)
    {
        if (items is not IEnumerable enumerable)
        {
            var entity = GetEntity(items);
            if (entity != null)
                yield return entity;

            yield break;
        }

        foreach (var item in enumerable)
        {
            var entity = GetEntity(item);
            if (entity != null)
                yield return entity;
        }
    }

    private static IEnumerable<Entity> GetItemsSkippingFirst(object items)
    {
        if (items is not IEnumerable enumerable || items is string)
        {
            foreach (var item in GetItems(items))
                yield return item;

            yield break;
        }

        var index = 0;
        foreach (var item in enumerable)
        {
            if (index++ == 0)
                continue;

            var entity = GetEntity(item);
            if (entity != null)
                yield return entity;
        }
    }

    private static Entity GetEntity(object item)
    {
        if (item is Entity entity)
            return entity;

        var dynamicItem = (dynamic)item;
        return GetDynamicValue(() => dynamicItem.Item) as Entity ??
               GetDynamicValue(() => dynamicItem.Entity) as Entity ??
               GetDynamicValue(() => dynamicItem.Item.Item) as Entity ??
               GetDynamicValue(() => dynamicItem.InventoryItem.Item) as Entity;
    }

    private static bool IsMap(Entity item)
    {
        return item.TryGetComponent<MapKey>(out _);
    }

    private static bool IsScarab(Entity item)
    {
        return item.Metadata?.StartsWith("Metadata/Items/Scarabs/", StringComparison.Ordinal) == true;
    }

    private IEnumerable<string> GetRequiredScarabs()
    {
        return GetScarabSettings()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "None", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetBaseName(Entity item)
    {
        return Instance?.GameController?.Files?.BaseItemTypes?.Translate(item.Path)?.BaseName ?? string.Empty;
    }

    private void RefreshScarabOptions()
    {
        var scarabs = GetScarabBaseNamesFromBaseItemTypes()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        foreach (var fallbackScarab in FallbackScarabOptions.Reverse())
        {
            if (!scarabs.Contains(fallbackScarab, StringComparer.OrdinalIgnoreCase))
                scarabs.Insert(0, fallbackScarab);
        }

        _scarabOptions = scarabs;

        foreach (var setting in GetScarabSettings())
        {
            setting.Values = _scarabOptions;
            if (string.Equals(setting.Value, "Any", StringComparison.OrdinalIgnoreCase))
                setting.Value = "None";

            if (!_scarabOptions.Contains(setting.Value, StringComparer.OrdinalIgnoreCase))
                setting.Value = "None";
        }
    }

    private void RefreshScarabOptionsIfNeeded()
    {
        if ((DateTime.UtcNow - _lastScarabRefresh).TotalSeconds < 5)
            return;

        _lastScarabRefresh = DateTime.UtcNow;
        RefreshScarabOptions();
    }

    private ListNode[] GetScarabSettings()
    {
        return [Settings.Scarab1, Settings.Scarab2, Settings.Scarab3, Settings.Scarab4, Settings.Scarab5];
    }

    private IEnumerable<string> GetScarabBaseNamesFromBaseItemTypes()
    {
        var baseItemTypesObject = GameController?.Files?.BaseItemTypes;
        if (baseItemTypesObject == null)
            yield break;

        dynamic baseItemTypes = baseItemTypesObject;
        foreach (var collection in new[]
                 {
                     GetDynamicValue(() => baseItemTypes.EntriesList),
                     GetDynamicValue(() => baseItemTypes.Entries),
                     GetDynamicValue(() => baseItemTypes.Records),
                     GetDynamicValue(() => baseItemTypes.Contents),
                     GetDynamicValue(() => baseItemTypes.List)
                 })
        {
            foreach (var scarabName in GetScarabBaseNamesFromCollection(collection))
                yield return scarabName;
        }

        var type = baseItemTypesObject.GetType();
        foreach (var property in type.GetProperties())
        {
            foreach (var scarabName in GetScarabBaseNamesFromCollection(GetDynamicValue(() => property.GetValue(baseItemTypesObject))))
                yield return scarabName;
        }

        foreach (var field in type.GetFields())
        {
            foreach (var scarabName in GetScarabBaseNamesFromCollection(GetDynamicValue(() => field.GetValue(baseItemTypesObject))))
                yield return scarabName;
        }
    }

    private static IEnumerable<string> GetScarabBaseNamesFromCollection(object collection)
    {
        if (collection is not IEnumerable items || collection is string)
            yield break;

        foreach (var item in items)
        {
            var baseItem = GetDynamicValue(() => ((dynamic)item).Value) ?? item;
            var metadata = GetDynamicValue(() => ((dynamic)baseItem).Metadata) as string;
            if (metadata?.StartsWith("Metadata/Items/Scarabs/", StringComparison.Ordinal) != true)
                continue;

            var baseName = GetDynamicValue(() => ((dynamic)baseItem).BaseName) as string;
            if (!string.IsNullOrWhiteSpace(baseName))
                yield return baseName;
        }
    }

    private void DrawScarabDropdown(string label, ListNode setting, int index)
    {
        _scarabFilters[index] ??= string.Empty;

        ImGui.InputTextWithHint($"##{label}Filter", "Type to search...", ref _scarabFilters[index], 100);

        var filtered = _scarabOptions
            .Where(x => string.IsNullOrWhiteSpace(_scarabFilters[index]) || x.Contains(_scarabFilters[index], StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(_scarabFilters[index]) && !filtered.Contains(_scarabFilters[index], StringComparer.OrdinalIgnoreCase))
            filtered = filtered.Prepend(_scarabFilters[index]).ToArray();

        if (!filtered.Contains(setting.Value, StringComparer.OrdinalIgnoreCase))
            filtered = filtered.Prepend(setting.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var selectedIndex = Array.FindIndex(filtered, x => string.Equals(x, setting.Value, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;

        if (ImGui.Combo(label, ref selectedIndex, filtered, filtered.Length))
            setting.Value = filtered[selectedIndex];
    }

    private static void DrawListDropdown(string label, ListNode setting)
    {
        var values = setting.Values.ToArray();
        var selectedIndex = Array.FindIndex(values, x => string.Equals(x, setting.Value, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;

        if (ImGui.Combo(label, ref selectedIndex, values, values.Length))
            setting.Value = values[selectedIndex];
    }

    private bool MatchesMapRarity(ItemRarity rarity)
    {
        return string.Equals(Settings.MapRarity.Value, "Any", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Settings.MapRarity.Value, rarity.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static int GetMapStatValue(Mods mods, params string[] requiredTerms)
    {
        var best = 0;

        foreach (var mod in mods.ItemMods)
        {
            foreach (var text in new[] { mod.Translation, mod.DisplayName, mod.Name, mod.RawName })
            {
                if (string.IsNullOrWhiteSpace(text) || requiredTerms.Any(term => !text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var match = PercentRegex.Match(text);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
                    best = Math.Max(best, value);
            }
        }

        return best;
    }

    private static bool HasMapStat(Mods mods, params string[] requiredTerms)
    {
        return mods.ItemMods.Any(mod =>
            new[] { mod.Translation, mod.DisplayName, mod.Name, mod.RawName }.Any(text =>
                !string.IsNullOrWhiteSpace(text) && requiredTerms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))));
    }
}
