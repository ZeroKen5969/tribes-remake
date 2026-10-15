using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Net;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// A multiplayer room: the 4 tribe slots (players, computer tribes, open slots), the local
/// player's colour (8 colours, one per tribe) and team, ready, chat, and for the host the map,
/// computer tribes and the start button. Absent players are shown as offline.
/// </summary>
public partial class LobbyScreen : ScreenBase
{
    private readonly List<PanelContainer> slotPanels = new();
    private LobbyConnection? lobby;
    private NetSessionRef? running;
    private BoxContainer split = null!;
    private ScrollContainer leftPane = null!;
    private Control leftContent = null!;
    private Label roomInfo = null!;
    private VBoxContainer slots = null!;
    private ColourPicker colours = null!;
    private OptionButton team = null!;
    private Control teamRow = null!;
    private Button ready = null!;
    private Button start = null!;
    private Label startHint = null!;
    private OptionButton map = null!;
    private Label mapRoom = null!;
    private EndlessOptions endlessOptions = null!;
    private WorldOptions matchOptions = null!;
    private OptionButton aiLevel = null!;
    private Control hostControls = null!;
    private Label chatLog = null!;
    private ScrollContainer chatScroll = null!;
    private LineEdit chatInput = null!;
    private Label connecting = null!;
    private IReadOnlyList<MissionEntry> maps = [];
    private bool devColourDone;
    private bool devRecolourDone;
    private int devStartStep;
    private double devStartAt;
    private bool devReadyDone;
    private double openedSeconds;

    /// <inheritdoc/>
    protected override string? BackScreen => UiScreens.Multiplayer;

    /// <inheritdoc/>
    protected override void Build()
    {
        lobby = LobbyConnection.Current;
        maps = MissionCatalog.CustomMaps;
        IsSettled = DevOptions.GetInt("lan-expect", 0) == 0;
        VBoxContainer content = BuildSheet("lobby.title", out _);
        roomInfo = UiFactory.Plain("", PixelTheme.Variations.Muted);
        roomInfo.HorizontalAlignment = HorizontalAlignment.Center;
        roomInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(roomInfo);

        split = new BoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 8);
        content.AddChild(split);

