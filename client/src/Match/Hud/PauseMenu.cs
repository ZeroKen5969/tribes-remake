using System;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>What the game menu can lead to.</summary>
public enum MenuChoice
{
    /// <summary>Back to the game.</summary>
    Resume,

    /// <summary>Save the game.</summary>
    Save,

    /// <summary>Load a game.</summary>
    Load,

    /// <summary>The match settings.</summary>
    Settings,

    /// <summary>Help and keys.</summary>
    Help,

    /// <summary>The mission's objective.</summary>
    Objective,

    /// <summary>Give up the match (network play).</summary>
    Forfeit,

    /// <summary>Back to the main menu.</summary>
    Quit,

    /// <summary>Test help (playing alone): explore the land around the camp at once.</summary>
    TestExplore,

    /// <summary>Development: win the mission now.</summary>
    CheatWin,

    /// <summary>Development: lose the mission now.</summary>
    CheatLose,
}

/// <summary>
/// The game menu: go on, game speed (single player), save and load (single player, or the host),
/// settings, help, the objective, giving up (network play) and leaving for the main menu.
/// Holds the game while open when playing alone.
/// </summary>
public partial class PauseMenu : HudPopup
{
    private readonly IMatchActions actions;
    private readonly bool canSave, canLoad;

    /// <summary>The game menu of the match <paramref name="actions"/> plays.</summary>
    public PauseMenu(IMatchActions actions, bool canSave, bool canLoad)
        : base("hud.menu", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), HoldReason.Menu)
    {
        this.actions = actions;
        this.canSave = canSave;
        this.canLoad = canLoad;
        Name = "PauseMenu";
        MaxWidth = 300;
    }

    /// <summary>An entry was picked.</summary>
    public event Action<MenuChoice>? Picked;

    /// <summary>A speed was picked (percent).</summary>
    public event Action<int>? SpeedPicked;

    /// <inheritdoc/>
    protected override void Fill()
    {
        HudMetrics m = Metrics;
        Add(m, PaintedIcon.Play, "hud.menu.resume", MenuChoice.Resume);
        if (actions.Runner.CanChangeSpeed)
        {
            HBoxContainer speeds = HudPanels.Row(m.Gap);
            speeds.Name = "Speeds";
            PaintedIcon[] icons = { PaintedIcon.Play, PaintedIcon.Fast, PaintedIcon.Fastest };
            int[] percents = { 100, 200, 400 };
            for (int i = 0; i < icons.Length; i++)
            {
                int percent = percents[i];
                Button button = HudWidgets.TextButton(m, HudArt.Painted(icons[i]), percent / 100 + "×", () => SpeedPicked?.Invoke(percent),
                    "Speed" + percent.ToString(CultureInfo.InvariantCulture));
                button.ToggleMode = true;
                button.SetPressedNoSignal(actions.Runner.SpeedPercent == percent);
                button.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
                speeds.AddChild(button);
            }
            Content.AddChild(speeds);
        }
        if (canSave)
        {
            Add(m, PaintedIcon.Save, "hud.menu.save", MenuChoice.Save);
        }
        if (canLoad)
        {
            Add(m, PaintedIcon.Save, "hud.menu.load", MenuChoice.Load);
        }
        if (Tribes.Client.Session.MatchSessions.CanInvite(actions.Runner))
        {
            var invite = new CheckButton
            {
                Name = "InviteToggle",
                Text = "hud.menu.invite",
                ButtonPressed = Tribes.Client.Session.MatchSessions.IsInviteOpen(actions.Runner),
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, Math.Max(PixelTheme.RowHeight, m.Button)),
            };
            invite.Toggled += on => Tribes.Client.Session.MatchSessions.SetInviteOpen(actions.Runner, on);
            Content.AddChild(invite);
        }
        Add(m, PaintedIcon.Menu, "hud.menu.settings", MenuChoice.Settings);
        Add(m, PaintedIcon.Help, "hud.help", MenuChoice.Help);
        Add(m, PaintedIcon.Objective, "hud.objective", MenuChoice.Objective);
        if (actions.Runner.IsNetwork && !actions.Spectating)
        {
            Add(m, PaintedIcon.Close, "hud.menu.forfeit", MenuChoice.Forfeit);
        }
        Add(m, PaintedIcon.ComeOut, "hud.menu.quit", MenuChoice.Quit);
        if (!actions.Runner.IsNetwork && !actions.Spectating)
        {
            // A test help in every build, only when playing alone (it changes the game outside the orders).
            Content.AddChild(HudPanels.Separator());
            Add(m, PaintedIcon.Objective, "hud.menu.test_explore", MenuChoice.TestExplore);
        }
        if (Dev.MatchDev.CheatsAllowed && !actions.Runner.IsNetwork)
        {
            Content.AddChild(HudPanels.Separator());
            Add(m, PaintedIcon.Objective, "hud.cheat.win", MenuChoice.CheatWin);
            Add(m, PaintedIcon.Close, "hud.cheat.lose", MenuChoice.CheatLose);
        }
    }

    private void Add(HudMetrics m, PaintedIcon icon, string key, MenuChoice choice) =>
        Content.AddChild(HudWidgets.TextButton(m, HudArt.Painted(icon), key, () => Picked?.Invoke(choice), choice + "Choice"));
}

