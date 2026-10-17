using System;
using Godot;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// Sizes of the match interface in logical pixels. On touch screens buttons are at least about
/// 9 mm (a fingertip) on the real screen, whatever its density and the pixel scale; with a mouse
/// they are compact. Icons are drawn at a whole-number scale that fits the button.
/// </summary>
public sealed record HudMetrics
{
    /// <summary>A fingertip on a touch screen.</summary>
    public const float TouchTargetMillimetres = 9f;

    /// <summary>The smallest button a finger still presses reliably (Android's 48 dp).</summary>
    public const float SmallestTouchMillimetres = 7.6f;

    /// <summary>Button size with a mouse.</summary>
    public const int MouseButton = 24;

    private const int SmallestTouchButton = 30, LargestTouchButton = 60;
    private const float MillimetresPerInch = 25.4f;

    /// <summary>Whether the screen is touched rather than clicked.</summary>
    public bool Touch { get; init; }

    /// <summary>Side of a square button.</summary>
    public int Button { get; init; } = MouseButton;

    /// <summary>Scale of the original icons on buttons.</summary>
    public int IconScale { get; init; } = 1;

    /// <summary>Whether the screen is taller than wide.</summary>
    public bool Portrait { get; init; }

    /// <summary>The logical screen size.</summary>
    public Vector2 Screen { get; init; }

    /// <summary>Logical pixels per millimetre of the real screen.</summary>
    public float PixelsPerMillimetre { get; init; } = 4;

    /// <summary>
    /// The smallest a row of buttons may shrink to so all of them fit on the screen: about 7.6 mm
    /// with a finger (never more than <see cref="Button"/>), the usual size with a mouse.
    /// </summary>
    public int SmallestButton => Touch
        ? Math.Clamp((int)MathF.Ceiling(SmallestTouchMillimetres * PixelsPerMillimetre), SmallestTouchButton, Math.Max(SmallestTouchButton, Button))
        : Button;

    /// <summary>Gap between controls.</summary>
    public int Gap => Touch ? 3 : 2;

    /// <summary>The whole-number scale of the original icons on a button of <paramref name="button"/> pixels.</summary>
    public static int IconScaleFor(int button) => Math.Max(1, button / 22);

    /// <summary>Height of the top bar.</summary>
    public int TopBar => Touch ? Math.Max(22, Button * 2 / 3) : 20;

    /// <summary>Width of the side column in landscape.</summary>
    public int SideColumn => Math.Clamp((int)(Screen.X * 0.24f), Touch ? 5 * Button + (6 * Gap) : 150, Touch ? 6 * Button + (7 * Gap) : 190);

    /// <summary>The metrics for a logical screen of <paramref name="screen"/> at pixel scale <paramref name="pixelScale"/>.</summary>
    public static HudMetrics For(Vector2 screen, float pixelScale, bool touch, float dpi)
    {
        float scale = Math.Max(1, pixelScale);
        float perMm = (dpi > 0 ? dpi : 96) / MillimetresPerInch / scale;
        int button = MouseButton;
        if (touch)
        {
            button = Math.Clamp((int)MathF.Ceiling(TouchTargetMillimetres * perMm), SmallestTouchButton, LargestTouchButton);
            // Still fits seven buttons across a narrow portrait screen.
            button = Math.Min(button, Math.Max(SmallestTouchButton, (int)(Math.Min(screen.X, screen.Y) / 7.5f)));
        }
        int iconScale = IconScaleFor(button);
        return new HudMetrics
        {
            Touch = touch,
            Button = button,
            IconScale = iconScale,
            Portrait = screen.Y > screen.X,
            Screen = screen,
            PixelsPerMillimetre = perMm,
        };
    }

    /// <summary>The metrics of the running window.</summary>
    public static HudMetrics Current(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        Vector2 screen = viewport.GetVisibleRect().Size;
        float scale = viewport.GetWindow()?.ContentScaleFactor ?? 1;
        bool touch = DisplayServer.IsTouchscreenAvailable() || OS.HasFeature("mobile") || Dev.MatchDev.ForceTouch;
        int screenIndex = DisplayServer.WindowGetCurrentScreen();
        float dpi = Dev.MatchDev.ForcedDpi > 0 ? Dev.MatchDev.ForcedDpi : DisplayServer.ScreenGetDpi(screenIndex);
        return For(screen, scale, touch, dpi);
    }
}
