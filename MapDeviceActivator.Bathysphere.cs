using System;
using System.Linq;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using SharpDX;

namespace MapDeviceActivator;

public partial class MapDeviceActivator
{
    private const string ChartPortalMetadata = "Metadata/Terrain/Leagues/Deepwater/Objects/ChartPortal";

    private bool IsBathysphereMode()
    {
        return string.Equals(Settings.Mode.Value, "Bathysphere", StringComparison.OrdinalIgnoreCase);
    }

    private Element GetBathysphereWindow()
    {
        return GetChild(GameController?.IngameState?.IngameUi as Element, 32);
    }

    private bool IsBathysphereWindowVisible()
    {
        var window = GetBathysphereWindow();
        return window is { IsVisible: true };
    }

    private Element GetBathysphereChartSlot()
    {
        return GetChild(GetBathysphereWindow(), 3, 1);
    }

    private Element GetBathysphereDescendButton()
    {
        return GetChild(GetBathysphereWindow(), 3, 0);
    }

    private bool IsBathysphereChartSlotEmpty()
    {
        var slot = GetBathysphereChartSlot();
        return slot?.Children != null && slot.Children.Count == 1;
    }

    private bool IsBathysphereChartSlotFilled()
    {
        var slot = GetBathysphereChartSlot();
        return slot?.Children != null && slot.Children.Count == 2;
    }

    private LabelOnGround FindChartPortalLabel()
    {
        var labels = GameController?.IngameState?.IngameUi?.ItemsOnGroundLabelsVisible;
        if (labels == null)
            return null;

        return labels.FirstOrDefault(label =>
            label is { IsVisible: true, Label.IsValid: true, Label.IsVisible: true, ItemOnGround: { IsValid: true } entity } &&
            IsChartPortalEntity(entity));
    }

    private static bool IsChartPortalEntity(Entity entity)
    {
        return string.Equals(entity.Path, ChartPortalMetadata, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entity.Metadata, ChartPortalMetadata, StringComparison.OrdinalIgnoreCase);
    }

    private void QueueBathysphereActivation()
    {
        _activated = true;
        scheduler.AddTask(InsertChartDescendAndEnterPortal(), "ActivateBathysphere");
        scheduler.Run();
    }

    private async SyncTask<bool> InsertChartDescendAndEnterPortal()
    {
        if (!IsBathysphereWindowVisible())
            return false;

        if (!IsBathysphereChartSlotFilled())
        {
            if (!IsBathysphereChartSlotEmpty())
                return false;

            var chart = FindMatchingChartInInventory();
            if (chart == null)
                return false;

            var chartRect = chart.GetClientRect();
            if (chartRect.Size == Size2F.Zero || chartRect.Height <= 0 || chartRect.Width <= 0)
                return false;

            await InputAsync.HoldCtrl();
            await InputAsync.ClickElement(chartRect);
            await InputAsync.ReleaseCtrl();

            if (!await InputAsync.Wait(IsBathysphereChartSlotFilled, 1000, "Timed out waiting for bathysphere chart slot to fill."))
                return false;
        }

        var descendButton = GetBathysphereDescendButton();
        if (descendButton == null)
            return false;

        var descendRect = descendButton.GetClientRectCache;
        if (descendRect.Size == Size2F.Zero || descendRect.Height <= 0 || descendRect.Width <= 0)
            return false;

        await InputAsync.ClickElement(descendRect);

        // Brief settle so the ChartPortal label is ready after Descend.
        await InputAsync.Wait(Random.Shared.Next(500, 551));

        if (!await InputAsync.Wait(() => FindChartPortalLabel() != null, 5000, "Timed out waiting for ChartPortal ground label."))
            return false;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            var portalLabel = FindChartPortalLabel();
            if (portalLabel == null)
                return true;

            var label = portalLabel.Label;
            if (label == null)
                return true;

            var labelRect = label.GetClientRectCache;
            if (labelRect.Size == Size2F.Zero || labelRect.Height <= 0 || labelRect.Width <= 0)
                labelRect = label.GetClientRect();

            if (labelRect.Size != Size2F.Zero && labelRect.Height > 0 && labelRect.Width > 0)
                await InputAsync.ClickElement(labelRect);

            await InputAsync.Wait(Random.Shared.Next(65, 101));
        }

        return FindChartPortalLabel() == null;
    }
}
