using System;
using Godot;
using Tribes.Client.Audio;
using Tribes.Client.Platform;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Text;
using Tribes.Core.Content;
using Tribes.Update;

namespace Tribes.Client.App;

/// <summary>
/// The application: settings, game content and moving between screens. Registered as the
/// autoload "App", so it lives for the whole run.
/// </summary>
public partial class GameApp : Node
{
    private GameContent? content;

    /// <summary>The running application.</summary>
    public static GameApp Instance { get; private set; } = null!;

    /// <summary>The player's settings.</summary>
    public GameSettings Settings { get; private set; } = new();

    /// <summary>The game content, loaded on first use.</summary>
    public GameContent Content => content ??= ContentStore.Load();

    /// <summary>The update service behind the "check for updates" button; null when this build cannot update itself.</summary>
    public UpdateManager? Updates { get; private set; }

    /// <summary>Why <see cref="Updates"/> is null.</summary>
    public UpdateException? UpdatesUnavailable { get; private set; }

    /// <summary>The outcome of the last Windows app update (read once at start), for the main menu to show.</summary>
    public WindowsSwapResult? LastAppUpdate { get; set; }

    /// <summary>
    /// The content revision running now: the loaded content pack's, else the built-in one. A pack
    /// applied later counts only after the restart.
    /// </summary>
    public int ContentRevision { get; private set; } = BuildInfo.Current.ContentRevision;

    /// <summary>The match the menus ask for; the match screen reads it when it opens.</summary>
    public MatchRequest? Match { get; set; }

