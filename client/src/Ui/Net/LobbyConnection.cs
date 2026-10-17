using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Platform;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Transport.Lan;

namespace Tribes.Client.Ui.Net;

/// <summary>
/// The room this device is in, hosted here (<see cref="HostSession"/>) or joined
/// (<see cref="ClientSession"/>), kept while moving from the multiplayer screen to the lobby and
/// handed to the match screen when the match starts. <see cref="Update"/> must run every frame
/// (the lobby screen does it). All events are raised from <see cref="Update"/> or the calls below.
/// </summary>
public sealed partial class LobbyConnection : IDisposable
{
    private readonly List<string> log = new();
    private RoomState? lastRoom;
    private bool disposed;

    private LobbyConnection(HostSession? host, ClientSession? client)
    {
        Host = host;
        Client = client;
        if (host is not null)
        {
            host.RoomChanged += (_, e) => OnRoomChanged(e.Room);
            host.ChatReceived += (_, e) => OnChat(e.SenderName, e.Text);
            host.MatchStarted += (_, e) => Started?.Invoke(e.Start);
        }
        if (client is not null)
        {
            client.RoomChanged += (_, e) => OnRoomChanged(e.Room);
            client.ChatReceived += (_, e) => OnChat(e.SenderName, e.Text);
            client.MatchStarted += (_, e) => Started?.Invoke(e.Start);
            client.RequestDenied += (_, e) => Notice?.Invoke(DescribeDenied(e.Result));
            client.Ended += (_, e) => Ended?.Invoke(DescribeEnd(e.Reason, e.Rejection));
            client.PhaseChanged += (_, e) => PhaseChanged?.Invoke(e.Phase);
        }
    }

    /// <summary>The room this device is in now, or null.</summary>
    public static LobbyConnection? Current { get; private set; }

    /// <summary>Set when this device hosts the room.</summary>
    public HostSession? Host { get; }

    /// <summary>Set when this device joined someone's room.</summary>
    public ClientSession? Client { get; }

    /// <summary>Whether this device hosts the room.</summary>
    public bool IsHost => Host is not null;

    /// <summary>The room as last seen; null until a client has been welcomed.</summary>
    public RoomState? Room => Host?.Room ?? Client?.Room;

    /// <summary>This device's player in the room.</summary>
    public PlayerInfo? LocalPlayer => Host?.LocalPlayer ?? Client?.LocalPlayer;

    /// <summary>The address others can type to join (hosts only).</summary>
    public string? Address => Host?.Address;

    /// <summary>The chat and room events so far, newest last.</summary>
    public IReadOnlyList<string> Log => log;

    /// <summary>The room's list, colours, teams or readiness changed.</summary>
    public event Action<RoomState>? RoomChanged;

    /// <summary>A chat line or room event was added to <see cref="Log"/>.</summary>
    public event Action<string>? LogAdded;

    /// <summary>Something the player should be told (a request was denied).</summary>
    public event Action<string>? Notice;

    /// <summary>The match started; the session now runs it.</summary>
    public event Action<MatchStart>? Started;

    /// <summary>The connection ended (clients); the text says why.</summary>
    public event Action<string>? Ended;

    /// <summary>The client's connection phase changed.</summary>
    public event Action<ClientPhase>? PhaseChanged;

