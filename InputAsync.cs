using System;
using System.Windows.Forms;
using ExileCore.Shared;
using SharpDX;
using InputHumanizer.Input;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExileCore;

namespace MapDeviceActivator;

public class InputAsync : ExileCore.Input
{
    private const int MouseEventWheel = 0x0800;
    private const int WheelDelta = 120;
    private static Random Random { get; } = new Random();
    private static MapDeviceActivator Instance => MapDeviceActivator.Instance;
    public static IInputController _inputController = null;
    public static bool LOCK_CONTROLLER = false;

    private static void IController()
    {
        LOCK_CONTROLLER = true;
        if (_inputController != null)
            return;

        if (Instance == null)
        {
            return;
        }

        var tryGetInputController = Instance.GameController.PluginBridge.GetMethod<Func<string, IInputController>>("InputHumanizer.TryGetInputController");
        if (tryGetInputController == null)
        {
            return;
        }

        try
        {
            _inputController = tryGetInputController("MapDeviceActivator");
            if (_inputController == null)
            {
                throw new Exception("Failed to get InputHumanizer controller");
            }
        }
        catch
        {
            throw;
        }
    }

    public static void IControllerEnd()
    {
        if (LOCK_CONTROLLER)
            return;
        _inputController?.Dispose();
        _inputController = null;
    }

    private static System.Numerics.Vector2 AnywhereInRectangle(RectangleF rect)
    {
        var topLeft = rect.TopLeft;
        var bottomRight = rect.BottomRight;
        var xTenPct = (int)((bottomRight.X - topLeft.X) * 0.2f);
        var yTenPct = (int)((bottomRight.Y - topLeft.Y) * 0.2f);
        var randomX = Random.Next((int)topLeft.X + xTenPct, (int)bottomRight.X - xTenPct);
        var randomY = Random.Next((int)topLeft.Y + yTenPct, (int)bottomRight.Y - yTenPct);
        return new System.Numerics.Vector2(randomX, randomY);
    }

    public static async SyncTask<bool> ClickElement(RectangleF pos, MouseButtons mouseButton = MouseButtons.Left)
    {
        IController();
        ExileCore.Input.SetCursorPos(AnywhereInRectangle(pos));
        await _inputController.Click(mouseButton);
        IControllerEnd();
        return true;
    }

    public static async SyncTask<bool> ClickElement(RectangleF pos)
    {
        return await ClickElement(pos, MouseButtons.Left);
    }

    public static new async SyncTask<bool> Click(MouseButtons mouseButton = MouseButtons.Left)
    {
        IController();
        await _inputController.Click(mouseButton);
        IControllerEnd();
        return true;
    }

    public static async SyncTask<bool> HoldCtrl()
    {
        return await KeyDown(Keys.ControlKey);
    }

    public static async SyncTask<bool> ReleaseCtrl()
    {
        return await KeyUp(Keys.ControlKey);
    }

    public static new async SyncTask<bool> KeyDown(Keys key)
    {
        IController();
        await _inputController.KeyDown(key);
        IControllerEnd();
        return true;
    }

    public static new async SyncTask<bool> KeyUp(Keys key)
    {
        IController();
        await _inputController.KeyUp(key);
        IControllerEnd();
        return true;
    }

    public static new async SyncTask<bool> KeyPress(Keys key)
    {
        IController();
        await _inputController.KeyDown(key);
        await _inputController.KeyUp(key);
        IControllerEnd();
        return true;
    }

    public static new async SyncTask<bool> VerticalScroll(bool scrollUp, int clicks)
    {
        mouse_event(MouseEventWheel, 0, 0, (scrollUp ? clicks : -clicks) * WheelDelta, 0);
        await TaskUtils.NextFrame();
        return true;
    }

    public static async SyncTask<bool> Wait(int ms)
    {
        return await Wait(TimeSpan.FromMilliseconds(ms));
    }

    public static async SyncTask<bool> Wait(TimeSpan period)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < period)
        {
            await TaskUtils.NextFrame();
        }
        return true;
    }

    public static async SyncTask<bool> Wait(Func<bool> fn, int ms = 100, string ErrorMessage = "")
    {
        return await Wait(fn, TimeSpan.FromMilliseconds(ms), ErrorMessage);
    }

    public static async SyncTask<bool> Wait(Func<bool> fn, TimeSpan period, string ErrorMessage = "")
    {
        var sw = Stopwatch.StartNew();
        while (!fn() && sw.Elapsed < period)
            await TaskUtils.NextFrame();

        if (!fn())
        {
            return false;
        }
        return true;
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
}
