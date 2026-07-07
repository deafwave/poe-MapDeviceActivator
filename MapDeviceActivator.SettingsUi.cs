using System;
using System.Linq;
using ExileCore.Shared.Nodes;
using ImGuiNET;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
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
}