    /// <summary>The game and content versions players must share to be in one room.</summary>
    public static (string Game, string Content) Versions()
    {
        // The content running now (a pack applied since start only counts after the restart).
        int revision = GameApp.Instance.ContentRevision;
        return (BuildInfo.Current.Version, "content-r" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The player key of this device (from the settings, or a fresh one for "--player-id=new").</summary>
    public static Guid PlayerKey()
    {
        string? dev = Dev.DevOptions.Get("player-id");
        if (dev == "new")
        {
            return devKey ??= Guid.NewGuid();
        }
        if (Guid.TryParse(dev, out Guid given))
        {
            return given;
        }
        return Guid.TryParse(GameApp.Instance.Settings.PlayerId, out Guid key) ? key : Guid.NewGuid();
    }

    private static Guid? devKey;

    /// <summary>The player name of this run.</summary>
    public static string PlayerName() =>
        Dev.DevOptions.Get("player-name") is { Length: > 0 } name ? name : GameApp.Instance.Settings.PlayerName;

    /// <summary>Hosts a room on the local network and makes it <see cref="Current"/>.</summary>
    public static LobbyConnection HostLan(RoomMode mode, int preferredColour = -1)
    {
        CloseCurrent();
        (string game, string content) = Versions();
        string name = PlayerName();
        var transport = new LanTransport();
        var host = new HostSession(transport, new HostOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = PlayerKey(),
            PlayerName = name,
            PreferredColour = preferredColour,
            Room = new RoomSettings
            {
                Name = UiText.F("lobby.room_name", name),
                Mode = mode,
                AllowJoinInProgress = mode == RoomMode.Endless,
            },
            // Debug builds can lower the largest world sent, to see what players are told when it is too large.
            Lockstep = new LockstepOptions
            {
                MaxSnapshotBytes = OS.IsDebugBuild() ? Tribes.Client.Ui.Dev.DevOptions.GetInt("lan-snapshot-limit", NetProtocol.MaxSnapshotBytes) : NetProtocol.MaxSnapshotBytes,
            },
        }, MatchSimulations.Create);
        MulticastLease.Acquire();
        Current = new LobbyConnection(host, null) { lastRoom = host.Room };
        if (transport.DiscoveryError is { } problem)
        {
            Current.Add(UiText.F("lobby.discovery_failed", problem));
        }
        GD.Print($"LOBBY hosting {host.Address} mode={mode}");
        return Current;
    }

    /// <summary>
    /// Joins the room at <paramref name="address"/> ("ip:port" or "ip") and makes it
    /// <see cref="Current"/>; <paramref name="spectate"/> joins as a new spectator. A player who
    /// already holds a slot there (coming back to a match) gets that slot back either way.
    /// </summary>
    public static LobbyConnection JoinLan(string address, int preferredColour = -1, bool spectate = false)
    {
        CloseCurrent();
        (string game, string content) = Versions();
        var client = new ClientSession(new LanTransport(), new ClientOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = PlayerKey(),
            PlayerName = PlayerName(),
            PreferredColour = preferredColour,
            Spectate = spectate,
        }, MatchSimulations.Create);
        Current = new LobbyConnection(null, client);
        client.Connect(address);
        GD.Print($"LOBBY joining {address}");
        return Current;
    }

    /// <summary>Leaves and closes the current room, if any.</summary>
    public static void CloseCurrent()
    {
        Current?.Dispose();
        Current = null;
    }

    /// <summary>Hands the session over to the match screen: it is no longer <see cref="Current"/> and is not closed.</summary>
    public NetSessionRef HandOver(MatchStart? start)
    {
        if (Current == this)
        {
            Current = null;
        }
        disposed = true;
        return new NetSessionRef(Host, Client, start);
    }

    /// <summary>Runs the session; call every frame.</summary>
    public void Update()
    {
        if (disposed)
        {
            return;
        }
        Host?.Update();
        Client?.Update();
    }

    /// <summary>Picks colour <paramref name="colour"/> for this device's tribe.</summary>
    public RequestResult ChooseColour(int colour) => Host?.ChooseColour(colour) ?? Client!.ChooseColour(colour);

    /// <summary>Moves this device's tribe to <paramref name="team"/>.</summary>
    public RequestResult ChooseTeam(int team) => Host?.ChooseTeam(team) ?? Client!.ChooseTeam(team);

    /// <summary>Says whether this device's player is ready (clients).</summary>
    public RequestResult SetReady(bool ready) => Client?.SetReady(ready) ?? RequestResult.Accepted;

    /// <summary>Sends a chat line.</summary>
    public RequestResult SendChat(string text) => Host?.SendChat(text) ?? Client!.SendChat(text);

    /// <summary>Whether the host can start now, and if not, why.</summary>
    public RequestResult CanStart() => Host?.CanStart() ?? RequestResult.NotAllowedNow;

    /// <summary>Starts the match with <paramref name="setup"/> (hosts).</summary>
    public RequestResult Start(MatchSetup setup) => Host?.Start(setup.ToBytes()) ?? RequestResult.NotAllowedNow;

    /// <summary>Adds a computer tribe (hosts); returns its slot or -1.</summary>
    public int AddComputer(int aiLevel) => Host?.AddComputerPlayer(-1, aiLevel) ?? -1;

    /// <summary>Changes a computer tribe (hosts).</summary>
    public RequestResult ConfigureComputer(int slot, int colour, int team, int aiLevel) =>
        Host?.ConfigureComputerPlayer(slot, colour, team, aiLevel) ?? RequestResult.NotAllowedNow;

