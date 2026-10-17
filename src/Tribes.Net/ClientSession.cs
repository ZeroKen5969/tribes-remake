using System;
using System.Collections.Generic;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

/// <summary>
/// A player's (or spectator's) side of a room: joins the host, takes part in the lobby, and in a
/// match sends commands to the host and runs its simulation with the batches the host relays.
/// When the connection drops during a match it connects again by itself and catches up, from the
/// missed batches or from a fresh snapshot. When the host leaves a match on a transport with host
/// migration, the next player in line takes over (possibly this one, see <see cref="BecameHost"/>)
/// and the others follow; commands that had not run yet are sent again.
/// </summary>
/// <remarks>
/// Single-threaded: call <see cref="Update"/> every frame on the game thread; every event is
/// raised from inside it (or from the method the game called).
/// </remarks>
public sealed partial class ClientSession : IDisposable
{
    private static readonly TimeSpan BacklogWindow = TimeSpan.FromSeconds(1);
    private const int MaxEarlyBatches = 100_000;
    private const int MaxPendingCommands = 1024;
    private const int HashMemory = 512;
    private readonly ITransport transport;
    private readonly ClientOptions options;
    private readonly IClock clock;
    private readonly Func<MatchStart, ILockstepSimulation> simulationFactory;
    private readonly InputDelayController inputDelay;
    private readonly List<TickBatch> earlyBatches = [];
    private readonly List<(long Sequence, byte[] Payload)> sentCommands = [];
    private readonly BatchHistory receivedBatches;
    private HashLedger ownHashes = new(HashMemory);
    private MatchLedger? ledger;
    private RoomExtras extras = RoomExtras.Empty;
    private long nextSequence = 1;
    private IConnection? connection;
    private string? address;
    private LockstepEngine? engine;
    private TickPacer? pacer;
    private SnapshotAssembler? assembler;
    private bool desynced;
    private Guid timelineId;
    private int reconnectsLeft;
    private TimeSpan reconnectAt;
    private int standingBacklog = int.MaxValue;
    private TimeSpan backlogWindowStart;
    private bool disposed;

    /// <summary>Creates a session; <see cref="Connect"/> joins a room.</summary>
    /// <param name="transport">A transport that is not hosting; the session uses its client role.</param>
    /// <param name="options">Versions, the persistent player id and name.</param>
    /// <param name="simulationFactory">Builds the game simulation when a match starts or is joined.</param>
    /// <param name="clock">Time source; null for the real clock.</param>
    public ClientSession(ITransport transport, ClientOptions options, Func<MatchStart, ILockstepSimulation> simulationFactory, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(simulationFactory);
        string? problem = options.Lockstep.FindProblem() ?? FindProblem(options);
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        this.transport = transport;
        this.options = options;
        this.simulationFactory = simulationFactory;
        this.clock = clock ?? SystemClock.Instance;
        inputDelay = new InputDelayController(options.Lockstep.MinInputDelayTicks, options.Lockstep.MaxInputDelayTicks);
        receivedBatches = new BatchHistory(options.Lockstep.BatchHistoryTicks);
        transport.PeerConnected += OnPeerConnected;
        transport.PeerDisconnected += OnPeerDisconnected;
        transport.MessageReceived += OnMessageReceived;
    }

    /// <summary>Where the session is.</summary>
    public ClientPhase Phase { get; private set; }

    /// <summary>The room as the host last described it, or null before joining.</summary>
    public RoomState? Room { get; private set; }

    /// <summary>
    /// The id the room shows for this player (<see cref="PlayerInfo.Id"/>), derived from the secret
    /// <see cref="ClientOptions.PlayerId"/>; empty until the host has answered.
    /// </summary>
    public Guid LocalPlayerId { get; private set; }

    /// <summary>This player in <see cref="Room"/>, or null.</summary>
    public PlayerInfo? LocalPlayer => Room?.FindPlayer(LocalPlayerId);

    /// <summary>The match's start data, or null outside a match.</summary>
    public MatchStart? Match { get; private set; }

    /// <summary>The simulation this session drives, or null outside a match.</summary>
    public ILockstepSimulation? Simulation => engine?.Simulation;

    /// <summary>How far the snapshot of a running match has come in, or null while none is on its way.</summary>
    public SnapshotProgress? SnapshotDownload => assembler is { } coming
        ? new SnapshotProgress(coming.Received, coming.Header.ChunkCount, coming.Header.RawLength)
        : null;

    /// <summary>Whether the host paused the match.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Game speed in percent of normal.</summary>
    public int SpeedPercent { get; private set; } = 100;

