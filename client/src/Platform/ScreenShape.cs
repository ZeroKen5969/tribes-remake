using System;
using Godot;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Logic;

namespace Tribes.Client.Platform;

/// <summary>
/// The parts of the screen the interface keeps clear of, in window pixels: camera cut-outs
/// (Godot's safe area, on phones and tablets) and rounded corners (Android 12 and later, through
/// <c>com.prehistoric.tribes.platform.ScreenCorners</c>). A PC has neither; "--screen-corners=R"
/// pretends corners rounded by R window pixels there, to see the layout of such a phone.
/// </summary>
public static class ScreenShape
{
    private const string CornersClass = "com.prehistoric.tribes.platform.ScreenCorners";
    private const int CornerNumbers = 14;

    private static bool cornersUnavailable;

    /// <summary>The room the camera cut-outs need on each edge of a window of <paramref name="window"/>.</summary>
    public static EdgeMargins Cutouts(Vector2I window)
    {
        if (!OS.HasFeature("mobile"))
        {
            // On a PC the "safe area" is the desktop's work area, which says nothing about the window.
            return EdgeMargins.None;
        }
        Rect2I safe = DisplayServer.GetDisplaySafeArea();
        return SafeMargins.OutsideSafeArea(window.X, window.Y, safe.Position.X, safe.Position.Y, safe.Size.X, safe.Size.Y);
    }

    /// <summary>The room the rounded corners need on each edge of a window of <paramref name="window"/>.</summary>
    public static EdgeMargins Corners(Vector2I window)
    {
        if (DevOptions.GetInt("screen-corners", 0) is > 0 and int radius)
        {
            return SafeMargins.ForCorners(window.X, window.Y, ScreenCorner.AllRounded(window.X, window.Y, radius));
        }
        if (!AndroidJava.IsAndroid || cornersUnavailable)
        {
            return EdgeMargins.None;
        }
        try
        {
            int[] numbers = AndroidJava.CallWithActivity(CornersClass, "read").AsInt32Array();
            if (numbers.Length != CornerNumbers || numbers[0] <= 0 || numbers[1] <= 0)
            {
                return EdgeMargins.None;
            }
            var corners = new ScreenCorner[4];
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = new ScreenCorner(numbers[2 + (3 * i)], numbers[3 + (3 * i)], numbers[4 + (3 * i)]);
            }
            // The corners are measured on the activity's view, which the game's window fills.
            return SafeMargins.ForCorners(numbers[0], numbers[1], corners);
        }
        catch (InvalidOperationException e)
        {
            // An APK without the helper (or Java refusing): no rounding is known, so none is kept clear.
            cornersUnavailable = true;
            GD.PushWarning($"Screen corners: {e.Message}");
            return EdgeMargins.None;
        }
    }
}
