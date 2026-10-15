using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Photon;
using Tribes.Net.Transport;

namespace Tribes.Client.Ui.Net;

/// <summary>
/// The 「網際網路」 tab of the multiplayer screen: the rooms of one Photon region (the same rows as
/// the LAN list, <see cref="RoomListing"/>), the region choice (nearest by default), joining by
/// room code, and creating versus or endless rooms. While a room is being created or joined the
/// tab waits here, so Photon's errors are shown in place; the lobby opens once the room is reached.
/// </summary>
public partial class InternetTab : VBoxContainer
{
    private const int MaxCodeLength = 16;
    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(30);

    private readonly Action<string> showMessage;
    private readonly List<Button> actions = [];
    private PhotonRoomBrowser? browser;
    private Label status = null!;
    private VBoxContainer roomList = null!;
    private LineEdit code = null!;
    private OptionButton region = null!;
    private Button cancel = null!;
    private LobbyConnection? waiting;
    private DateTime waitingSince;
    private bool devActionDone;

    /// <summary>Creates the tab; <paramref name="showMessage"/> shows a dialog on the screen.</summary>
    public InternetTab(Action<string> showMessage)
    {
        this.showMessage = showMessage;
        Name = "Internet";
        AddThemeConstantOverride("separation", 6);
        Build();
        VisibilityChanged += OnVisibilityChanged;
    }

    /// <summary>Godot needs a parameterless constructor for scripts; the tab is always built in code.</summary>
    public InternetTab()
        : this(_ => { })
    {
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        browser?.Poll();
        if (waiting is not null)
        {
            WaitForRoom();
        }
        else
        {
            RunDevAction();
        }
    }

