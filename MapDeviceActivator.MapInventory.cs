using System;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
    private static readonly Regex PercentRegex = new(@"(\d+)%", RegexOptions.Compiled);

    private ServerInventory.InventSlotItem FindMatchingMapInInventory()
    {
        var playerInventories = GameController?.Game?.IngameState?.ServerData?.PlayerInventories;
        var firstInventory = playerInventories?.FirstOrDefault();
        if (firstInventory?.Inventory?.InventorySlotItems == null)
            return null;

        foreach (var item in firstInventory.Inventory.InventorySlotItems)
        {
            if (item?.Item == null || !item.Item.IsValid)
                continue;

            if (!item.Item.TryGetComponent<MapKey>(out var mapKey) || mapKey.Tier != Settings.MapTier.Value)
                continue;

            if (!item.Item.TryGetComponent<Mods>(out var mods) || !MatchesMapRarity(mods.ItemRarity))
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

    private ServerInventory.InventSlotItem FindMatchingChartInInventory()
    {
        var playerInventories = GameController?.Game?.IngameState?.ServerData?.PlayerInventories;
        var firstInventory = playerInventories?.FirstOrDefault();
        if (firstInventory?.Inventory?.InventorySlotItems == null)
            return null;

        foreach (var item in firstInventory.Inventory.InventorySlotItems)
        {
            if (item?.Item == null || !item.Item.IsValid)
                continue;

            if (!item.Item.TryGetComponent<DeepwaterChart>(out _))
                continue;

            if (!item.Item.TryGetComponent<Mods>(out var mods) || !MatchesMapRarity(mods.ItemRarity))
                continue;

            return item;
        }

        return null;
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
