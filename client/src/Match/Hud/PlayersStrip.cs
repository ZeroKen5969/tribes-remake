using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Session;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Client.World;
using Tribes.Core.World;
using Tribes.Session;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The tribes of a match with other people, in a corner under the minimap: each tribe's colour,
/// its player's name and how it is played (「離線」 while its player is away — the tribe then just
/// gets no orders). A small button above it shows or hides the list (also the Tab key); the choice
/// is kept in the settings (<see cref="GameSettings.ShowPlayers"/>). Hidden when playing alone.
/// </summary>
public partial class PlayersStrip : VBoxContainer
{
    /// <summary>Widest a name is shown; longer ones end in "…".</summary>
    private const int MaxNameWidth = 84;

    private readonly IMatchActions actions;
    private readonly PanelContainer list = new() { Name = "PlayersList", MouseFilter = MouseFilterEnum.Ignore };
    private readonly VBoxContainer rows = new() { MouseFilter = MouseFilterEnum.Ignore };
    private Button? toggle;
    private int toggleSize;
    private int shownHash;
    private bool shownExpanded;

    /// <summary>A strip for the match <paramref name="actions"/> plays.</summary>
    public PlayersStrip(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>Whether the list is open (the setting).</summary>
    public static bool Expanded => GameApp.Instance.Settings.ShowPlayers;

    /// <summary>The name shown for <paramref name="tribe"/>: its player's, else the computer's.</summary>
    public static string TribeName(IMatchActions actions, int tribe)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (PlayerEntry player in actions.Runner.Players)
        {
            if (player.Tribe == tribe && !player.IsComputer && player.Name.Length > 0)
            {
                return player.Name;
            }
        }
        GameWorld world = actions.World;
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].Controller.Kind != TribeControl.Computer
            ? UiText.F("player.tribe_numbered", tribe + 1)
            : ComputerName(world, tribe);
    }

    /// <summary>
    /// A computer tribe's name: "電腦" when it is the only one, else numbered in tribe order
    /// ("電腦 2"), so a world with many of them tells them apart.
    /// </summary>
    private static string ComputerName(GameWorld world, int tribe)
    {
        int computers = 0, number = 0;
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            if (world.Tribes[t].Controller.Kind == TribeControl.Computer)
            {
                computers++;
                number = t <= tribe ? computers : number;
            }
        }
        return computers <= 1 ? UiText.T("player.computer") : UiText.F("player.computer_numbered", Math.Max(1, number));
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "Players";
        MouseFilter = MouseFilterEnum.Ignore;
        // Wider rows than the minimap grow the list to the left, so it stays against the screen edge.
        GrowHorizontal = GrowDirection.Begin;
        AddThemeConstantOverride("separation", 1);
        rows.AddThemeConstantOverride("separation", 1);
        list.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        list.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(UiPalette.Dark, 0.55f),
            ContentMarginLeft = 3,
            ContentMarginRight = 3,
            ContentMarginTop = 1,
            ContentMarginBottom = 2,
        });
        list.AddChild(rows);
        AddChild(list);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // The setting can also change from the settings panel.
        if (Visible && shownExpanded != Expanded)
        {
            Refresh();
        }
    }

    /// <summary>Opens or closes the list and keeps the choice.</summary>
    public void Toggle()
    {
        GameSettings settings = GameApp.Instance.Settings;
        settings.ShowPlayers = !settings.ShowPlayers;
        settings.Save();
        Refresh();
    }

    /// <summary>Shows the players as they are now.</summary>
    public void Refresh()
    {
        Visible = actions.Runner.IsNetwork;
        if (!Visible)
        {
            return;
        }
        FitToggle();
        bool expanded = Expanded;
        toggle!.SetPressedNoSignal(expanded);
        list.Visible = expanded;
        var hash = new HashCode();
        hash.Add(expanded);
        hash.Add(UiText.Current);
        foreach (PlayerEntry player in actions.Runner.Players)
        {
            hash.Add(player);
        }
        int value = hash.ToHashCode();
        if (value == shownHash && shownExpanded == expanded)
        {
            return;
        }
        shownHash = value;
        shownExpanded = expanded;
        HudWidgets.Clear(rows);
        if (!expanded)
        {
            return;
        }
        var listed = new List<PlayerEntry>();
        var computers = new List<bool>();
        foreach (PlayerEntry player in actions.Runner.Players)
        {
            if (player.Tribe < 0 && player.State != PlayerState.Spectator)
            {
                continue;
            }
            listed.Add(player);
            computers.Add(player.IsComputer);
        }
        (IReadOnlyList<int> shown, int left) = PlayerList.Pick(computers);
        foreach (int index in shown)
        {
            rows.AddChild(Row(listed[index]));
        }
        if (left > 0)
        {
            rows.AddChild(Text(UiText.F("hud.players.more", left), muted: true));
        }
    }

    /// <summary>The button that opens and closes the list (made again when the button size changes).</summary>
    private void FitToggle()
    {
        HudMetrics m = actions.Metrics;
        if (toggle is not null && toggleSize == m.Button)
        {
            return;
        }
        if (toggle is not null)
        {
            RemoveChild(toggle);
            toggle.QueueFree();
        }
        toggle = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Players), "hud.players.toggle", Toggle, name: "PlayersToggle");
        toggle.ToggleMode = true;
        toggle.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        AddChild(toggle);
        MoveChild(toggle, 0);
        toggleSize = m.Button;
    }

    private Control Row(PlayerEntry player)
    {
        HBoxContainer row = HudPanels.Row(3);
        int colour = Math.Clamp(player.Tribe >= 0 ? actions.ColourOf(player.Tribe) : 0, 0, 7);
        var swatch = new Panel
        {
            CustomMinimumSize = new Vector2(7, 7),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        swatch.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = player.Tribe >= 0 ? new Color(WorldView.Colours.Colours[colour].Swatch) : UiPalette.Clear,
            BorderColor = player.Tribe >= 0 ? UiPalette.Ink : UiPalette.Clear,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        row.AddChild(swatch);
        Label name = Text(player.Tribe >= 0 ? TribeName(actions, player.Tribe) : player.Name, muted: false);
        float width = name.GetThemeFont("font").GetStringSize(name.Text, HorizontalAlignment.Left, -1, name.GetThemeFontSize("font_size")).X;
        if (width > MaxNameWidth)
        {
            name.CustomMinimumSize = new Vector2(MaxNameWidth, 0);
            name.ClipText = true;
            name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        row.AddChild(name);
        if (player.State != PlayerState.Playing && !player.IsComputer)
        {
            Label state = Text(SessionText.State(player), muted: true);
            state.Modulate = player.State == PlayerState.Absent ? new Color(1, 0.75f, 0.6f) : new Color(1, 1, 1, 0.8f);
            row.AddChild(state);
        }
        return row;
    }

    private static Label Text(string text, bool muted)
    {
        Label label = HudWidgets.Value(text, PixelTheme.Variations.OnDark);
        if (muted)
        {
            label.Modulate = new Color(1, 1, 1, 0.8f);
        }
        return label;
    }
}