    /// <summary>
    /// Development: "--net-ui-join=ADDRESS" joins that room, "--net-ui-host=versus|endless" creates
    /// a room, through this tab as a player would, once the room list is connected.
    /// </summary>
    private void RunDevAction()
    {
        if (devActionDone || browser?.Status != PhotonStatus.Connected)
        {
            return;
        }

        devActionDone = true;
        if (DevOptions.Get("net-ui-join") is { Length: > 0 } address)
        {
            Join(address, spectate: false);
        }
        else if (DevOptions.Get("net-ui-host") is { Length: > 0 } mode)
        {
            HostRoom(mode == "endless" ? RoomMode.Endless : RoomMode.Versus);
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        StopBrowsing();
        if (waiting is not null && LobbyConnection.Current == waiting)
        {
            // Left the screen while still connecting: the attempt is given up.
            LobbyConnection.CloseCurrent();
        }

        waiting = null;
        base._ExitTree();
    }

    /// <summary>
    /// The texts get whole lines of their own (status above the region choice, the hosting hint
    /// above its buttons), so the tab reads the same in a narrow portrait window as in landscape.
    /// </summary>
    private void Build()
    {
        status = UiFactory.Label("multiplayer.internet.connecting", PixelTheme.Variations.Muted, wrap: true);
        AddChild(status);
        HBoxContainer top = UiFactory.Line(6);
        string[] regionKeys = [.. RegionChoices().Select(c => c is null ? "multiplayer.internet.region_best" : "multiplayer.internet.region." + c)];
        region = UiFactory.Options(regionKeys, Math.Max(0, Array.IndexOf(RegionChoices(), InternetPlay.Region)), ChooseRegion);
        region.TooltipText = "multiplayer.internet.region_label";
        region.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(region);
        top.AddChild(Act(UiFactory.Button("multiplayer.refresh", () => browser?.Refresh())));
        AddChild(top);

        roomList = UiFactory.Column(4);
        PanelContainer listBox = UiFactory.Panel(PixelTheme.Variations.Inset, UiFactory.Scroll(roomList));
        listBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        listBox.CustomMinimumSize = new Vector2(0, 80);
        AddChild(listBox);

        HBoxContainer join = UiFactory.Line(6);
        code = new LineEdit
        {
            PlaceholderText = "multiplayer.internet.code_hint",
            MaxLength = MaxCodeLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Default,
        };
        code.TextSubmitted += _ => JoinTyped();
        join.AddChild(code);
        join.AddChild(Act(UiFactory.Button("multiplayer.join_address", JoinTyped)));
        AddChild(join);

        AddChild(UiFactory.Label("multiplayer.internet.host_hint", PixelTheme.Variations.Muted, wrap: true));
        HBoxContainer host = UiFactory.Line(6);
        host.AddChild(HostButton("multiplayer.internet.host_versus", RoomMode.Versus, PixelTheme.Variations.Primary));
        host.AddChild(HostButton("multiplayer.internet.host_endless", RoomMode.Endless, null));
        cancel = UiFactory.Button("multiplayer.internet.cancel", CancelWaiting);
        cancel.Visible = false;
        host.AddChild(cancel);
        AddChild(host);

        if (!InternetPlay.IsAvailable)
        {
            status.Text = InternetPlay.ErrorText(PhotonError.NotConfigured);
            foreach (Button button in actions)
            {
                button.Disabled = true;
            }

            region.Disabled = true;
            code.Editable = false;
        }

        ShowRooms();
    }

    private Button HostButton(string key, RoomMode mode, string? variation)
    {
        Button button = UiFactory.Button(key, () => HostRoom(mode), variation);
        button.CustomMinimumSize = new Vector2(96, PixelTheme.RowHeight);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return Act(button);
    }

    private Button Act(Button button)
    {
        actions.Add(button);
        return button;
    }

    private static string?[] RegionChoices() => [null, .. PhotonRegions.Known];

    private void OnVisibilityChanged()
    {
        if (IsVisibleInTree())
        {
            StartBrowsing();
        }
        else
        {
            // Browsing holds a connection to Photon (one of the app's concurrent users).
            StopBrowsing();
        }
    }

    private void StartBrowsing()
    {
        if (browser is not null || !InternetPlay.IsAvailable || InternetPlay.Settings() is not { } settings)
        {
            return;
        }

        browser = new PhotonRoomBrowser(settings);
        browser.RoomsChanged += (_, _) => ShowRooms();
        browser.StatusChanged += (_, _) => ShowRooms();
        browser.Start();
        ShowRooms();
    }

    private void StopBrowsing()
    {
        browser?.Dispose();
        browser = null;
    }

    private void ChooseRegion(int index)
    {
        string?[] choices = RegionChoices();
        InternetPlay.Region = choices[Math.Clamp(index, 0, choices.Length - 1)];
        StopBrowsing();
        if (IsVisibleInTree())
        {
            StartBrowsing();
        }
    }

    private void ShowRooms()
    {
        foreach (Node child in roomList.GetChildren())
        {
            child.QueueFree();
        }

        if (!InternetPlay.IsAvailable)
        {
            return;
        }

        (string game, string content) = LobbyConnection.Versions();
        IReadOnlyList<RoomListEntry> entries = RoomListing.Build(browser?.Rooms ?? Array.Empty<RoomInfo>(), LobbyConnection.PlayerKey(), game, content, BuildFingerprint.Current);
        if (waiting is null)
        {
            status.Text = BrowseText(entries.Count);
        }

        if (entries.Count == 0)
        {
            Label none = UiFactory.Label(browser?.Status == PhotonStatus.Connected ? "multiplayer.internet.no_rooms" : "", PixelTheme.Variations.Muted, wrap: true);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            roomList.AddChild(none);
            return;
        }

        foreach (RoomListEntry entry in entries)
        {
            roomList.AddChild(RoomRow(entry));
        }
    }

    private string BrowseText(int rooms)
    {
        if (browser is null)
        {
            return UiText.T("multiplayer.internet.connecting");
        }

        string where = InternetPlay.Region is null && browser.Region is { } best
            ? UiText.F("multiplayer.internet.region_auto_is", InternetPlay.RegionName(best))
            : InternetPlay.RegionName(browser.Region ?? InternetPlay.Region);
        return browser.Status switch
        {
            PhotonStatus.Connected => UiText.F("multiplayer.internet.found", where, rooms),
            PhotonStatus.Failed => InternetPlay.ErrorText(browser.LastError),
            _ => UiText.T("multiplayer.internet.connecting"),
        };
    }

    /// <summary>One room, as in the LAN list: name and badge, mode, players, ping, what the button means, the button.</summary>
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
            text.AddChild(UiFactory.Label(note, PixelTheme.Variations.Muted, wrap: true));
        }

