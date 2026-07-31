using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Collections.Generic;

namespace MapDeviceActivator;

public class MapDeviceActivatorSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(false);
    public ListNode Mode { get; set; } = new() { Values = new List<string> { "Map Device", "Bathysphere" }, Value = "Map Device" };
    public RangeNode<int> MapTier { get; set; } = new(16, 1, 17);
    public ListNode MapRarity { get; set; } = new() { Values = new List<string> { "Any", "Normal", "Magic", "Rare", "Unique" }, Value = "Normal" };
    public RangeNode<int> MinimumMapQuantity { get; set; } = new(0, 0, 200);
    public RangeNode<int> MinimumMapPackSize { get; set; } = new(0, 0, 100);
    public ToggleNode RequireMoreScarabs { get; set; } = new(false);
    public ToggleNode RequireMoreCurrency { get; set; } = new(false);
    public TextNode AtlasMapName { get; set; } = new("");
    public ListNode Scarab1 { get; set; } = new() { Values = new List<string> { "None" }, Value = "None" };
    public ListNode Scarab2 { get; set; } = new() { Values = new List<string> { "None" }, Value = "None" };
    public ListNode Scarab3 { get; set; } = new() { Values = new List<string> { "None" }, Value = "None" };
    public ListNode Scarab4 { get; set; } = new() { Values = new List<string> { "None" }, Value = "None" };
    public ListNode Scarab5 { get; set; } = new() { Values = new List<string> { "None" }, Value = "None" };
}
