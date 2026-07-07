using System;
using System.Collections.Generic;
using ExileCore;
using ExileCore.Shared;
using SharpDX;

namespace MapDeviceActivator;

public partial class MapDeviceActivator : BaseSettingsPlugin<MapDeviceActivatorSettings>
{
    internal static MapDeviceActivator Instance;

    private readonly Scheduler scheduler = new();
    private readonly string[] _scarabFilters = new string[5];
    private bool _activated;
    private bool _atlasSelectionAttempted;
    private DateTime _lastAtlasSelectionAttempt = DateTime.MinValue;
    private DateTime _lastScarabRefresh = DateTime.MinValue;
    private List<string> _scarabOptions = ["None"];

    public override bool Initialise()
    {
        Instance ??= this;
        RefreshScarabOptions();
        return base.Initialise();
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
        if (scheduler.CurrentTask != null || scheduler.Tasks.Count > 0 || _activated)
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
            QueueActivation(mapDeviceWindow);
            return;
        }

        var matchingMap = FindMatchingMapInInventory();
        if (matchingMap == null)
            return;

        var initialMapRect = matchingMap.GetClientRect();
        if (initialMapRect.Size == Size2F.Zero || initialMapRect.Height <= 0 || initialMapRect.Width <= 0)
            return;

        QueueActivation(mapDeviceWindow);
    }

    private void QueueActivation(object mapDeviceWindow)
    {
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
        return mapDeviceWindow != null && GetDynamicBool(() => ((dynamic)mapDeviceWindow).IsVisible);
    }

    private async SyncTask<bool> CtrlClickMapThenActivate(dynamic mapDeviceWindow)
    {
        if (MapDeviceHasMap(mapDeviceWindow))
        {
            if (!MapDeviceHasRequiredScarabs())
                return false;

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
}
