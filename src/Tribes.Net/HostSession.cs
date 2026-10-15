using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

/// <summary>
/// The host of a room: owns the roster and the lobby rules, and in a match collects the players'
/// commands, closes one batch per tick, relays it to every peer and runs its own simulation with
/// it. Peers never talk to each other (star topology). Keeps every record of the match, so it can
/// save it and later resume it.
/// </summary>
/// <remarks>
/// Single-threaded: call <see cref="Update"/> every frame on the game thread; every event is
/// raised from inside it (or from the method the game called).
/// </remarks>
public sealed partial class HostSession : IDisposable
{
    private readonly ITransport transport;
    private readonly HostOptions options;
    private readonly IClock clock;
    private readonly Func<MatchStart, ILockstepSimulation> simulationFactory;
    private readonly Dictionary<int, RemotePeer> peers = [];
    private readonly HostRoom room;
    private HostMatch? match;
    private bool closed;
    private bool disposed;

    /// <summary>Creates a room and starts hosting it on the transport.</summary>
    /// <param name="transport">A transport that is neither hosting nor connected.</param>
    /// <param name="options">Versions, the host player and the room settings.</param>
    /// <param name="simulationFactory">Builds the game simulation when a match starts.</param>
    /// <param name="clock">Time source; null for the real clock.</param>
    public HostSession(ITransport transport, HostOptions options, Func<MatchStart, ILockstepSimulation> simulationFactory, IClock? clock = null)
        : this(transport, Validated(options), simulationFactory, clock, CreateRoom(options))
    {
        StartHosting();
    }

