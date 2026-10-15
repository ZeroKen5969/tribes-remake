using System;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Audio;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Settings: language (applied at once), music and effects volume, interface scale, window or full
/// screen (PC), hints, tutorial, edge scrolling (PC), the player list in matches with other people
/// and the player name. Every change is saved straight away.
/// </summary>
public partial class SettingsScreen : ScreenBase
{
    private const int MaxNameLength = 24;

    /// <inheritdoc/>
    protected override void Build()
    {
        VBoxContainer content = BuildSheet("settings.title", out _);
        VBoxContainer rows = UiFactory.Column(8);
        content.AddChild(UiFactory.Scroll(rows));
        GameSettings settings = Settings;

        var languages = new OptionButton { CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight), AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        for (int i = 0; i < UiText.Languages.Count; i++)
        {
            languages.AddItem(UiText.Languages[i].NativeName, i);
            if (UiText.Languages[i].Code == UiText.Find(settings.Language).Code)
            {
                languages.Selected = i;
            }
        }
        languages.GetPopup().AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        languages.ItemSelected += index =>
        {
            settings.Language = UiText.Languages[(int)index].Code;
            Save();
            GameApp.Instance.ApplySettings();
        };
        rows.AddChild(UiFactory.Row("settings.language", languages));

        rows.AddChild(VolumeRow("settings.music", settings.MusicVolume, value =>
        {
            settings.MusicVolume = value;
            MusicPlayer.Instance?.ApplyVolume();
        }));
        rows.AddChild(VolumeRow("settings.effects", settings.EffectsVolume, value => settings.EffectsVolume = value));

        OptionButton scale = UiFactory.Options(["settings.scale.1", "settings.scale.2", "settings.scale.3"], settings.InterfaceScale - 1, index =>
        {
            settings.InterfaceScale = index + 1;
            Save();
            GameApp.Instance.ApplySettings();
        });
        rows.AddChild(UiFactory.Row("settings.scale", scale));
        if (GameApp.CanFullscreen)
        {
            rows.AddChild(UiFactory.Row("settings.display", DisplayModes()));
        }

        rows.AddChild(Toggle("settings.hints", settings.ShowHints, on => settings.ShowHints = on));
        rows.AddChild(Toggle("settings.tutorial", settings.Tutorial, on => settings.Tutorial = on));
        if (OS.GetName() != "Android")
        {
            rows.AddChild(Toggle("settings.edge_scroll", settings.EdgeScroll, on => settings.EdgeScroll = on));
        }
        rows.AddChild(Toggle("settings.show_players", settings.ShowPlayers, on => settings.ShowPlayers = on));

        var name = new LineEdit
        {
            Text = settings.PlayerName,
            MaxLength = MaxNameLength,
            PlaceholderText = "settings.player_name_hint",
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
            SelectAllOnFocus = true,
        };
        void SaveName()
        {
            string value = name.Text.Trim();
            if (value.Length == 0)
            {
                name.Text = settings.PlayerName;
                return;
            }
            settings.PlayerName = value;
            Save();
        }
        name.TextSubmitted += _ => SaveName();
        name.FocusExited += SaveName;
        name.TextChanged += text =>
        {
            // Saved as typed, so leaving the screen at once keeps it; an empty name is not saved.
            if (text.Trim().Length > 0)
            {
                settings.PlayerName = text.Trim();
                Save();
            }
        };
        rows.AddChild(UiFactory.Row("settings.player_name", name));
        rows.AddChild(UiFactory.Label("settings.note", PixelTheme.Variations.Muted, wrap: true));

        languages.CallDeferred(Control.MethodName.GrabFocus);
        if (Dev.DevOptions.Get("ui-switch-lang") is { } switchTo)
        {
            // Development: change the language a second after opening, without saving it.
            GetTree().CreateTimer(1.0).Timeout += () =>
            {
                settings.Language = switchTo;
                GameApp.Instance.ApplySettings();
                GD.Print($"UI language switched to {UiText.Current}");
            };
        }
        if (Dev.DevOptions.Has("ui-popup"))
        {
            // Development: show the option list open, to check the popup style.
            languages.CallDeferred(OptionButton.MethodName.ShowPopup);
        }
    }

    /// <summary>The window or full screen choice (PC); it follows F11 and Alt+Enter while the screen is open.</summary>
    private OptionButton DisplayModes()
    {
        GameApp app = GameApp.Instance;
        OptionButton modes = UiFactory.Options(["settings.display.window", "settings.display.fullscreen"], app.IsFullscreen ? 1 : 0,
            index => app.SetFullscreen(index == 1));
        modes.Name = "DisplayMode";
        void Follow() => modes.Selected = app.IsFullscreen ? 1 : 0;
        app.FullscreenChanged += Follow;
        modes.TreeExiting += () => app.FullscreenChanged -= Follow;
        return modes;
    }

    private HBoxContainer VolumeRow(string key, double value, Action<double> changed)
    {
        var line = UiFactory.Line(6);
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 5,
            Value = Math.Round(value * 100),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(100, 18),
            FocusMode = FocusModeEnum.All,
        };
        Label label = UiFactory.Plain(Percent(slider.Value));
        label.CustomMinimumSize = new Vector2(40, 0);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            label.Text = Percent(v);
            changed(v / 100.0);
            Save();
        };
        line.AddChild(slider);
        line.AddChild(label);
        return UiFactory.Row(key, line);
    }

    private static string Percent(double value) => ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture) + "%";

    private static CheckBox Toggle(string key, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Text = key, ButtonPressed = on, CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight) };
        box.Toggled += value =>
        {
            changed(value);
            Save();
        };
        return box;
    }

    private static void Save() => GameApp.Instance.Settings.Save();
}
