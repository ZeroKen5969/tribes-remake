using System;
using Godot;
using Tribes.Client.Ui.Screens;

namespace Tribes.Client.Ui.Dev;

/// <summary>
/// Development aid for "--ui-capture=PATH": once the current screen has settled and
/// "--ui-capture-delay" more frames have passed, saves the window to PATH and quits.
/// <list type="bullet">
/// <item>"--resize-to=WxH" first resizes the window while the screen is shown, then captures.</item>
/// <item>"--boot-hold" keeps the boot screen up so it can be captured.</item>
/// <item>Scenes that are not menu screens (map views) are captured after the delay.</item>
/// </list>
/// After saving it prints <c>UI_EDGES</c> with the colours of the window's outer pixels as the
/// desktop shows them, so a border left by the pixel scaling would show up there.
/// </summary>
public partial class UiCapture : Node
{
    private const int AnySceneFrames = 90;

    private static bool installed;

    private string path = "";
    private int delay;
    private int waited;
    private int frames;
    private Vector2I? resizeTo;
    private bool resized;
    private ulong startedMsec;

    /// <summary>Adds the capture node to the tree when the option is given (once).</summary>
    public static void Install(Node root)
    {
        string? path = DevOptions.Get("ui-capture");
        if (installed || string.IsNullOrEmpty(path))
        {
            return;
        }
        installed = true;
        var capture = new UiCapture { Name = "UiCapture", path = path, delay = DevOptions.GetInt("ui-capture-delay", 20) };
        capture.resizeTo = ParseSize(DevOptions.Get("resize-to"));
        root.CallDeferred(Node.MethodName.AddChild, capture);
        // "--ui-window=WxH": a borderless window of exactly that size, even taller than the desktop
        // (a framed window is clamped to the screen, which spoils portrait captures).
        if (ParseSize(DevOptions.Get("ui-window")) is { } size)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(size);
        }
    }

    private static Vector2I? ParseSize(string? text)
    {
        string[] parts = (text ?? "").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height)
            ? new Vector2I(width, height)
            : null;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        startedMsec = Time.GetTicksMsec();
    }

    private bool Settled()
    {
        if (BootScreen.Holding)
        {
            return true;
        }
        if (ScreenBase.Current is { } screen)
        {
            return screen.IsSettled;
        }
        // Not a menu screen (for example a map view): it is ready after a while.
        return frames > AnySceneFrames && GetTree().CurrentScene is not BootScreen;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        frames++;
        bool timedOut = Time.GetTicksMsec() - startedMsec > 90_000;
        if (!Settled() && !timedOut)
        {
            waited = 0;
            return;
        }
        if (++waited < delay)
        {
            return;
        }
        if (resizeTo is { } size && !resized)
        {
            resized = true;
            waited = 0;
            GD.Print($"UI_RESIZE {DisplayServer.WindowGetSize()} -> {size}");
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetSize(size);
            return;
        }
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(path);
        ScreenBase? current = ScreenBase.Current;
        GD.Print($"UI_CAPTURE {path} {result} {image.GetWidth()}x{image.GetHeight()} screen={current?.GetType().Name ?? GetTree().CurrentScene?.Name} settled={Settled()}");
        PrintEdges();
        if (DevOptions.Has("ui-reach"))
        {
            // "--ui-reach": whether every button shown can be pressed (UI_REACH lines).
            UiReach.Print(UiReach.Check(GetTree().Root, UiReach.SafeScreen(GetViewport())), current?.GetType().Name ?? GetTree().CurrentScene?.Name ?? "-");
        }
        SetProcess(false);
        // "--ui-capture-linger=SECONDS" keeps running a while (another instance may still need this one).
        int linger = DevOptions.GetInt("ui-capture-linger", 0);
        SceneTree tree = GetTree();
        if (linger > 0)
        {
            tree.CreateTimer(linger).Timeout += () => tree.Quit();
        }
        else
        {
            tree.Quit();
        }
    }

    /// <summary>Prints the outermost pixels of the window as the desktop shows them.</summary>
    private static void PrintEdges()
    {
        Vector2I at = DisplayServer.WindowGetPosition();
        Vector2I size = DisplayServer.WindowGetSize();
        Vector2I screen = DisplayServer.ScreenGetSize();
        Rect2I visible = new Rect2I(at, size).Intersection(new Rect2I(DisplayServer.ScreenGetPosition(), screen));
        Image? shown = visible.Size.X > 2 && visible.Size.Y > 2 ? DisplayServer.ScreenGetImageRect(visible) : null;
        if (shown is null)
        {
            GD.Print("UI_EDGES unavailable");
            return;
        }
        int w = shown.GetWidth(), h = shown.GetHeight();
        string Colour(int x, int y) => shown.GetPixel(Math.Clamp(x, 0, w - 1), Math.Clamp(y, 0, h - 1)).ToHtml(false);
        GD.Print($"UI_EDGES window={size} checked={visible.Size} left={Colour(0, h / 2)} right={Colour(w - 1, h / 2)} top={Colour(w / 2, 0)} bottom={Colour(w / 2, h - 1)} corner={Colour(w - 1, h - 1)}");
    }
}
