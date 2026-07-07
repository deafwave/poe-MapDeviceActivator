using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
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
    private bool _atlasSelectionAttempted = false;
    private DateTime _lastAtlasSelectionAttempt = DateTime.MinValue;
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
        var mapDeviceWindow = GetMapDeviceWindow();
        var atlas = GameController.IngameState.IngameUi.Atlas;
        if (IsMapDeviceWindowVisible(mapDeviceWindow) || atlas is { IsVisible: true })
        {
            scheduler.Run();
        }
        else
        {
            if (scheduler.CurrentTask != null || scheduler.Tasks.Count > 0)
                scheduler.StopAllRoutines();

            _activated = false;
            _atlasSelectionAttempted = false;
        }
        return null;
    }

    public override void Render()
    {
        if (scheduler.CurrentTask != null || scheduler.Tasks.Count > 0)
            return;

        if (_activated)
            return;

        var mapDeviceWindow = GetMapDeviceWindow();
        if (!IsMapDeviceWindowVisible(mapDeviceWindow))
        {
            if (ShouldAttemptAtlasSelection())
            {
                _atlasSelectionAttempted = true;
                _lastAtlasSelectionAttempt = DateTime.UtcNow;
                scheduler.AddTask(SelectAtlasMap(Settings.AtlasMapName.Value), "SelectAtlasMap");
                scheduler.Run();
            }

            return;
        }

        if (MapDeviceHasMap(mapDeviceWindow))
        {
            _activated = true;
            scheduler.AddTask(CtrlClickMapThenActivate(mapDeviceWindow), "ActivateMap");
            scheduler.Run();
            return;
        }

        var matchingMap = FindMatchingMapInInventory();
        if (matchingMap == null)
            return;

        var initialMapRect = matchingMap.GetClientRect();
        if (initialMapRect.Size == Size2F.Zero || initialMapRect.Height <= 0 || initialMapRect.Width <= 0)
            return;

        _activated = true;
        scheduler.AddTask(CtrlClickMapThenActivate(mapDeviceWindow), "ActivateMap");
        scheduler.Run();
    }

    private bool ShouldAttemptAtlasSelection()
    {
        if (string.IsNullOrWhiteSpace(Settings.AtlasMapName.Value) || GameController.IngameState.IngameUi.Atlas is not { IsVisible: true })
            return false;

        return !_atlasSelectionAttempted || (DateTime.UtcNow - _lastAtlasSelectionAttempt).TotalSeconds > 5;
    }

    private object GetMapDeviceWindow()
    {
        dynamic atlas = GameController?.IngameState?.IngameUi?.Atlas;
        return GetDynamicValue(() => atlas.MapDeviceWindow) ?? GameController?.IngameState?.IngameUi?.MapDeviceWindow;
    }

    private static bool IsMapDeviceWindowVisible(object mapDeviceWindow)
    {
        if (mapDeviceWindow == null)
            return false;

        return GetDynamicBool(() => ((dynamic)mapDeviceWindow).IsVisible);
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
        if (MapDeviceHasMap(mapDeviceWindow))
        {
            if (!MapDeviceHasRequiredScarabs())
            {
                return false;
            }

            await InputAsync.ClickElement(mapDeviceWindow.ActivateButton.GetClientRectCache);
            return true;
        }

        var map = FindMatchingMapInInventory();
        if (map == null)
            return false;

        var mapRect = map.GetClientRect();
        if (mapRect.Size == Size2F.Zero || mapRect.Height <= 0 || mapRect.Width <= 0)
            return false;

        await InputAsync.HoldCtrl();
        await InputAsync.ClickElement(mapRect);
        await InputAsync.ReleaseCtrl();

        if (!await InputAsync.Wait(() => MapDeviceHasMap(mapDeviceWindow), 1000, "Timed out waiting for map device slot to contain a map."))
            return false;

        if (!MapDeviceHasRequiredScarabs())
            return false;

        await InputAsync.ClickElement(mapDeviceWindow.ActivateButton.GetClientRectCache);
        return true;
    }

    private async SyncTask<bool> SelectAtlasMap(string atlasMapName)
    {
        var atlasMap = FindAtlasMapElement(atlasMapName);
        if (atlasMap == null)
            atlasMap = await HoverAtlasMapsUntilNameExists(atlasMapName);

        if (atlasMap == null)
            return false;

        var atlasMapRect = GetAtlasMapRect(atlasMap);
        if (atlasMapRect.Size == Size2F.Zero || atlasMapRect.Height <= 0 || atlasMapRect.Width <= 0)
            return false;

        ExileCore.Input.SetCursorPos(GetAtlasPanelCenter());
        await InputAsync.VerticalScroll(false, 8);
        await InputAsync.Wait(100);

        atlasMap = FindAtlasMapElement(atlasMapName);
        if (atlasMap == null)
            return false;

        atlasMapRect = GetAtlasMapRect(atlasMap);
        if (!IsRectVisibleOnScreen(atlasMapRect))
            return false;

        await InputAsync.ClickElement(atlasMapRect);
        return true;
    }

    private async SyncTask<object> HoverAtlasMapsUntilNameExists(string atlasMapName)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            foreach (var atlasMap in GetAtlasMapElements())
            {
                var rect = GetAtlasMapRect(atlasMap);
                if (rect.Size == Size2F.Zero || rect.Height <= 0 || rect.Width <= 0)
                    continue;

                ExileCore.Input.SetCursorPos(rect.Center);
                await TaskUtils.NextFrame();
                await TaskUtils.NextFrame();

                var name = GetAtlasMapName(atlasMap);
                if (AtlasMapNameMatches(name, atlasMapName))
                    return atlasMap;
            }

            await TaskUtils.NextFrame();
        }

        return null;
    }

    private object FindAtlasMapElement(string atlasMapName)
    {
        return GetAtlasMapElements()
            .FirstOrDefault(x => AtlasMapNameMatches(GetAtlasMapName(x), atlasMapName));
    }

    private IEnumerable<object> GetAtlasMapElements()
    {
        var innerAtlas = GetInnerAtlasElement();
        if (innerAtlas?.Children == null)
            yield break;

        foreach (var child in innerAtlas.Children)
        {
            if (Math.Abs(child.Height - 53f) < 0.5f)
                yield return child;
        }
    }

    private static string GetAtlasMapName(object atlasMapElement)
    {
        dynamic map = atlasMapElement;
        return GetDynamicValue(() => map.Tooltip.Children[1].Children[0].Text) as string ?? string.Empty;
    }

    private Element GetInnerAtlasElement()
    {
        var atlasPanel = GameController?.IngameState?.IngameUi?.Atlas;
        return GetMemberValue(atlasPanel, "InnerAtlas") as Element ?? GetChild(atlasPanel as Element, 0, 2);
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null)
            return null;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();
        return type.GetProperty(memberName, flags)?.GetValue(target) ??
               type.GetField(memberName, flags)?.GetValue(target);
    }

    private static Element GetChild(Element element, params int[] indices)
    {
        var current = element;
        foreach (var index in indices)
        {
            if (current?.Children == null || current.Children.Count <= index)
                return null;

            current = current.Children[index];
        }

        return current;
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

    private bool IsRectVisibleOnScreen(RectangleF rect)
    {
        if (rect.Size == Size2F.Zero || rect.Height <= 0 || rect.Width <= 0)
            return false;

        var windowRect = GameController.Window.GetWindowRectangle();
        var center = rect.Center;
        return center.X >= 0 &&
               center.Y >= 0 &&
               center.X <= windowRect.Width &&
               center.Y <= windowRect.Height;
    }

    private System.Numerics.Vector2 GetAtlasPanelCenter()
    {
        var atlasRect = ((Element)GameController.IngameState.IngameUi.Atlas).GetClientRectCache;
        if (atlasRect.Size != Size2F.Zero && atlasRect.Width > 0 && atlasRect.Height > 0)
            return new System.Numerics.Vector2(atlasRect.Center.X, atlasRect.Center.Y);

        var windowRect = GameController.Window.GetWindowRectangle();
        return new System.Numerics.Vector2(windowRect.Width / 2f, windowRect.Height / 2f);
    }

    private static bool MapDeviceHasMap(object mapDeviceWindow)
    {
        var map = GetMapDeviceMap(mapDeviceWindow);
        return map != null && IsMap(map);
    }

    private bool MapDeviceHasRequiredScarabs()
    {
        var requiredScarabs = GetRequiredScarabs().ToList();
        if (requiredScarabs.Count == 0)
            return true;

        var scarabs = GetMapDeviceScarabNames().Select(NormalizeItemName).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (scarabs.Count == 0)
            return false;

        foreach (var requiredScarab in requiredScarabs)
        {
            var normalizedRequiredScarab = NormalizeItemName(requiredScarab);
            var index = scarabs.FindIndex(x => string.Equals(x, normalizedRequiredScarab, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            scarabs.RemoveAt(index);
        }

        return true;
    }

    private static Entity GetMapDeviceMap(object mapDeviceWindow)
    {
        dynamic window = mapDeviceWindow;
        dynamic atlas = Instance?.GameController?.IngameState?.IngameUi?.Atlas;
        var atlasMapSlot = GetDynamicValue(() => atlas.MapDeviceWindow.MapSlot);
        var windowMapSlot = GetDynamicValue(() => window.MapSlot);

        return GetVisibleSlotEntity(atlasMapSlot) ?? GetVisibleSlotEntity(windowMapSlot);
    }

    private static IEnumerable<string> GetMapDeviceScarabNames()
    {
        dynamic atlas = Instance?.GameController?.IngameState?.IngameUi?.Atlas;
        var scarabSlots = GetDynamicValue(() => atlas.MapDeviceWindow.ScarabSlots);
        if (scarabSlots is not IEnumerable slots || scarabSlots is string)
            yield break;

        foreach (var slot in slots)
        {
            dynamic dynamicSlot = slot;
            var visibleItems = GetDynamicValue(() => dynamicSlot.VisibleInventoryItems);
            if (visibleItems is not IEnumerable items || visibleItems is string)
                continue;

            foreach (var visibleItem in items)
            {
                dynamic dynamicVisibleItem = visibleItem;
                var item = GetDynamicValue(() => dynamicVisibleItem.Item) as Entity;
                var name = GetBaseComponentName(item);
                if (!string.IsNullOrWhiteSpace(name) && IsScarabSlotItem(item, name))
                    yield return name;
            }
        }
    }

    private static string GetBaseComponentName(Entity item)
    {
        if (item == null || !item.TryGetComponent<Base>(out var baseComponent))
            return string.Empty;

        return GetDynamicValue(() => ((dynamic)baseComponent).Name) as string ??
               GetDynamicValue(() => ((dynamic)baseComponent).BaseName) as string ??
               string.Empty;
    }

    private static bool IsScarabSlotItem(Entity item, string name)
    {
        dynamic dynamicItem = item;
        var metadata = GetDynamicValue(() => dynamicItem.Metadata) as string;
        return metadata?.StartsWith("Metadata/Items/Scarabs/", StringComparison.OrdinalIgnoreCase) == true ||
               name.Contains("Scarab", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeItemName(string name)
    {
        return Regex.Replace(name ?? string.Empty, @"\s+", " ").Trim();
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
        catch (NullReferenceException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool GetDynamicBool(Func<object> getter)
    {
        return GetDynamicValue(getter) is true;
    }

    private static Entity GetVisibleSlotEntity(object slot)
    {
        if (slot == null)
            return null;

        dynamic dynamicSlot = slot;
        var visibleItems = GetDynamicValue(() => dynamicSlot.VisibleInventoryItems);
        if (visibleItems is not IEnumerable enumerable || visibleItems is string)
            return null;

        foreach (var visibleItem in enumerable)
        {
            dynamic dynamicVisibleItem = visibleItem;
            var item = GetDynamicValue(() => dynamicVisibleItem.Item) as Entity;
            if (item is { IsValid: true })
                return item;
        }

        return null;
    }

    private static bool IsMap(Entity item)
    {
        return item.TryGetComponent<MapKey>(out _);
    }

    private IEnumerable<string> GetRequiredScarabs()
    {
        return GetScarabSettings()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "None", StringComparison.OrdinalIgnoreCase));
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
