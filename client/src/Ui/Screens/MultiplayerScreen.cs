using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Lan;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Multiplayer. The LAN tab lists the rooms found on the local network (UDP discovery), hosts a
/// room, and joins a room by address. The Internet tab (<see cref="InternetTab"/>) does the same
/// over Photon.
/// </summary>
public partial class MultiplayerScreen : ScreenBase
{
    private const int MaxAddressLength = 64;

    private LanRoomBrowser? browser;
    private bool leased;
    private VBoxContainer roomList = null!;
    private Label browseStatus = null!;
    private LineEdit address = null!;
    private TabContainer tabs = null!;

    /// <inheritdoc/>
    protected override void Build()
    {
        LobbyConnection.CloseCurrent();
        VBoxContainer content = BuildSheet("multiplayer.title", out _);
        tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(tabs);
        tabs.AddChild(BuildLanTab());
        tabs.AddChild(new InternetTab(text => ShowMessage(text)));
        tabs.SetTabTitle(0, "multiplayer.tab_lan");
        tabs.SetTabTitle(1, "multiplayer.tab_internet");
        if (DevOptions.Get("ui-tab") == "internet")
        {
            tabs.CurrentTab = 1;
        }
        StartBrowsing();
    }

    private Control BuildLanTab()
    {
        VBoxContainer lan = UiFactory.Column(6);
        lan.Name = "Lan";
        HBoxContainer top = UiFactory.Line(6);
        browseStatus = UiFactory.Label("multiplayer.searching", PixelTheme.Variations.Muted);
        browseStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        browseStatus.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(browseStatus);
        top.AddChild(UiFactory.Button("multiplayer.refresh", () => browser?.Refresh()));
        lan.AddChild(top);

        roomList = UiFactory.Column(4);
        PanelContainer listBox = UiFactory.Panel(PixelTheme.Variations.Inset, UiFactory.Scroll(roomList));
        listBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        listBox.CustomMinimumSize = new Vector2(0, 80);
        lan.AddChild(listBox);

        HBoxContainer join = UiFactory.Line(6);
        address = new LineEdit
        {
            PlaceholderText = "multiplayer.address_hint",
            MaxLength = MaxAddressLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Url,
        };
        address.TextSubmitted += _ => JoinTyped();
        join.AddChild(address);
        join.AddChild(UiFactory.Button("multiplayer.join_address", JoinTyped));
        lan.AddChild(join);

        HBoxContainer host = UiFactory.Line(6);
        host.AddChild(UiFactory.Label("multiplayer.host_hint", PixelTheme.Variations.Muted, wrap: true));
        Button create = UiFactory.Button("multiplayer.host", () => HostRoom(RoomMode.Versus), PixelTheme.Variations.Primary);
        create.CustomMinimumSize = new Vector2(96, PixelTheme.RowHeight);
        host.AddChild(create);
        lan.AddChild(host);
        return lan;
    }

    private void StartBrowsing()
    {
        try
        {
            MulticastLease.Acquire();
            leased = true;
            browser = new LanRoomBrowser();
            browser.RoomsChanged += (_, _) => ShowRooms();
            browser.Start();
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            GD.PushWarning($"LAN discovery unavailable: {e.Message}");
            browseStatus.Text = UiText.F("multiplayer.search_failed", e.Message);
            browser = null;
        }
        ShowRooms();
    }

    /// <inheritdoc/>
    public override void _Process(double delta) => browser?.Poll();

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        browser?.Shutdown();
        browser?.Dispose();
        browser = null;
        if (leased)
        {
            leased = false;
            MulticastLease.Release();
        }
        base._ExitTree();
    }

    private void ShowRooms()
    {
        foreach (Node child in roomList.GetChildren())
        {
            child.QueueFree();
        }
        (string game, string content) = LobbyConnection.Versions();
        IReadOnlyList<RoomListEntry> entries = RoomListing.Build(browser?.Rooms ?? Array.Empty<RoomInfo>(), LobbyConnection.PlayerKey(), game, content, BuildFingerprint.Current);
        if (browser is not null)
        {
            browseStatus.Text = entries.Count == 0 ? UiText.T("multiplayer.searching") : UiText.F("multiplayer.found", entries.Count);
        }
        if (entries.Count == 0)
        {
            Label none = UiFactory.Label("multiplayer.no_rooms", PixelTheme.Variations.Muted, wrap: true);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            roomList.AddChild(none);
            return;
        }
        foreach (RoomListEntry entry in entries)
        {
            roomList.AddChild(RoomRow(entry));
        }
    }

    /// <summary>
    /// One room: name (with the 「對局中」 badge while its match runs), mode, players and ping, a line
    /// saying what the button means, and the button: join, come back to the match, or watch.
    /// </summary>
    private Control RoomRow(RoomListEntry entry)
    {
        RoomInfo room = entry.Room;
        HBoxContainer row = UiFactory.Line(6);
        VBoxContainer text = UiFactory.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        HBoxContainer title = UiFactory.Line(4);
        if (entry.BadgeKey is { } badge)
        {
            title.AddChild(UiFactory.Badge(badge));
        }
        title.AddChild(UiFactory.Cell(room.Room.Name));
        text.AddChild(title);
        string mode = UiText.T(room.Room.Mode == RoomMode.Endless ? "multiplayer.mode_endless" : "multiplayer.mode_versus");
        text.AddChild(UiFactory.Cell(UiText.F("multiplayer.room_details", mode, room.Room.Players, room.Room.MaxPlayers,
            ((int)room.Ping.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)), PixelTheme.Variations.Muted));
        if (entry.NoteKey is { } note)
        {
            Label meaning = UiFactory.Label(note, PixelTheme.Variations.Muted, wrap: true);
            text.AddChild(meaning);
        }
        row.AddChild(text);
        Button act = UiFactory.Button(entry.ActionKey, () => Join(room.Address, entry.ConnectsAsSpectator),
            entry.Action == RoomAction.Return ? PixelTheme.Variations.Primary : null);
        act.Disabled = !entry.CanAct;
        act.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        act.CustomMinimumSize = new Vector2(84, PixelTheme.RowHeight);
        row.AddChild(act);
        return UiFactory.Panel(entry.Action == RoomAction.Return ? PixelTheme.Variations.SlotSelected : PixelTheme.Variations.Slot, row);
    }

    private void JoinTyped()
    {
        string text = address.Text.Trim();
        if (text.Length == 0)
        {
            ShowNote(UiText.T("multiplayer.address_empty"));
            return;
        }
        Join(text);
    }

    private void Join(string target) => Join(target, spectate: false);

    private void Join(string target, bool spectate)
    {
        try
        {
            LobbyConnection.JoinLan(target, spectate: spectate);
            UiScreens.Go(UiScreens.Lobby);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            GD.PushWarning($"cannot join {target}: {e.Message}");
            LobbyConnection.CloseCurrent();
            ShowMessage(UiText.T("lobby.end.error"));
        }
    }

    private void HostRoom(RoomMode mode)
    {
        try
        {
            LobbyConnection.HostLan(mode);
            UiScreens.Go(UiScreens.Lobby);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException or ArgumentException)
        {
            GD.PushWarning($"cannot host: {e}");
            ShowMessage(UiText.F("multiplayer.host_failed", e.Message));
        }
    }
}