        VBoxContainer left = UiFactory.Column(6);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1.4f;
        slots = UiFactory.Column(3);
        for (int i = 0; i < RoomLimits.MaxPlayers; i++)
        {
            var panel = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Slot, CustomMinimumSize = new Vector2(0, 30) };
            slots.AddChild(panel);
            slotPanels.Add(panel);
        }
        connecting = UiFactory.Label("lobby.connecting", PixelTheme.Variations.Heading, wrap: true);
        connecting.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(connecting);
        left.AddChild(slots);

        HBoxContainer mine = UiFactory.Line(10);
        VBoxContainer colourBox = UiFactory.Column(6);
        colourBox.AddChild(UiFactory.Label("lobby.your_colour", PixelTheme.Variations.Heading));
        colours = new ColourPicker(PickColour);
        colourBox.AddChild(colours);
        mine.AddChild(colourBox);
        VBoxContainer choices = UiFactory.Column(4);
        choices.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        team = UiFactory.Options(["lobby.team.1", "lobby.team.2", "lobby.team.3", "lobby.team.4"], 0, index => Report(lobby?.ChooseTeam(index)));
        teamRow = UiFactory.Row("lobby.team", team, 48);
        choices.AddChild(teamRow);
        ready = UiFactory.Button("lobby.ready");
        ready.ToggleMode = true;
        ready.Toggled += on => Report(lobby?.SetReady(on));
        choices.AddChild(ready);
        mine.AddChild(choices);
        left.AddChild(mine);

        leftPane = UiFactory.Scroll(left);
        leftContent = left;
        split.AddChild(leftPane);

        VBoxContainer right = UiFactory.Column(4);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        VBoxContainer host = UiFactory.Column(4);
        string[] mapNames = new string[maps.Count];
        for (int i = 0; i < maps.Count; i++)
        {
            mapNames[i] = maps[i].Name;
        }
        map = UiFactory.Options(mapNames, 0, _ => Refresh());
        map.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        host.AddChild(UiFactory.Row("lobby.map", map, 64));
        mapRoom = UiFactory.Label("", PixelTheme.Variations.Muted, wrap: true);
        mapRoom.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        host.AddChild(mapRoom);
        aiLevel = UiFactory.Options(["difficulty.easy", "difficulty.hard"], 0, _ => { });
        host.AddChild(UiFactory.Row("lobby.computer_level", aiLevel, 64));
        // A versus room's settings: opening grace, start stock, wild animals.
        matchOptions = new WorldOptions(Tribes.Session.MatchPlan.DefaultMatchGraceMinutes);
        host.AddChild(matchOptions);
        // An endless room's world: start area, extra computer tribes (no maximum), new tribes, its settings.
        endlessOptions = new EndlessOptions(computerTribes: 0, withDifficulty: false);
        host.AddChild(endlessOptions);
        hostControls = host;
        // The host's settings scroll with the slots, so the chat and the start button always fit.
        left.AddChild(host);
        right.AddChild(UiFactory.Label("lobby.chat", PixelTheme.Variations.Heading));
        chatLog = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, AutoTranslateMode = AutoTranslateModeEnum.Disabled, VerticalAlignment = VerticalAlignment.Bottom };
        chatScroll = UiFactory.Scroll(chatLog);
        PanelContainer chatBox = UiFactory.Panel(PixelTheme.Variations.Inset, chatScroll);
        chatBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        chatBox.CustomMinimumSize = new Vector2(0, 60);
        right.AddChild(chatBox);
        HBoxContainer say = UiFactory.Line(4);
        chatInput = new LineEdit
        {
            MaxLength = RoomLimits.MaxChatLength,
            PlaceholderText = "lobby.chat_hint",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
        };
        chatInput.TextSubmitted += _ => SendChat();
        say.AddChild(chatInput);
        say.AddChild(UiFactory.Button("lobby.send", SendChat));
        right.AddChild(say);
        startHint = UiFactory.Label("", PixelTheme.Variations.Muted, wrap: true);
        startHint.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        right.AddChild(startHint);
        start = UiFactory.Button("lobby.start", StartMatch, PixelTheme.Variations.Primary);
        right.AddChild(start);
        split.AddChild(right);

        if (lobby is null)
        {
            CallDeferred(MethodName.NoRoom);
            return;
        }
        lobby.RoomChanged += OnRoomChanged;
        lobby.LogAdded += OnLogAdded;
        lobby.Notice += ShowNote;
        lobby.Ended += OnEnded;
        lobby.Started += OnStarted;
        foreach (string line in lobby.Log)
        {
            OnLogAdded(line);
        }
        Refresh();
    }

    private void NoRoom() => ShowMessage(UiText.T("lobby.no_room"), () => UiScreens.Go(UiScreens.Multiplayer));

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        Unsubscribe();
        base._ExitTree();
    }

    private void Unsubscribe()
    {
        if (lobby is not null)
        {
            lobby.RoomChanged -= OnRoomChanged;
            lobby.LogAdded -= OnLogAdded;
            lobby.Notice -= ShowNote;
            lobby.Ended -= OnEnded;
            lobby.Started -= OnStarted;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        lobby?.Update();
        if (running is not null)
        {
            running.Host?.Update();
            running.Client?.Update();
        }
        openedSeconds += delta;
        int recolour = DevOptions.GetInt("lan-recolour", -1);
        if (!devRecolourDone && recolour >= 0 && openedSeconds > 2 && lobby?.LocalPlayer is { HasSlot: true })
        {
            devRecolourDone = true;
            GD.Print($"LOBBY dev choosing colour {recolour}");
            Report(lobby.ChooseColour(recolour));
        }
        if (!devReadyDone && DevOptions.Has("lan-ready") && lobby is { IsHost: false, LocalPlayer.HasSlot: true } && openedSeconds > 1)
        {
            devReadyDone = true;
            GD.Print("LOBBY dev ready");
            Report(lobby.SetReady(true));
        }
        if (DevOptions.Has("lan-autostart") && lobby is { IsHost: true })
        {
            DevAutostart();
        }
        int quitAfter = DevOptions.GetInt("lan-quit-after", 0);
        if (quitAfter > 0 && openedSeconds > quitAfter)
        {
            GD.Print("LOBBY dev quit");
            GetTree().Quit();
        }
    }

    /// <inheritdoc/>
    protected override void Layout(bool isPortrait)
    {
        split.Vertical = isPortrait;
        // Portrait: the slots take the height they need and the chat gets the rest.
        leftPane.SizeFlagsVertical = isPortrait ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill;
        FitLeftPane();
    }

    private void FitLeftPane()
    {
        leftPane.CustomMinimumSize = new Vector2(0, IsPortrait ? leftContent.GetCombinedMinimumSize().Y : 0);
    }

    /// <inheritdoc/>
    protected override void RefreshTexts() => Refresh();

    private void OnRoomChanged(RoomState room)
    {
        int colour = DevOptions.GetInt("colour", -1);
        if (!devColourDone && colour >= 0 && lobby?.LocalPlayer is { } me && me.HasSlot)
        {
            devColourDone = true;
            if (me.Colour != colour)
            {
                Report(lobby.ChooseColour(colour));
            }
        }
        int expected = DevOptions.GetInt("lan-expect", 0);
        if (expected > 0 && room.Players.Count >= expected)
        {
            IsSettled = true;
        }
        Refresh();
    }

    private void OnLogAdded(string line)
    {
        chatLog.Text = string.Join("\n", lobby?.Log ?? Array.Empty<string>());
        chatScroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, chatLog);
        chatScroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 1_000_000);
    }

    private void OnEnded(string reason)
    {
        GD.Print($"LOBBY ended: {reason}");
        Unsubscribe();
        LobbyConnection.CloseCurrent();
        lobby = null;
        ShowMessage(reason, () => UiScreens.Go(UiScreens.Multiplayer));
    }

    private void OnStarted(MatchStart matchStart)
    {
        if (lobby is null)
        {
            return;
        }
        RoomState? room = lobby.Room;
        PlayerInfo? me = lobby.LocalPlayer;
        MatchSetup? setup = MatchSetup.FromBytes(matchStart.GameSetup.Span);
        var players = new List<MatchPlayer>();
        foreach (PlayerInfo player in matchStart.Players)
        {
            if (player.HasSlot)
            {
                players.Add(new MatchPlayer(player.Slot, DisplayName(player), player.Kind == PlayerKind.Computer, player.Colour,
                    player.Team, player.AiLevel, me is not null && player.Id == me.Id));
            }
        }
        running = lobby.HandOver(matchStart);
        // The session lives on in the match screen; this screen must not hear from it any more.
        Unsubscribe();
        lobby = null;
        GameApp.Instance.Match = new MatchRequest
        {
            Mode = matchStart.Mode == RoomMode.Endless ? MatchMode.Endless : MatchMode.Versus,
            Mission = setup?.Mission ?? -1,
            Difficulty = setup?.Difficulty ?? 0,
            Players = players,
            Session = running,
            Endless = setup?.Endless,
            ShowHints = Settings.ShowHints,
        };
        GD.Print($"LOBBY match started room={room?.Settings.Name} players={players.Count}");
        MatchLauncher.Launch(Overlay);
    }

    private void Refresh()
    {
        RoomState? room = lobby?.Room ?? running?.Host?.Room ?? running?.Client?.Room;
        PlayerInfo? me = lobby?.LocalPlayer ?? running?.Host?.LocalPlayer ?? running?.Client?.LocalPlayer;
        bool isHost = lobby?.IsHost ?? running?.Host is not null;
        bool inLobby = room is not null && room.Phase == RoomPhase.Lobby && lobby is not null;
        connecting.Visible = room is null;
        slots.Visible = room is not null;
        if (room is null)
        {
            roomInfo.Text = "";
            colours.SetState(-1, new HashSet<int>(), enabled: false);
            ready.Disabled = true;
            start.Visible = false;
            hostControls.Visible = false;
            startHint.Text = "";
            return;
        }
        string mode = UiText.T(room.Settings.Mode == RoomMode.Endless ? "multiplayer.mode_endless" : "multiplayer.mode_versus");
        roomInfo.Text = isHost && lobby?.Address is { } address
            ? UiText.F("lobby.info_host", room.Settings.Name, mode, address)
            : UiText.F("lobby.info", room.Settings.Name, mode);
        var taken = new HashSet<int>();
        foreach (PlayerInfo player in room.Players)
        {
            if (player.HasSlot && player.Colour >= 0 && (me is null || player.Id != me.Id))
            {
                taken.Add(player.Colour);
            }
        }
        int mission = RoomMission(room);
        (int humans, int tribes) = CountTribes(room);
        // While the map capacities are worked out (a moment after start) the room just waits for them.
        int? known = MissionCatalog.KnownTribeLimit(mission);
        if (known is null)
        {
            MissionCatalog.WhenCapacitiesKnown(() =>
            {
                if (IsInsideTree())
                {
                    Refresh();
                }
            });
        }
        int limit = known ?? int.MaxValue;
        bool roomOnMap = MatchRoom.ComputersAllowed(limit, room.Settings.MaxPlayers - tribes, tribes) > 0;
        for (int slot = 0; slot < slotPanels.Count; slot++)
        {
            FillSlot(slotPanels[slot], slot, room, me, isHost && inLobby, taken, roomOnMap);
        }
        mapRoom.Text = mission < 0 ? "" : known is null ? UiText.T("lobby.map_room_counting") : UiText.F("lobby.map_room", limit);
        CallDeferred(MethodName.FitLeftPane);
        bool versus = room.Settings.Mode == RoomMode.Versus;
        colours.SetState(me?.Colour ?? -1, taken, enabled: inLobby && me is { HasSlot: true });
        teamRow.Visible = versus;
        team.Disabled = !inLobby || me is not { HasSlot: true };
        if (me is not null && me.Team >= 0 && me.Team < team.ItemCount)
        {
            team.Selected = me.Team;
        }
        ready.Visible = !isHost;
        ready.Disabled = !inLobby;
        ready.SetPressedNoSignal(me?.IsReady ?? false);
        ready.Text = me?.IsReady == true ? "lobby.ready_done" : "lobby.ready";
        hostControls.Visible = isHost && inLobby;
        map.GetParent<Control>().Visible = versus;
        mapRoom.Visible = versus;
        endlessOptions.Visible = !versus;
        matchOptions.Visible = versus;
        start.Visible = isHost;
        start.Text = versus ? "lobby.start" : "lobby.start_endless";
        start.Disabled = true;
        if (isHost && inLobby && lobby is not null)
        {
            RequestResult can = lobby.CanStart();
            MatchProblem problem = known is null ? MatchProblem.None : MissionCatalog.Check(mission, humans, tribes);
            start.Disabled = can != RequestResult.Accepted || problem != MatchProblem.None || known is null;
            startHint.Text = known is null ? UiText.T("lobby.map_room_counting")
                : problem != MatchProblem.None ? MissionCatalog.ProblemText(problem, mission, tribes)
                : can == RequestResult.Accepted ? UiText.T("lobby.can_start") : LobbyConnection.DescribeResult(can);
        }
        else
        {
            startHint.Text = room.Phase == RoomPhase.InGame ? UiText.T("lobby.in_game") : UiText.T("lobby.wait_host");
        }
    }

    /// <summary>The mission the room starts on: the chosen map, or -1 for an endless world.</summary>
    private int RoomMission(RoomState room) =>
        room.Settings.Mode == RoomMode.Endless || maps.Count == 0 ? -1 : maps[Math.Clamp(map.Selected, 0, maps.Count - 1)].Mission;

    /// <summary>The people and all tribes (people and computer players) holding a slot.</summary>
    private static (int Humans, int Tribes) CountTribes(RoomState room)
    {
        int humans = 0, tribes = 0;
        foreach (PlayerInfo player in room.Players)
        {
            if (player.HasSlot)
            {
                tribes++;
                humans += player.Kind == PlayerKind.Human ? 1 : 0;
            }
        }
        return (humans, tribes);
    }

    private void FillSlot(PanelContainer panel, int slot, RoomState room, PlayerInfo? me, bool hostControlsOn, HashSet<int> taken, bool roomOnMap)
    {
        foreach (Node child in panel.GetChildren())
        {
            child.QueueFree();
        }
        PlayerInfo? player = room.PlayerInSlot(slot);
        panel.ThemeTypeVariation = player is not null && me is not null && player.Id == me.Id ? PixelTheme.Variations.SlotSelected : PixelTheme.Variations.Slot;
        HBoxContainer row = UiFactory.Line(6);
        panel.AddChild(row);
        var icon = new TextureRect { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(16, 16) };
        row.AddChild(icon);
        if (player is null)
        {
            icon.Texture = null;
            Label open = UiFactory.Label("lobby.slot_open", PixelTheme.Variations.Muted);
            open.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            open.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(open);
            if (hostControlsOn && roomOnMap)
            {
                Button add = UiFactory.Button("lobby.add_computer_short", AddComputer, PixelTheme.Variations.Tool);
                add.CustomMinimumSize = new Vector2(0, 20);
                row.AddChild(add);
            }
            else if (hostControlsOn)
            {
                // The slot is free but the map has no room for another camp.
                Label full = UiFactory.Label("lobby.map_full_slot", PixelTheme.Variations.Muted);
                full.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(full);
            }
            return;
        }
        icon.Texture = player.Kind == PlayerKind.Computer ? Icons.Computer : Icons.Person;
        VBoxContainer text = UiFactory.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(UiFactory.Cell(DisplayName(player)));
        text.AddChild(UiFactory.Cell(Status(player, room), PixelTheme.Variations.Muted));
        row.AddChild(text);

        TribeColour colour = TribeColours.Get(player.Colour);
        var swatch = new ColorRect { Color = colour.Swatch, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = colour.Name };
        var swatchFrame = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Inset, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        swatchFrame.AddChild(swatch);
        row.AddChild(swatchFrame);
        if (room.Settings.Mode == RoomMode.Versus)
        {
            Label teamLabel = UiFactory.Plain(UiText.F("lobby.team_short", player.Team + 1));
            teamLabel.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(teamLabel);
        }
        if (player.Kind == PlayerKind.Human && !player.IsHost && player.IsReady)
        {
            row.AddChild(new TextureRect { Texture = PixelTheme.TickIcon, StretchMode = TextureRect.StretchModeEnum.KeepCentered, TooltipText = "lobby.status_ready" });
        }
        if (!hostControlsOn || player.IsHost)
        {
            return;
        }
        if (player.Kind == PlayerKind.Computer)
        {
            Button recolour = UiFactory.Button("lobby.next_colour", () => NextComputerColour(player, taken), PixelTheme.Variations.Tool);
            recolour.CustomMinimumSize = new Vector2(0, 20);
            row.AddChild(recolour);
            if (room.Settings.Mode == RoomMode.Versus)
            {
                Button nextTeam = UiFactory.Button("lobby.next_team", () => Report(lobby?.ConfigureComputer(player.Slot, player.Colour, (player.Team + 1) % RoomLimits.MaxTeams, player.AiLevel)), PixelTheme.Variations.Tool);
                nextTeam.CustomMinimumSize = new Vector2(0, 20);
                row.AddChild(nextTeam);
            }
        }
        var remove = new Button { Icon = PixelTheme.CrossIcon, ThemeTypeVariation = PixelTheme.Variations.Tool, TooltipText = player.Kind == PlayerKind.Computer ? "lobby.remove_computer" : "lobby.kick", CustomMinimumSize = new Vector2(22, 20) };
        remove.Pressed += () =>
        {
            if (player.Kind == PlayerKind.Computer)
            {
                Report(lobby?.RemoveComputer(player.Slot));
            }
            else
            {
                ModalDialog.Show(Overlay, UiText.F("lobby.kick_confirm", player.Name), (UiKeys.Yes, () => Report(lobby?.Kick(player.Id))), (UiKeys.No, null));
            }
        };
        row.AddChild(remove);
    }

    /// <summary>A person's own name; computer tribes are named in the player's language.</summary>
    private static string DisplayName(PlayerInfo player) =>
        player.Kind == PlayerKind.Computer ? UiText.F("lobby.computer_name", player.Slot + 1) : player.Name;

    private static string Status(PlayerInfo player, RoomState room)
    {
        if (player.Kind == PlayerKind.Computer)
        {
            return UiText.F("lobby.status_computer", UiText.T(player.AiLevel >= 1 ? "difficulty.hard" : "difficulty.easy"));
        }
        if (player.Presence == PlayerPresence.Absent || !player.IsConnected)
        {
            return UiText.T("lobby.status_offline");
        }
        if (player.IsHost)
        {
            return UiText.T("lobby.status_host");
        }
        if (room.Phase == RoomPhase.InGame)
        {
            return UiText.T("lobby.status_playing");
        }
        return UiText.T(player.IsReady ? "lobby.status_ready" : "lobby.status_not_ready");
    }

    private void NextComputerColour(PlayerInfo player, HashSet<int> takenByOthers)
    {
        var used = new HashSet<int>(takenByOthers);
        if (lobby?.LocalPlayer is { Colour: >= 0 } me)
        {
            used.Add(me.Colour);
        }
        for (int step = 1; step <= RoomLimits.ColourCount; step++)
        {
            int candidate = (player.Colour + step) % RoomLimits.ColourCount;
            if (!used.Contains(candidate) || candidate == player.Colour)
            {
                Report(lobby?.ConfigureComputer(player.Slot, candidate, player.Team, player.AiLevel));
                return;
            }
        }
    }

    /// <summary>
    /// "--lan-autostart": the host adds "--lan-computers=N" computer tribes (one by default), then
    /// starts once "--lan-expect=N" people are in the room (checks the start path).
    /// </summary>
    private void DevAutostart()
    {
        int expected = DevOptions.GetInt("lan-expect", 0);
        if (devStartStep == 0 && openedSeconds > 2 && (lobby!.Room?.Players.Count ?? 0) >= expected)
        {
            devStartStep = 1;
            devStartAt = openedSeconds + 1;
            for (int i = DevOptions.GetInt("lan-computers", 1); i > 0; i--)
            {
                GD.Print($"LOBBY dev add computer slot={lobby.AddComputer(1)}");
            }
        }
        // Wait (at most 30 seconds) for everyone to be ready.
        else if (devStartStep == 1 && openedSeconds > devStartAt
            && (lobby!.CanStart() == RequestResult.Accepted || openedSeconds > devStartAt + 30))
        {
            devStartStep = 2;
            GD.Print($"LOBBY dev start can={lobby!.CanStart()}");
            StartMatch();
        }
    }

    private void PickColour(int colour) => Report(lobby?.ChooseColour(colour));

    private void AddComputer()
    {
        if (lobby?.Room is not { } room)
        {
            return;
        }
        (_, int tribes) = CountTribes(room);
        if (MatchRoom.ComputersAllowed(MissionCatalog.TribeLimit(RoomMission(room)), room.Settings.MaxPlayers - tribes, tribes) <= 0)
        {
            ShowNote(UiText.T("lobby.map_full_slot"));
            return;
        }
        if (lobby.AddComputer(aiLevel.Selected) < 0)
        {
            ShowNote(UiText.T("lobby.room_full"));
        }
    }

    private void SendChat()
    {
        string text = chatInput.Text.Trim();
        if (text.Length == 0 || lobby is null)
        {
            return;
        }
        Report(lobby.SendChat(text));
        chatInput.Text = "";
    }

    private void StartMatch()
    {
        if (lobby is null)
        {
            return;
        }
        int mission = maps.Count > 0 ? maps[Math.Clamp(map.Selected, 0, maps.Count - 1)].Mission : -1;
        int seed = (int)(Time.GetUnixTimeFromSystem() % int.MaxValue);
        bool endless = lobby.Room?.Settings.Mode == RoomMode.Endless;
        if (lobby.Room is { } room)
        {
            // Never start a match the map has no room for (the rules would refuse it).
            (int humans, int tribes) = CountTribes(room);
            MatchProblem problem = MissionCatalog.Check(RoomMission(room), humans, tribes);
            if (problem != MatchProblem.None)
            {
                ShowNote(MissionCatalog.ProblemText(problem, RoomMission(room), tribes));
                return;
            }
        }
        var setup = new MatchSetup(endless ? -1 : mission, aiLevel.Selected, seed,
            endless ? endlessOptions.Settings(seed) with { DevAllianceHalls = DevOptions.Has("session-dev-halls") && OS.IsDebugBuild() } : null,
            endless ? endlessOptions.World : matchOptions.Settings);
        Report(lobby.Start(setup));
    }

    private void Report(RequestResult? result)
    {
        if (result is { } r && r != RequestResult.Accepted)
        {
            ShowNote(LobbyConnection.DescribeResult(r));
        }
        Refresh();
    }

    /// <inheritdoc/>
    protected override void GoBack()
    {
        if (running is not null || (lobby is not null && lobby.Room is not null))
        {
            ModalDialog.Show(Overlay, "lobby.leave_confirm", (UiKeys.Yes, Leave), (UiKeys.No, null));
            return;
        }
        Leave();
    }

    private void Leave()
    {
        // Leaving makes the session report its own end; this screen is not listening any more.
        Unsubscribe();
        LobbyConnection.CloseCurrent();
        if (running is not null)
        {
            running.Client?.Leave();
            running.Client?.Dispose();
            if (running.Host is not null)
            {
                running.Host.Close();
                running.Host.Dispose();
                MulticastLease.Release();
            }
            running = null;
            GameApp.Instance.Match = null;
        }
        lobby = null;
        UiScreens.Go(UiScreens.Multiplayer);
    }
}