        row.AddChild(text);
        Button act = UiFactory.Button(entry.ActionKey, () => Join(room.Address, entry.ConnectsAsSpectator),
            entry.Action == RoomAction.Return ? PixelTheme.Variations.Primary : null);
        act.Disabled = !entry.CanAct || waiting is not null;
        act.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        act.CustomMinimumSize = new Vector2(84, PixelTheme.RowHeight);
        row.AddChild(act);
        return UiFactory.Panel(entry.Action == RoomAction.Return ? PixelTheme.Variations.SlotSelected : PixelTheme.Variations.Slot, row);
    }

    private void JoinTyped()
    {
        if (!PhotonAddress.TryParseRoom(code.Text, out string? typedRegion, out string roomCode))
        {
            showMessage(UiText.T("multiplayer.internet.code_invalid"));
            return;
        }

        // A code without a region is in the region listed here (the nearest one when none was chosen).
        string? where = typedRegion ?? browser?.Region ?? InternetPlay.Region;
        Join(where is null ? roomCode : PhotonAddress.Room(where, roomCode), spectate: false);
    }

    private void Join(string address, bool spectate)
    {
        try
        {
            Wait(LobbyConnection.JoinInternet(address, spectate: spectate), UiText.F("multiplayer.internet.joining", address));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            GD.PushWarning($"cannot join {address}: {e.Message}");
            LobbyConnection.CloseCurrent();
            showMessage(UiText.T("lobby.end.error"));
        }
    }

    private void HostRoom(RoomMode mode)
    {
        try
        {
            Wait(LobbyConnection.HostInternet(mode), UiText.T("multiplayer.internet.hosting"));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            GD.PushWarning($"cannot host on the internet: {e.Message}");
            LobbyConnection.CloseCurrent();
            showMessage(UiText.F("multiplayer.host_failed", e.Message));
        }
    }

    /// <summary>Stays on this tab until the room is reached (then the lobby opens) or the attempt fails.</summary>
    private void Wait(LobbyConnection connection, string text)
    {
        waiting = connection;
        waitingSince = DateTime.UtcNow;
        status.Text = text;
        SetBusy(true);
        ShowRooms();
    }

    private void WaitForRoom()
    {
        LobbyConnection connection = waiting!;
        if (LobbyConnection.Current != connection)
        {
            StopWaiting();
            return;
        }

        connection.Update();
        PhotonTransport? transport = connection.Internet;
        if (connection.Host is { } host)
        {
            if (host.Address is not null)
            {
                Open(connection);
            }
            else if (transport?.Status == PhotonStatus.Failed || DateTime.UtcNow - waitingSince > HostTimeout)
            {
                Fail(transport?.Status == PhotonStatus.Failed ? transport.LastError : PhotonError.Timeout);
            }

            return;
        }

        ClientSession client = connection.Client!;
        if (client.Phase == ClientPhase.Ended)
        {
            Fail(transport?.LastError is { } error and not PhotonError.None ? error : PhotonError.HostNotAnswering);
        }
        else if (client.Phase != ClientPhase.Connecting || transport?.Connections.Count > 0)
        {
            // The host answered; the lobby screen takes over before the room's first update arrives.
            Open(connection);
        }
    }

    private void Open(LobbyConnection connection)
    {
        GD.Print($"LOBBY internet room reached: {connection.Address ?? connection.Internet?.RoomCode}");
        waiting = null;
        UiScreens.Go(UiScreens.Lobby);
    }

    private void Fail(PhotonError error)
    {
        GD.Print($"LOBBY internet room not reached: {error}");
        LobbyConnection.CloseCurrent();
        StopWaiting();
        showMessage(InternetPlay.ErrorText(error));
    }

    private void CancelWaiting()
    {
        if (waiting is not null && LobbyConnection.Current == waiting)
        {
            LobbyConnection.CloseCurrent();
        }

        StopWaiting();
    }

    private void StopWaiting()
    {
        waiting = null;
        SetBusy(false);
        ShowRooms();
    }

    private void SetBusy(bool busy)
    {
        foreach (Button button in actions)
        {
            button.Disabled = busy || !InternetPlay.IsAvailable;
        }

        region.Disabled = busy || !InternetPlay.IsAvailable;
        code.Editable = !busy && InternetPlay.IsAvailable;
        cancel.Visible = busy;
    }
}