/// <summary>
/// The settings that can change during a match, each shown at once: language, music volume, hints
/// (also told to the rules), the mouse at the window edge scrolling the map, the interface size,
/// whose task icons the map shows, whether a villager's panel shows the skills, window or full
/// screen (PC) and, with other people, the player list.
/// </summary>
public partial class MatchSettingsPanel : HudPopup
{
    private readonly IMatchActions actions;

    /// <summary>The settings panel of the match <paramref name="actions"/> plays.</summary>
    public MatchSettingsPanel(IMatchActions actions)
        : base("hud.menu.settings", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), HoldReason.Menu)
    {
        this.actions = actions;
        Name = "MatchSettings";
        MaxWidth = 360;
        // The volume is applied while it slides and written down once, when the panel closes.
        Closed += () => GameApp.Instance.Settings.Save();
    }

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameSettings settings = GameApp.Instance.Settings;
        string[] languages = new string[UiText.Languages.Count];
        int current = 0;
        for (int i = 0; i < languages.Length; i++)
        {
            languages[i] = UiText.Languages[i].NativeName;
            current = UiText.Languages[i].Code == settings.Language ? i : current;
        }
        OptionButton language = UiFactory.Options(languages, current, index =>
        {
            settings.Language = UiText.Languages[index].Code;
            Apply(settings);
        });
        language.Name = "Language";
        language.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        Content.AddChild(UiFactory.Row("settings.language", language));
        var music = new HSlider { Name = "Music", MinValue = 0, MaxValue = 1, Step = 0.05, Value = settings.MusicVolume, CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight) };
        music.ValueChanged += value =>
        {
            settings.MusicVolume = value;
            GameApp.Instance.ApplySettings();
        };
        Content.AddChild(UiFactory.Row("settings.music", music));
        var effects = new HSlider { Name = "Effects", MinValue = 0, MaxValue = 1, Step = 0.05, Value = settings.EffectsVolume, CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight) };
        effects.ValueChanged += value =>
        {
            settings.EffectsVolume = value;
            GameApp.Instance.ApplySettings();
            // A sample at the new volume (its cooldown keeps a drag from rattling).
            Tribes.Client.Audio.SoundEffects.Instance?.Play(Tribes.Client.MatchPlay.Logic.SoundCues.Hint);
        };
        Content.AddChild(UiFactory.Row("settings.effects", effects));
        var hints = new CheckBox { Name = "Hints", Text = "settings.hints", ButtonPressed = actions.World.Hints.Enabled };
        hints.Toggled += on =>
        {
            settings.ShowHints = on;
            settings.Save();
            if (!actions.Spectating)
            {
                actions.Send(new SetHints(actions.Tribe, on));
            }
        };
        Content.AddChild(hints);
        if (!Metrics.Touch)
        {
            var edge = new CheckBox { Name = "EdgeScroll", Text = "settings.edge_scroll", ButtonPressed = settings.EdgeScroll };
            edge.Toggled += on =>
            {
                settings.EdgeScroll = on;
                settings.Save();
            };
            Content.AddChild(edge);
        }
        string[] scales = { "settings.scale.1", "settings.scale.2", "settings.scale.3" };
        OptionButton scale = UiFactory.Options(scales, Math.Clamp(settings.InterfaceScale - 1, 0, 2), index =>
        {
            settings.InterfaceScale = index + 1;
            Apply(settings);
        });
        scale.Name = "InterfaceScale";
        Content.AddChild(UiFactory.Row("settings.scale", scale));
        if (Metrics.Touch || Tribes.Client.Ui.Screens.SettingsScreen.EdgeMarginOffered)
        {
            Content.AddChild(UiFactory.Row("settings.edge_margin", Tribes.Client.Ui.Screens.SettingsScreen.EdgeMargins(settings)));
        }
        OptionButton taskIcons = UiFactory.Options(["settings.task_icons.0", "settings.task_icons.1", "settings.task_icons.2"],
            Math.Clamp(settings.TaskIcons, 0, 2), index =>
            {
                settings.TaskIcons = index;
                settings.Save();
            });
        taskIcons.Name = "TaskIcons";
        Content.AddChild(UiFactory.Row("settings.task_icons", taskIcons));
        var skills = new CheckBox { Name = "UnitCardDetailed", Text = "settings.unit_card_detailed", ButtonPressed = settings.UnitCardDetailed };
        skills.Toggled += on =>
        {
            settings.UnitCardDetailed = on;
            settings.Save();
        };
        Content.AddChild(skills);
        if (GameApp.CanFullscreen)
        {
            GameApp app = GameApp.Instance;
            OptionButton display = UiFactory.Options(["settings.display.window", "settings.display.fullscreen"], app.IsFullscreen ? 1 : 0,
                index => app.SetFullscreen(index == 1));
            display.Name = "DisplayMode";
            void Follow() => display.Selected = app.IsFullscreen ? 1 : 0;
            app.FullscreenChanged += Follow;
            display.TreeExiting += () => app.FullscreenChanged -= Follow;
            Content.AddChild(UiFactory.Row("settings.display", display));
        }
        if (actions.Runner.IsNetwork)
        {
            var players = new CheckBox { Name = "ShowPlayers", Text = "settings.show_players", ButtonPressed = settings.ShowPlayers };
            players.Toggled += on =>
            {
                settings.ShowPlayers = on;
                settings.Save();
            };
            Content.AddChild(players);
        }
    }

    private static void Apply(GameSettings settings)
    {
        settings.Save();
        GameApp.Instance.ApplySettings();
    }
}
