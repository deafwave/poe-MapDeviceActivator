using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore.Shared.Nodes;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
    private static readonly string[] FallbackScarabOptions = ["None"];

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
}