    /// <summary>Ticks between sending a command and running it, adapted to the round trip.</summary>
    public int InputDelayTicks => inputDelay.Current;

    /// <summary>Smoothed round trip to the host.</summary>
    public TimeSpan RoundTripTime => connection?.RoundTripTime ?? TimeSpan.Zero;

    /// <summary>Batches received but not run yet.</summary>
    public int BufferedTicks => engine?.Buffered ?? 0;

    /// <summary>Whether the simulation is running extra ticks to catch up with the host.</summary>
    public bool IsCatchingUp => BufferedTicks > options.Lockstep.CatchUpThresholdTicks;

    /// <summary>Whether <see cref="SendCommand"/> would send now (an active player in a running match).</summary>
    public bool CanSendCommands =>
        Phase == ClientPhase.InGame && engine is not null && connection is not null && LocalPlayer?.CanSendCommands == true;

    /// <summary>Why the session ended; <see cref="SessionEndReason.None"/> before.</summary>
    public SessionEndReason EndReason { get; private set; }

    /// <summary>For a rejected join, the host's reason.</summary>
    public RejectReason Rejection { get; private set; }

    /// <summary>A sentence about the end, to show the player.</summary>
    public string EndMessage { get; private set; } = string.Empty;

    /// <summary>The phase changed.</summary>
    public event EventHandler<ClientPhaseChangedEventArgs>? PhaseChanged;

    /// <summary>The room changed.</summary>
    public event EventHandler<RoomChangedEventArgs>? RoomChanged;

    /// <summary>A chat line arrived.</summary>
    public event EventHandler<ChatReceivedEventArgs>? ChatReceived;

    /// <summary>A match began on this peer (at the start, or joined from a snapshot).</summary>
    public event EventHandler<MatchStartedEventArgs>? MatchStarted;

    /// <summary>A slot's player joined, left, returned or forfeited, at the tick it took effect.</summary>
    public event EventHandler<PlayerPresenceChangedEventArgs>? PlayerPresenceChanged;

    /// <summary>A tick ran.</summary>
    public event EventHandler<TickSteppedEventArgs>? TickStepped;

    /// <summary>The host found this peer's state differing from its own.</summary>
    public event EventHandler<DesyncDetectedEventArgs>? DesyncDetected;

    /// <summary>The host turned down a request.</summary>
    public event EventHandler<RequestDeniedEventArgs>? RequestDenied;

    /// <summary>The match was paused or resumed, or the speed changed.</summary>
    public event EventHandler<ControlStateChangedEventArgs>? ControlStateChanged;

    /// <summary>The session ended.</summary>
    public event EventHandler<SessionEndedEventArgs>? Ended;

    /// <summary>
    /// The host left and this peer now hosts the match. Switch to the given
    /// <see cref="HostSession"/> (same simulation); this session then ends with
    /// <see cref="SessionEndReason.BecameHost"/>.
    /// </summary>
    public event EventHandler<BecameHostEventArgs>? BecameHost;

    /// <summary>The host left and this client now plays on with the player who took over.</summary>
    public event EventHandler<HostChangedEventArgs>? HostChanged;

    /// <summary>Starts joining the room at <paramref name="address"/>.</summary>
    /// <param name="address">A <see cref="RoomInfo.Address"/> or an address the player typed.</param>
    public void Connect(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Phase != ClientPhase.Idle)
        {
            throw new InvalidOperationException("A session connects once; create a new session to join another room.");
        }

