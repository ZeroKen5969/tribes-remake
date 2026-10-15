using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Audio;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// The first scene. GameApp has already mounted a downloaded content pack (if any); this screen
/// shows the original title picture while it loads the interface art and theme and the game
/// content, starts the menu music, and then opens the main menu.
/// </summary>
public partial class BootScreen : Control
{
    private const string FontPath = "res://assets/fonts/cubic-11/Cubic_11.ttf";

    private readonly Queue<(string Key, Action Step)> steps = new();
    private Label status = null!;
    private int framesShown;
    private bool failed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        UiCapture.Install(GetTree().Root);
        string? language = DevOptions.Get("ui-lang");
        if (language is not null)
        {
            UiText.Apply(language);
        }
        AddChild(new SplashBackdrop { Name = "Splash" });

        status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        status.AddThemeFontOverride("font", GD.Load<Font>(FontPath));
        status.AddThemeFontSizeOverride("font_size", PixelTheme.BodySize);
        status.AddThemeColorOverride("font_color", UiPalette.Paper);
        status.AddThemeColorOverride("font_outline_color", UiPalette.Ink);
        status.AddThemeConstantOverride("outline_size", 2);
        AddChild(status);
        status.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        status.OffsetTop = -22;
        status.OffsetBottom = -6;

        steps.Enqueue(("boot.loading_art", () => _ = PixelTheme.Current));
        steps.Enqueue(("boot.loading_content", () => _ = GameApp.Instance.Content));
        steps.Enqueue(("boot.starting", () => MusicPlayer.Instance?.PlayMood(MusicPlayer.Normal)));
        status.Text = UiText.T(steps.Peek().Key);
    }

    /// <summary>"--boot-hold": the boot screen stays up after loading (for development screenshots).</summary>
    public static bool Holding { get; private set; }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Let the picture show before the slow steps run.
        if (failed || ++framesShown < 3)
        {
            return;
        }
        if (steps.Count == 0)
        {
            SetProcess(false);
            if (DevOptions.Has("boot-hold"))
            {
                Holding = true;
                return;
            }
            OpenFirstScreen();
            return;
        }
        (string key, Action step) = steps.Dequeue();
        try
        {
            step();
        }
        catch (Exception e)
        {
            failed = true;
            GD.PushError($"boot step {key} failed: {e}");
            status.Text = UiText.F("boot.failed", e.Message);
            return;
        }
        if (steps.Count > 0)
        {
            status.Text = UiText.T(steps.Peek().Key);
        }
    }

    private static void OpenFirstScreen()
    {
        GD.Print("TRIBES_MENU_READY");
        // The rooms' map capacities, worked out in the background while the menus show.
        Game.MissionCatalog.PrepareCapacities();
        if (DevOptions.Has("lan-host") || DevOptions.Has("lan-join"))
        {
            LobbyDev.Start();
            return;
        }
        string? screen = DevOptions.Get("ui-screen");
        UiScreens.Go(screen is null ? UiScreens.MainMenu : "res://scenes/ui/" + screen + ".tscn");
    }
}
