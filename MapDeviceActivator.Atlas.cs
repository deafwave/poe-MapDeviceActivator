using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared;
using SharpDX;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
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

                ExileCore.Input.SetCursorPos(new System.Numerics.Vector2(rect.Center.X, rect.Center.Y));
                await TaskUtils.NextFrame();
                await TaskUtils.NextFrame();

                if (AtlasMapNameMatches(GetAtlasMapName(atlasMap), atlasMapName))
                    return atlasMap;
            }

            await TaskUtils.NextFrame();
        }

        return null;
    }

    private object FindAtlasMapElement(string atlasMapName)
    {
        return GetAtlasMapElements().FirstOrDefault(x => AtlasMapNameMatches(GetAtlasMapName(x), atlasMapName));
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
}