        transport.Connect(address);
        this.address = address;
        reconnectsLeft = options.ReconnectAttempts;
        SetPhase(ClientPhase.Connecting);
    }

    /// <summary>Handles network traffic and runs the ticks that are due. Call it every frame.</summary>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        transport.Poll();
        TimeSpan now = clock.Now;
        if (Phase == ClientPhase.Reconnecting && connection is null && now >= reconnectAt)
        {
            Reconnect();
        }

        if (Phase == ClientPhase.Migrating)
        {
            ContinueMigration(now);
        }

        if (Phase == ClientPhase.InGame)
        {
            RunSimulation(now);
        }
    }

    /// <summary>Asks for a colour (lobby only). The host may still deny it; see <see cref="RequestDenied"/>.</summary>
    /// <param name="colour">Colour id 0 to 7.</param>
    public RequestResult ChooseColour(int colour)
    {
        if (!RoomLimits.IsValidColour(colour))
        {
            return RequestResult.InvalidValue;
        }

        if (Phase != ClientPhase.Lobby || Room is null)
        {
            return RequestResult.NotAllowedNow;
        }

        if (Room.IsColourTaken(colour, LocalPlayerId))
        {
            return RequestResult.ColourTaken;
        }

        return Request(new ChooseColourMessage(colour));
    }

    /// <summary>Asks for a team (versus lobby only).</summary>
    /// <param name="team">Team id 0 to 3.</param>
    public RequestResult ChooseTeam(int team)
    {
        if (!RoomLimits.IsValidTeam(team))
        {
            return RequestResult.InvalidValue;
        }

        if (Phase != ClientPhase.Lobby || Room?.Settings.Mode != RoomMode.Versus)
        {
            return RequestResult.NotAllowedNow;
        }

        return Request(new ChooseTeamMessage(team));
    }

    /// <summary>Says ready or not ready (lobby only).</summary>
    /// <param name="ready">Ready to start.</param>
    public RequestResult SetReady(bool ready)
    {
        if (Phase != ClientPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        return LocalPlayer is { HasSlot: true } ? Request(new SetReadyMessage(ready)) : RequestResult.NotAPlayer;
    }

    /// <summary>Sends a chat line to everyone in the room.</summary>
    /// <param name="text">The line; control characters are removed and it is cut to <see cref="RoomLimits.MaxChatLength"/>.</param>
    public RequestResult SendChat(string text)
    {
        string clean = RoomLimits.CleanText(text, RoomLimits.MaxChatLength);
        if (clean.Length == 0)
        {
            return RequestResult.InvalidValue;
        }

        return Phase is ClientPhase.Lobby or ClientPhase.Loading or ClientPhase.InGame
            ? Request(new ChatMessage(clean))
            : RequestResult.NotAllowedNow;
    }

    /// <summary>
    /// Sends a game command of this player's tribe. The host runs it on every peer at the tick
    /// <see cref="InputDelayTicks"/> ahead of the newest batch, or later if it arrives late. Until
    /// it has run, the session keeps it and sends it again after a reconnect or a host change.
    /// </summary>
    /// <param name="payload">The game's encoding of the command.</param>
    /// <returns>False when this player may not send commands now (spectator, forfeited, not in a match).</returns>
    public bool SendCommand(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (payload.Length > options.Lockstep.MaxCommandBytes)
        {
            throw new ArgumentException($"A command may be at most {options.Lockstep.MaxCommandBytes} bytes.", nameof(payload));
        }

        if (!CanSendCommands)
        {
            return false;
        }

        byte[] copy = payload.ToArray();
        long sequence = nextSequence++;
        sentCommands.Add((sequence, copy));
        if (sentCommands.Count > MaxPendingCommands)
        {
            sentCommands.RemoveAt(0);
        }

        Send(new CommandMessage(CommandTarget(), sequence, copy));
        return true;
    }

    /// <summary>Asks the host to pause the match.</summary>
    public RequestResult RequestPause() => ControlRequest(ControlKind.Pause, 0);

    /// <summary>Asks the host to continue the match.</summary>
    public RequestResult RequestResume() => ControlRequest(ControlKind.Resume, 0);

    /// <summary>Asks the host for another game speed; the host decides.</summary>
    /// <param name="speedPercent">Speed in percent of normal, 25 to 400.</param>
    public RequestResult RequestSpeed(int speedPercent) =>
        RoomLimits.IsValidSpeed(speedPercent) ? ControlRequest(ControlKind.Speed, speedPercent) : RequestResult.InvalidValue;

    /// <summary>Gives up the match. Final: the player may keep watching, and can rejoin this match only as a spectator.</summary>
    public RequestResult Forfeit()
    {
        if (Phase != ClientPhase.InGame || LocalPlayer is not { HasSlot: true, Presence: not PlayerPresence.Forfeited })
        {
            return RequestResult.NotAllowedNow;
        }

        return Request(new ForfeitMessage());
    }

    /// <summary>
    /// Leaves the room. In a match the tribe stays in the world without orders (the computer does
    /// not play it) until the player joins again with the same player id.
    /// </summary>
    public void Leave()
    {
        if (Phase is ClientPhase.Ended or ClientPhase.Idle)
        {
            return;
        }

        IConnection? current = connection;
        current?.Send(MessageCodec.Encode(new LeaveMessage()));
        End(SessionEndReason.Left, "You left the room.", RejectReason.None);
        current?.Disconnect();
    }

    /// <summary>Leaves the room; see <see cref="Leave"/>.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Leave();
        Detach();
        disposed = true;
    }

    private void Reconnect()
    {
        reconnectAt = TimeSpan.MaxValue;
        try
        {
            transport.Connect(address!);
        }
        catch (InvalidOperationException ex)
        {
            // The transport is busy with another connection (for example the game shares it).
            End(SessionEndReason.ConnectionLost, ex.Message, RejectReason.None);
        }
    }

    /// <summary>An ended session lets go of the transport, so a new session can use it.</summary>
    private void Detach()
    {
        transport.PeerConnected -= OnPeerConnected;
        transport.PeerDisconnected -= OnPeerDisconnected;
        transport.MessageReceived -= OnMessageReceived;
    }

    private static string? FindProblem(ClientOptions options)
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

        if (options.ReconnectAttempts < 0 || options.ReconnectDelay < TimeSpan.Zero)
        {
            return "ReconnectAttempts and ReconnectDelay must not be negative.";
        }

        return options.PreferredColour is < -1 or >= RoomLimits.ColourCount ? "PreferredColour must be -1 or a colour id." : null;
    }

    private RequestResult ControlRequest(ControlKind kind, int value) =>
        Phase == ClientPhase.InGame ? Request(new ControlRequestMessage(kind, value)) : RequestResult.NotAllowedNow;

    private RequestResult Request(NetMessage message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection is null)
        {
            return RequestResult.NotAllowedNow;
        }

        Send(message);
        return RequestResult.Accepted;
    }

    private void Send(NetMessage message) => connection?.Send(MessageCodec.Encode(message));

    private void SetPhase(ClientPhase phase)
    {
        if (Phase != phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke(this, new ClientPhaseChangedEventArgs { Phase = phase });
        }
    }

    private void SetRoom(RoomState room)
    {
        if (Room?.RoomId != room.RoomId)
        {
            LocalPlayerId = SessionShared.PublicId(options.PlayerId, room.RoomId);
        }

        Room = room;
        RoomChanged?.Invoke(this, new RoomChangedEventArgs { Room = room });
    }

    private void End(SessionEndReason reason, string text, RejectReason rejection)
    {
        if (Phase == ClientPhase.Ended)
        {
            return;
        }

        EndReason = reason;
        Rejection = rejection;
        EndMessage = text;
        Detach();
        SetPhase(ClientPhase.Ended);
        Ended?.Invoke(this, new SessionEndedEventArgs { Reason = reason, Rejection = rejection, Message = text });
    }

    private void Fail(SessionEndReason reason, string text, RejectReason rejection = RejectReason.None)
    {
        IConnection? current = connection;
        End(reason, text, rejection);
        current?.Disconnect();
    }

    private void OnPeerConnected(object? sender, PeerEventArgs e)
    {
        if (Phase is not (ClientPhase.Connecting or ClientPhase.Reconnecting or ClientPhase.Migrating) || connection is not null)
        {
            e.Connection.Disconnect();
            return;
        }

        connection = e.Connection;
        long resumeTick = engine is not null && !desynced ? engine.Simulation.Tick : -1;
        Send(new HelloMessage(
            NetProtocol.Version,
            options.GameVersion,
            options.ContentVersion,
            options.PlayerId,
            SessionShared.CleanName(options.PlayerName),
            options.Spectate,
            options.PreferredColour,
            engine is not null ? timelineId : Guid.Empty,
            resumeTick,
            transport.MigrationAddress ?? string.Empty,
            options.RulesFingerprint));
    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        // Another connection of the transport (one this session closed, or one an earlier session
        // on the same transport closed) ends with Local; only the current one or a failed attempt counts.
        if (connection is null ? e.Reason == DisconnectReason.Local : e.Connection != connection)
        {
            return;
        }

        connection = null;
        if (Phase == ClientPhase.Idle)
        {
            return;
        }

        bool inMatch = Phase is ClientPhase.InGame or ClientPhase.Loading or ClientPhase.Reconnecting or ClientPhase.Migrating;
        if (inMatch && HostIsGone(e.Reason))
        {
            OnHostGone();
            return;
        }

        if (inMatch && reconnectsLeft > 0)
        {
            reconnectsLeft--;
            reconnectAt = clock.Now + options.ReconnectDelay;
            assembler = null;
            earlyBatches.Clear();
            engine?.ClearBuffered();
            SetPhase(ClientPhase.Reconnecting);
            return;
        }

        End(
            SessionEndReason.ConnectionLost,
            Phase == ClientPhase.Connecting ? "Could not reach the host." : "The connection to the host was lost.",
            RejectReason.None);
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.Connection != connection)
        {
            return;
        }

        try
        {
            NetMessage message = MessageCodec.Decode(e.Data);
            if (message is BatchMessage batch)
            {
                // Kept as received, so this peer can hand them on if it takes over hosting.
                receivedBatches.Add(batch.Batch.Tick, e.Data.ToArray());
            }

            Handle(message);
        }
        catch (ProtocolException ex)
        {
            Fail(SessionEndReason.ProtocolError, ex.Message);
        }
    }
}