    /// <inheritdoc/>
    public override void _EnterTree()
    {
        Instance = this;
        Settings = GameSettings.Load();
        // Boot step: write the settings back once, so a first run keeps its player key (PlayerId)
        // across restarts; reconnecting and resuming a hosted save depend on it.
        Settings.Save();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(ScreenshotArgument, StringComparison.Ordinal))
            {
                screenshotPath = arg[ScreenshotArgument.Length..];
            }
        }
        LoadContentPack();
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        GetTree().Root.SizeChanged += FitPixelScale;
        ApplySettings();
        // Development screenshots of any scene ("--ui-capture=PATH", see Ui/Dev/UiCapture.cs).
        UiCapture.Install(GetTree().Root);
    }

    /// <summary>
    /// Boot step: mounts a downloaded content pack (docs/UPDATES.md) before any game scene or
    /// content is loaded, and picks up the result of a finished Windows app update.
    /// </summary>
    private void LoadContentPack()
    {
        try
        {
            UpdateManagerOptions options = UpdatePlatformOptions.Create();
            Updates = UpdateManager.CreateDefault(options);
            string? pack = Updates.GetContentPackToLoad();
            if (pack is not null && !ProjectSettings.LoadResourcePack(pack))
            {
                GD.PushWarning($"content pack {pack} could not be loaded; using the built-in content");
                Updates.ContentPacks.Uninstall();
            }
            else if (pack is not null)
            {
                GD.Print($"content pack loaded: {pack}");
            }
            ContentRevision = Updates.ContentPacks.GetCurrentRevision(options.AppVersion, options.BuiltInContentRevision);
            if (options.Windows is not null)
            {
                var updater = new WindowsAppUpdater(options.Windows);
                LastAppUpdate = updater.ReadLastResult();
                updater.ClearLastResult();
            }
        }
        catch (Exception e) when (e is UpdateException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            // The game runs without in-game updates; the update screen tells the player why.
            UpdatesUnavailable = e as UpdateException ?? new UpdateException(UpdateErrorKind.StorageError, e.Message, e);
            GD.Print($"updates unavailable: {e.Message}");
        }
    }

    /// <summary>Applies the settings that change how the game looks and sounds: full screen, interface scale, language, music volume.</summary>
    public void ApplySettings()
    {
        ApplyWindowMode();
        FitPixelScale();
        UiText.Apply(Settings.Language);
        MusicPlayer.Instance?.ApplyVolume();
    }

    /// <summary>The window went between full screen and a window (the setting, F11 or Alt+Enter).</summary>
    public event Action? FullscreenChanged;

    /// <summary>Whether this platform has a window that can fill the screen (PC).</summary>
    public static bool CanFullscreen => OS.HasFeature("pc");

    /// <summary>Whether the game fills the screen now.</summary>
    public bool IsFullscreen => GetTree().Root.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;

    /// <summary>Fills the screen or goes back to a window, keeps the choice and applies it at once.</summary>
    public void SetFullscreen(bool on)
    {
        Settings.Fullscreen = on;
        Settings.Save();
        ApplyWindowMode();
    }

    /// <summary>
    /// Shows the window as the setting asks. Full screen is Godot's borderless full screen (the
    /// desktop's resolution, no mode change), so switching to other windows needs no screen mode
    /// change; going back restores the window as it was (maximized included). Development window
    /// sizes ("--ui-window", "--resize-to") keep the window.
    /// </summary>
    private void ApplyWindowMode()
    {
        if (!CanFullscreen || DevOptions.Has("ui-window") || DevOptions.Has("resize-to"))
        {
            return;
        }
        Window root = GetTree().Root;
        bool full = IsFullscreen;
        if (Settings.Fullscreen == full)
        {
            return;
        }
        if (Settings.Fullscreen)
        {
            windowedMode = root.Mode == Window.ModeEnum.Maximized ? Window.ModeEnum.Maximized : Window.ModeEnum.Windowed;
            root.Mode = Window.ModeEnum.Fullscreen;
        }
        else
        {
            root.Mode = windowedMode;
        }
        GD.Print($"WINDOW_MODE {(Settings.Fullscreen ? "fullscreen" : "window")} {root.Size.X}x{root.Size.Y}");
        FullscreenChanged?.Invoke();
    }

    private Window.ModeEnum windowedMode = Window.ModeEnum.Windowed;

    /// <inheritdoc/>
    public override void _Input(InputEvent @event)
    {
        // F11 and Alt+Enter switch between full screen and a window on every screen, before any
        // control takes the key (Enter would press the focused button).
        if (CanFullscreen && @event is InputEventKey { Pressed: true, Echo: false } key
            && (key.Keycode == Key.F11 || (key.AltPressed && key.Keycode is Key.Enter or Key.KpEnter)))
        {
            SetFullscreen(!IsFullscreen);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Shows the pixel art at the largest whole-number scale that keeps at least
    /// <see cref="ShortSidePixels"/> logical pixels on the screen's short side, in portrait and
    /// landscape alike, and fills the whole window at any size: the logical screen is the window
    /// divided by the scale, so when the window is not a multiple of the scale its last logical
    /// pixel is only partly shown instead of leaving a border. Runs again on every resize.
    /// </summary>
    private void FitPixelScale()
    {
        Window root = GetTree().Root;
        Vector2I size = root.Size;
        int shortSide = Math.Min(size.X, size.Y);
        int scale = Math.Clamp(shortSide / ShortSidePixels, 1, MaxPixelScale) * Math.Max(1, Settings.InterfaceScale);
        scale = Math.Clamp(scale, 1, Math.Max(1, shortSide / MinShortSidePixels));
        // A zero base size makes the factor the exact scale; a base size would be fitted with
        // whole-number stretching and leave up to scale - 1 black pixels at odd window sizes.
        root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        root.ContentScaleSize = Vector2I.Zero;
        root.ContentScaleFactor = scale;
        // Positions centred in a logical size that ends in a fraction land on whole screen pixels.
        root.Snap2DTransformsToPixel = true;
    }

    /// <summary>Logical pixels wanted on the short side of the screen.</summary>
    private const int ShortSidePixels = 340;

    /// <summary>Fewest logical pixels allowed on the short side when the player enlarges the interface.</summary>
    private const int MinShortSidePixels = 200;

    private const int MaxPixelScale = 8;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Development aid: "-- --screenshot=PATH" saves the screen after a few frames and quits,
        // so screens can be checked without a person looking at them.
        if (screenshotPath != null && ++framesShown == ScreenshotFrame)
        {
            Error result = GetViewport().GetTexture().GetImage().SavePng(screenshotPath);
            GD.Print($"SCREENSHOT {screenshotPath} {result}");
            GetTree().Quit();
        }
    }

    private const string ScreenshotArgument = "--screenshot=";
    private const int ScreenshotFrame = 30;
    private string? screenshotPath;
    private int framesShown;

    /// <summary>Shows the screen in <paramref name="scenePath"/> instead of the current one.</summary>
    public void Show(string scenePath)
    {
        Error result = GetTree().ChangeSceneToFile(scenePath);
        if (result != Error.Ok)
        {
            throw new InvalidOperationException($"cannot open {scenePath}: {result}");
        }
    }
}