    /// <summary>Removes a computer tribe (hosts).</summary>
    public RequestResult RemoveComputer(int slot) => Host?.RemoveComputerPlayer(slot) ?? RequestResult.NotAllowedNow;

    /// <summary>Removes a person from the room for good (hosts).</summary>
    public RequestResult Kick(Guid player) => Host?.Kick(player) ?? RequestResult.NotAllowedNow;

    /// <summary>Adds a line to <see cref="Log"/>.</summary>
    public void Add(string line)
    {
        log.Add(line);
        if (log.Count > 100)
        {
            log.RemoveAt(0);
        }
        LogAdded?.Invoke(line);
    }

    private void OnChat(string sender, string text) => Add(UiText.F("lobby.chat_line", sender, text));

    private void OnRoomChanged(RoomState room)
    {
        var parts = new List<string>();
        foreach (PlayerInfo player in room.Players)
        {
            parts.Add($"slot{player.Slot}:{player.Name}:{player.Kind}:colour{player.Colour}:team{player.Team}:ready={player.IsReady}");
        }
        GD.Print($"LOBBY room rev={room.Revision} phase={room.Phase} players={room.Players.Count} [{string.Join(", ", parts)}]");
        if (lastRoom is not null)
        {
            foreach (PlayerInfo player in room.Players)
            {
                if (player.Kind == PlayerKind.Human && lastRoom.FindPlayer(player.Id) is null)
                {
                    Add(UiText.F("lobby.joined", player.Name));
                }
            }
            foreach (PlayerInfo player in lastRoom.Players)
            {
                if (player.Kind == PlayerKind.Human && room.FindPlayer(player.Id) is null)
                {
                    Add(UiText.F("lobby.left", player.Name));
                }
            }
        }
        lastRoom = room;
        RoomChanged?.Invoke(room);
    }

    /// <summary>The text for a refused request.</summary>
    public static string DescribeResult(RequestResult result) => result switch
    {
        RequestResult.Accepted => "",
        RequestResult.ColourTaken => UiText.T("lobby.denied.colour_taken"),
        RequestResult.PlayersNotReady => UiText.T("lobby.denied.not_ready"),
        RequestResult.NotEnoughPlayers => UiText.T("lobby.denied.not_enough"),
        RequestResult.NeedTwoTeams => UiText.T("lobby.denied.two_teams"),
        RequestResult.RateLimited => UiText.T("lobby.denied.rate_limited"),
        RequestResult.InvalidValue => UiText.T("lobby.denied.invalid"),
        _ => UiText.T("lobby.denied.not_now"),
    };

    private static string DescribeDenied(RequestResult result) => DescribeResult(result);

    private static string DescribeEnd(SessionEndReason reason, RejectReason rejection) => reason switch
    {
        SessionEndReason.Rejected => rejection switch
        {
            RejectReason.RoomFull => UiText.T("lobby.end.room_full"),
            RejectReason.MatchInProgress => UiText.T("lobby.end.in_progress"),
            RejectReason.Banned => UiText.T("lobby.end.banned"),
            RejectReason.GameVersionMismatch or RejectReason.ProtocolMismatch => UiText.T("lobby.end.version"),
            RejectReason.ContentVersionMismatch => UiText.T("lobby.end.content_version"),
            RejectReason.RulesMismatch => UiText.T("lobby.end.rules"),
            RejectReason.WorldTooLarge => UiText.F("lobby.end.world_too_large", Tribes.Client.Session.SessionText.LimitMegabytes),
            _ => UiText.T("lobby.end.rejected"),
        },
        SessionEndReason.WorldTooLarge => UiText.F("lobby.end.world_too_large", Tribes.Client.Session.SessionText.LimitMegabytes),
        SessionEndReason.HostClosed => UiText.T("lobby.end.host_closed"),
        SessionEndReason.ConnectionLost => UiText.T("lobby.end.connection_lost"),
        SessionEndReason.Kicked => UiText.T("lobby.end.kicked"),
        SessionEndReason.Left => UiText.T("lobby.end.left"),
        _ => UiText.T("lobby.end.error"),
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (Client is not null)
        {
            Client.Leave();
            Client.Dispose();
        }
        if (Host is not null)
        {
            Host.Close();
            Host.Dispose();
            MulticastLease.Release();
        }
    }
}
