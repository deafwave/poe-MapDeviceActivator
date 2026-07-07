using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using Microsoft.CSharp.RuntimeBinder;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
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
}
