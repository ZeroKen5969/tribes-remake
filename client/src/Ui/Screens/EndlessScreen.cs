using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Net;

namespace Tribes.Client.Ui.Screens;

/// <summary>Defaults of a new endless world.</summary>
public static class EndlessDefaults
{
    /// <summary>Start area sizes offered, in cells (camps keep 80 cells apart; the rules make 128 by default and more land as camps need it).</summary>
    public static readonly int[] StartAreaSizes = [128, 192, 256];

    /// <summary>The start area size picked first (the rules' own).</summary>
    public const int StartAreaSize = 128;

    /// <summary>The opening grace picked first, in minutes (the endless rules' own).</summary>
    public const int GraceMinutes = 15;

    /// <summary>Computer tribes at the start, picked first (there is no maximum).</summary>
    public const int ComputerTribes = 2;
}

/// <summary>
/// Endless mode: a new world (seed, start area, computer tribes), continuing a saved world, or
/// opening a LAN room so friends can join the world, also after it has started.
/// </summary>
public partial class EndlessScreen : ScreenBase
{
    private const int MaxSeedLength = 9;

    private LineEdit seed = null!;
    private EndlessOptions options = null!;
    private VBoxContainer saves = null!;
    private TabContainer tabs = null!;

    /// <inheritdoc/>
    protected override void Build()
    {
        VBoxContainer content = BuildSheet("endless.title", out _);
        tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(tabs);
        tabs.AddChild(BuildNewWorld());
        tabs.AddChild(BuildContinue());
        tabs.AddChild(BuildInvite());
        tabs.SetTabTitle(0, "endless.tab_new");
        tabs.SetTabTitle(1, "endless.tab_continue");
        tabs.SetTabTitle(2, "endless.tab_invite");
        if (Dev.DevOptions.Get("ui-tab") is { } tab)
        {
            tabs.CurrentTab = tab switch { "continue" => 1, "invite" => 2, _ => 0 };
        }
    }

    private Control BuildNewWorld()
    {
        VBoxContainer page = UiFactory.Column(8);
        page.Name = "New";
        // The choices scroll (they outgrow a small window); the start button stays below them in view.
        VBoxContainer rows = UiFactory.Column(8);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ScrollContainer scroll = UiFactory.Scroll(rows);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        page.AddChild(scroll);
        rows.AddChild(UiFactory.Label("endless.new_intro", PixelTheme.Variations.Muted, wrap: true));
        HBoxContainer seedLine = UiFactory.Line(6);
        seed = new LineEdit
        {
            Text = RandomSeed(),
            MaxLength = MaxSeedLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number,
        };
        seedLine.AddChild(seed);
        seedLine.AddChild(UiFactory.Button("endless.random_seed", () => seed.Text = RandomSeed()));
        rows.AddChild(UiFactory.Row("endless.seed", seedLine));
        options = new EndlessOptions();
        rows.AddChild(options);
        page.AddChild(UiFactory.Button("endless.start_new", StartNew, PixelTheme.Variations.Primary));
        return page;
    }

    private Control BuildContinue()
    {
        VBoxContainer page = UiFactory.Column(6);
        page.Name = "Continue";
        page.AddChild(UiFactory.Label("endless.continue_intro", PixelTheme.Variations.Muted, wrap: true));
        saves = UiFactory.Column(4);
        PanelContainer box = UiFactory.Panel(PixelTheme.Variations.Inset, UiFactory.Scroll(saves));
        box.SizeFlagsVertical = SizeFlags.ExpandFill;
        page.AddChild(box);
        ShowSaves();
        return page;
    }

    private void ShowSaves()
    {
        IReadOnlyList<SaveInfo> list = SaveCatalog.List(MatchMode.Endless);
        if (list.Count == 0)
        {
            Label none = UiFactory.Label("endless.no_saves", PixelTheme.Variations.Muted, wrap: true);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            saves.AddChild(none);
            return;
        }
        foreach (SaveInfo save in list)
        {
            HBoxContainer row = UiFactory.Line(6);
            VBoxContainer text = UiFactory.Column(0);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(UiFactory.Cell(save.Title.Length > 0 ? save.Title : UiText.T("endless.world")));
            text.AddChild(UiFactory.Cell(UiText.F("load.details", save.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), SaveCatalog.PlayTime(save.PlaySeconds)), PixelTheme.Variations.Muted));
            if (SaveSettingsText.Line(save) is { } settings)
            {
                text.AddChild(settings);
            }
            row.AddChild(text);
            row.AddChild(UiFactory.Button("endless.continue", () => Continue(save)));
            saves.AddChild(UiFactory.Panel(PixelTheme.Variations.Slot, row));
        }
    }

    private Control BuildInvite()
    {
        VBoxContainer page = UiFactory.Column(8);
        page.Name = "Invite";
        page.AddChild(UiFactory.Label("endless.invite_intro", null, wrap: true));
        page.AddChild(UiFactory.Label("endless.invite_note", PixelTheme.Variations.Muted, wrap: true));
        page.AddChild(UiFactory.Spacer());
        page.AddChild(UiFactory.Button("endless.invite_host", HostEndlessRoom, PixelTheme.Variations.Primary));
        return page;
    }

    private void HostEndlessRoom()
    {
        try
        {
            LobbyConnection.HostLan(RoomMode.Endless);
            UiScreens.Go(UiScreens.Lobby);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException or ArgumentException)
        {
            GD.PushWarning($"cannot host: {e}");
            LobbyConnection.CloseCurrent();
            ShowMessage(UiText.F("multiplayer.host_failed", e.Message));
        }
    }

    private static string RandomSeed() => new Random().Next(1, 999_999_999).ToString(CultureInfo.InvariantCulture);

    private void StartNew()
    {
        if (!int.TryParse(seed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            ShowNote(UiText.T("endless.seed_invalid"));
            return;
        }
        GameApp.Instance.Match = new MatchRequest
        {
            Mode = MatchMode.Endless,
            Difficulty = options.Difficulty,
            Endless = options.Settings(value),
            Settings = options.World,
            Players = [new MatchPlayer(0, Settings.PlayerName, false, 0, 0, 0, true)],
            ShowHints = Settings.ShowHints,
        };
        MatchLauncher.Launch(Overlay);
    }

    private void Continue(SaveInfo save)
    {
        GameApp.Instance.Match = new MatchRequest { Mode = MatchMode.Endless, SaveToLoad = save.DataPath, ShowHints = Settings.ShowHints };
        MatchLauncher.Launch(Overlay);
    }
}