    private HostSession(ITransport transport, HostOptions options, Func<MatchStart, ILockstepSimulation> simulationFactory, IClock? clock, HostRoom room)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(simulationFactory);
        this.transport = transport;
        this.options = options;
        this.simulationFactory = simulationFactory;
        this.clock = clock ?? SystemClock.Instance;
        this.room = room;
        transport.PeerConnected += OnPeerConnected;
        transport.PeerDisconnected += OnPeerDisconnected;
        transport.MessageReceived += OnMessageReceived;
    }

    /// <summary>The room as it is now.</summary>
    public RoomState Room => room.State;

    /// <summary>The host player.</summary>
    public PlayerInfo LocalPlayer => room.Host;

    /// <summary>The address clients connect to (for LAN, "ip:port"), or null after <see cref="Close"/>.</summary>
    public string? Address => transport.HostAddress;

    /// <summary>The running match's start data, or null in the lobby.</summary>
    public MatchStart? Match => match?.Start;

    /// <summary>The host's simulation, or null in the lobby.</summary>
    public ILockstepSimulation? Simulation => match?.Simulation;

    /// <summary>Whether the match is paused. A resumed match starts paused.</summary>
    public bool IsPaused => match?.Paused ?? false;

    /// <summary>Game speed in percent of normal.</summary>
    public int SpeedPercent => match?.SpeedPercent ?? 100;

    /// <summary>Whether <see cref="Close"/> was called.</summary>
    public bool IsClosed => closed;

    /// <summary>The room changed.</summary>
    public event EventHandler<RoomChangedEventArgs>? RoomChanged;

    /// <summary>A chat line was sent (including the host's own).</summary>
    public event EventHandler<ChatReceivedEventArgs>? ChatReceived;

    /// <summary>The match started; <see cref="Simulation"/> is set.</summary>
    public event EventHandler<MatchStartedEventArgs>? MatchStarted;

    /// <summary>A slot's player joined, left, returned or forfeited, at the tick it took effect.</summary>
    public event EventHandler<PlayerPresenceChangedEventArgs>? PlayerPresenceChanged;

    /// <summary>A tick ran.</summary>
    public event EventHandler<TickSteppedEventArgs>? TickStepped;

    /// <summary>A peer's state hash differed from the host's.</summary>
    public event EventHandler<DesyncDetectedEventArgs>? DesyncDetected;

    /// <summary>The match was paused or resumed, or the speed changed.</summary>
    public event EventHandler<ControlStateChangedEventArgs>? ControlStateChanged;

    /// <summary>A player asked for another speed; call <see cref="SetSpeed"/> to agree.</summary>
    public event EventHandler<SpeedRequestedEventArgs>? SpeedRequested;

    /// <summary>A player's command was dropped.</summary>
    public event EventHandler<CommandRejectedEventArgs>? CommandRejected;

    /// <summary>Handles network traffic and runs the ticks that are due. Call it every frame.</summary>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closed)
        {
            return;
        }

        transport.Poll();
        TimeSpan now = clock.Now;
        DropSilentPeers(now);
        ExpireAwaitedPlayers(now);
        if (match is not null)
        {
            RunMatch(match, now);
        }
    }

    /// <summary>
    /// Tells every peer the room is closing, stops hosting and lets go of the transport, which can
    /// then host again (for example a resumed match). The match can still be saved afterwards.
    /// </summary>
    public void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        byte[] end = MessageCodec.Encode(new SessionEndMessage(SessionEndReason.HostClosed, "The host closed the room."));
        transport.SendToMany([.. peers.Values.Select(p => p.Connection)], end);
        peers.Clear();
        room.Close();
        transport.Shutdown();
        Detach();
        RoomChanged?.Invoke(this, new RoomChangedEventArgs { Room = room.State });
    }

    /// <summary>Leaves the room; see <see cref="Leave"/> (during a match the host role moves on when the transport allows it).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Leave();
        disposed = true;
    }

    /// <summary>Starts hosting; when the transport refuses (for example the port is taken) the session lets go of it again.</summary>
    private void StartHosting()
    {
        try
        {
            transport.StartHost(BuildAdvertisement());
        }
        catch
        {
            Detach();
            throw;
        }
    }

    private void Detach()
    {
        transport.PeerConnected -= OnPeerConnected;
        transport.PeerDisconnected -= OnPeerDisconnected;
        transport.MessageReceived -= OnMessageReceived;
    }

    private static HostOptions Validated(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? problem = options.Room.FindProblem() ?? options.Lockstep.FindProblem() ?? FindIdentityProblem(options);
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        return options;
    }

    private static string? FindIdentityProblem(HostOptions options)
    {
        if (options.PlayerId == Guid.Empty)
        {
            return "PlayerId must not be empty.";
        }

        if (!SessionShared.IsValidVersion(options.GameVersion) || !SessionShared.IsValidVersion(options.ContentVersion))
        {
            return $"GameVersion and ContentVersion must have 1 to {RoomLimits.MaxVersionLength} characters.";
        }

        if (!SessionShared.IsValidFingerprint(options.RulesFingerprint))
        {
            return $"RulesFingerprint may have at most {RoomLimits.MaxVersionLength} characters.";
        }

        return options.HelloTimeout <= TimeSpan.Zero ? "HelloTimeout must be positive." : null;
    }

    private static HostRoom CreateRoom(HostOptions options)
    {
        string name = RoomLimits.CleanText(options.Room.Name, RoomLimits.MaxRoomNameLength);
        RoomSettings settings = options.Room with { Name = name.Length == 0 ? "Tribes" : name };
        Guid roomId = Guid.NewGuid();
        Guid hostId = SessionShared.PublicId(options.PlayerId, roomId);
        return new HostRoom(roomId, settings, hostId, SessionShared.CleanName(options.PlayerName), options.PreferredColour);
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e) =>
        peers[e.Connection.Id] = new RemotePeer(e.Connection, clock.Now);

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (peers.Remove(e.Connection.Id, out RemotePeer? peer) && peer.HasJoined)
        {
            HandleDeparture(peer.PlayerId);
        }
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (!peers.TryGetValue(e.Connection.Id, out RemotePeer? peer))
        {
            return;
        }

        NetMessage message;
        try
        {
            message = MessageCodec.Decode(e.Data);
        }
        catch (ProtocolException ex)
        {
            if (!peer.HasJoined && MessageCodec.TryReadHelloVersion(e.Data.Span, out int version) && version != NetProtocol.Version)
            {
                Reject(peer, RejectReason.ProtocolMismatch, SessionShared.ProtocolMismatchText(version));
            }
            else
            {
                Drop(peer, SessionEndReason.ProtocolError, ex.Message);
            }

            return;
        }

        if (peer.HasJoined)
        {
            Dispatch(peer, message);
        }
        else if (message is HelloMessage hello)
        {
            HandleHello(peer, hello);
        }
        else
        {
            Drop(peer, SessionEndReason.ProtocolError, "The first message must be a hello.");
        }
    }

    private void Dispatch(RemotePeer peer, NetMessage message)
    {
        switch (message)
        {
            case ChatMessage chat:
                HandleChat(peer, chat.Text);
                break;
            case ChooseColourMessage colour:
                Answer(peer, RequestKind.Colour, room.ChooseColour(peer.PlayerId, colour.Colour));
                break;
            case ChooseTeamMessage team:
                Answer(peer, RequestKind.Team, room.ChooseTeam(peer.PlayerId, team.Team));
                break;
            case SetReadyMessage ready:
                Answer(peer, RequestKind.Ready, room.SetReady(peer.PlayerId, ready.Ready));
                break;
            case LeaveMessage:
                peers.Remove(peer.Connection.Id);
                peer.Connection.Disconnect();
                HandleDeparture(peer.PlayerId);
                break;
            case ForfeitMessage:
                Answer(peer, RequestKind.Forfeit, ForfeitSlotHolder(peer.PlayerId));
                break;
            case CommandMessage command:
                HandleCommand(peer, command);
                break;
            case StateHashMessage hash:
                HandleStateHash(peer, hash);
                break;
            case ControlRequestMessage control:
                HandleControlRequest(peer, control);
                break;
            default:
                Drop(peer, SessionEndReason.ProtocolError, $"A client may not send {message.Type} messages.");
                break;
        }
    }

    private void HandleHello(RemotePeer peer, HelloMessage hello)
    {
        (RejectReason versionProblem, string versionText) = CheckVersions(hello);
        if (versionProblem != RejectReason.None)
        {
            Reject(peer, versionProblem, versionText);
            return;
        }

        if (hello.PlayerId == Guid.Empty)
        {
            Reject(peer, RejectReason.InvalidRequest, "The player id is empty.");
            return;
        }

        Guid playerId = SessionShared.PublicId(hello.PlayerId, room.RoomId);
        AdmitResult admitted = room.Admit(playerId, SessionShared.CleanName(hello.Name), hello.Spectate, hello.PreferredColour);
        if (admitted.Outcome == AdmitOutcome.Rejected)
        {
            Reject(peer, admitted.Reason, admitted.Message);
            return;
        }

        ReplaceOlderConnection(peer, playerId);
        awaitingReturn.Remove(playerId);
        peer.MigrationAddress = hello.MigrationAddress;
        peer.PlayerId = playerId;
        peer.HasJoined = true;
        if (match is null)
        {
            Send(peer, new WelcomeMessage(room.State, Extras(), -1, Guid.Empty, 0));
            PublishRoom();
            return;
        }

        JoinRunningMatch(peer, hello, admitted, match);
    }

    private (RejectReason Reason, string Text) CheckVersions(HelloMessage hello)
    {
        if (hello.ProtocolVersion != NetProtocol.Version)
        {
            return (RejectReason.ProtocolMismatch, SessionShared.ProtocolMismatchText(hello.ProtocolVersion));
        }

        if (!string.Equals(hello.GameVersion, options.GameVersion, StringComparison.Ordinal))
        {
            return (RejectReason.GameVersionMismatch, $"Your game version '{hello.GameVersion}' differs from the host's '{options.GameVersion}'.");
        }

        if (!string.Equals(hello.ContentVersion, options.ContentVersion, StringComparison.Ordinal))
        {
            return (RejectReason.ContentVersionMismatch, $"Your content version '{hello.ContentVersion}' differs from the host's '{options.ContentVersion}'.");
        }

        // Same version numbers, other rules: the match would not stay in sync (spectators run the rules too).
        if (options.RulesFingerprint.Length > 0 && !string.Equals(hello.RulesFingerprint, options.RulesFingerprint, StringComparison.Ordinal))
        {
            return (RejectReason.RulesMismatch, "Your game's rules differ from the host's: both need the same build of the game.");
        }

        return (RejectReason.None, string.Empty);
    }

    /// <summary>A player who connects again replaces a connection the host has not seen drop yet.</summary>
    private void ReplaceOlderConnection(RemotePeer peer, Guid playerId)
    {
        foreach (RemotePeer other in peers.Values.ToArray())
        {
            if (other != peer && other.HasJoined && other.PlayerId == playerId)
            {
                peers.Remove(other.Connection.Id);
                Send(other, new SessionEndMessage(SessionEndReason.ReplacedByNewConnection, "You connected again from another connection."));
                other.Connection.Disconnect();
            }
        }
    }

    private void HandleDeparture(Guid playerId)
    {
        PlayerInfo? player = room.Find(playerId);
        if (player is null || player.IsHost)
        {
            return;
        }

        if (match is null)
        {
            room.Remove(playerId);
        }
        else if (!player.HasSlot)
        {
            room.Remove(playerId);
        }
        else
        {
            room.SetConnected(playerId, false);
            awaitingReturn.Remove(playerId);
            if (player.Kind == PlayerKind.Human && player.Presence == PlayerPresence.Active)
            {
                MarkAbsent(player, match);
            }
        }

        PublishRoom();
    }

    private void DropSilentPeers(TimeSpan now)
    {
        foreach (RemotePeer peer in peers.Values.ToArray())
        {
            if (!peer.HasJoined && now - peer.ConnectedAt > options.HelloTimeout)
            {
                peers.Remove(peer.Connection.Id);
                peer.Connection.Disconnect();
            }
        }
    }

    private void Answer(RemotePeer peer, RequestKind request, RequestResult result)
    {
        if (result == RequestResult.Accepted)
        {
            PublishRoom();
        }
        else
        {
            Deny(peer, request, result);
        }
    }

    private static void Deny(RemotePeer peer, RequestKind request, RequestResult result) =>
        Send(peer, new RequestDeniedMessage(request, result, SessionShared.Describe(result)));

    private void Reject(RemotePeer peer, RejectReason reason, string text)
    {
        Send(peer, new RejectMessage(reason, text));
        peers.Remove(peer.Connection.Id);
        peer.Connection.Disconnect();
    }

    private void Drop(RemotePeer peer, SessionEndReason reason, string text)
    {
        Send(peer, new SessionEndMessage(reason, text));
        peers.Remove(peer.Connection.Id);
        peer.Connection.Disconnect();
        if (peer.HasJoined)
        {
            HandleDeparture(peer.PlayerId);
        }
    }

    private void PublishRoom()
    {
        RoomState state = room.State;
        transport.SendToMany(JoinedConnections(), MessageCodec.Encode(new RoomUpdateMessage(state, Extras())));
        if (transport.Role == TransportRole.Host)
        {
            transport.UpdateAdvertisement(BuildAdvertisement());
        }

        RoomChanged?.Invoke(this, new RoomChangedEventArgs { Room = state });
    }

    private RoomAdvertisement BuildAdvertisement()
    {
        RoomState state = room.State;
        bool inGame = state.Phase == RoomPhase.InGame;
        bool freeSlot = state.OccupiedSlots < state.Settings.MaxPlayers;
        bool joinableInProgress = state.Settings.Mode == RoomMode.Endless && state.Settings.AllowJoinInProgress;
        return new RoomAdvertisement
        {
            RoomId = state.RoomId,
            Name = state.Settings.Name,
            HostName = room.Host.Name,
            Mode = state.Settings.Mode,
            Players = state.OccupiedSlots,
            MaxPlayers = state.Settings.MaxPlayers,
            Spectators = state.SpectatorCount,
            GameVersion = options.GameVersion,
            ContentVersion = options.ContentVersion,
            InGame = inGame,
            IsOpen = !closed && freeSlot && (!inGame || joinableInProgress),
            JoinableInProgress = joinableInProgress,
            // Room lists show returning players their match and forfeited players that they can watch.
            Members = [.. state.Players.Where(p => p.HasSlot && p.Kind == PlayerKind.Human).Select(p => new RoomMember(p.Id, p.Presence))],
            SpectatorsWelcome = !closed && state.Settings.AllowSpectators && state.SpectatorCount < state.Settings.MaxSpectators,
            RulesFingerprint = options.RulesFingerprint.Length > 0 ? options.RulesFingerprint : null,
        };
    }

    private List<IConnection> JoinedConnections() =>
        [.. peers.Values.Where(p => p.HasJoined).Select(p => p.Connection)];

    private List<IConnection> MatchConnections() =>
        [.. peers.Values.Where(p => p.InMatch).Select(p => p.Connection)];

    private RemotePeer? FindPeer(Guid playerId) =>
        peers.Values.FirstOrDefault(p => p.HasJoined && p.PlayerId == playerId);

    private static void Send(RemotePeer peer, NetMessage message) => peer.Connection.Send(MessageCodec.Encode(message));
}
