using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>
/// A transport over Photon Realtime (docs/NETWORK.md, 「Photon Realtime 的對應方式」). Hosting creates a
/// Photon room with a six-character code; its room property <c>h</c> names the actor that hosts
/// the session. Clients join the room and open a connection to that actor (Open/Accept events);
/// all messages are targeted, reliable, ordered Photon events. Because the room outlives its host,
/// the host role can move: when the host leaves, another member wins
/// <see cref="TakeOverHost"/> (a compare-and-swap of <c>h</c> on the Photon server) and the others
/// connect to it.
/// </summary>
/// <remarks>
/// Like every transport, all events are raised from <see cref="Poll"/> on the caller's thread.
/// Connecting is asynchronous: <see cref="Status"/> and <see cref="LastError"/> tell the game how far
/// it got and why it failed (no internet, unknown AppId, server full, region, room not found).
/// Runtime failures never throw; only misuse (calling in the wrong role, bad arguments) does.
/// </remarks>
public sealed partial class PhotonTransport : ITransport
{
    private static readonly TimeSpan PumpStep = TimeSpan.FromMilliseconds(1);
    private readonly PhotonSettings settings;
    private readonly Func<IPhotonLink> createLink;
    private readonly IClock clock;
    private readonly Action<TimeSpan> wait;
    private readonly Random random;
    private readonly Queue<TransportEvent> events = new();
    private readonly List<IConnection> connections = [];
    private readonly List<IPhotonLink> retired = [];
    private IPhotonLink? link;
    private bool linkReachedServer;
    private int othersInRoom;
    private TimeSpan hostHeardAt;
    private int silentHost;
    private int servicing;
    private int nextConnectionId = 1;
    private int nextNonce;
    private bool disposed;
    private bool statusDirty;

    /// <summary>Creates a transport that connects with <paramref name="settings"/>.</summary>
    /// <param name="settings">The AppId and connection options (<see cref="PhotonSettings.FromBuild"/>).</param>
    /// <exception cref="ArgumentException">The settings are not usable (<see cref="PhotonSettings.FindProblem"/>).</exception>
    public PhotonTransport(PhotonSettings settings)
        : this(settings, () => new RealtimeLink(), SystemClock.Instance, Thread.Sleep)
    {
    }

    internal PhotonTransport(PhotonSettings settings, Func<IPhotonLink> createLink, IClock clock, Action<TimeSpan> wait, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(createLink);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(wait);
        if (settings.FindProblem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(settings));
        }

        this.settings = settings;
        this.createLink = createLink;
        this.clock = clock;
        this.wait = wait;
        random = seed is { } fixedSeed ? new Random(fixedSeed) : new Random();
        nextNonce = random.Next(1, int.MaxValue / 2);
    }

    /// <inheritdoc />
    public TransportRole Role { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<IConnection> Connections => connections;

    /// <inheritdoc />
    /// <remarks>
    /// The room's address, <c>region/CODE</c>, once Photon created the room; null before (and
    /// while the connection to Photon is lost).
    /// </remarks>
    public string? HostAddress =>
        Role == TransportRole.Host && hostConfirmed && link is { State: LinkState.Room, Region: { } region } && roomCode is { } code
            ? PhotonAddress.Room(region, code)
            : null;

    /// <summary>The code of the room this transport hosts or is in (the part players type), or null.</summary>
    public string? RoomCode => roomCode;

    /// <summary>The region connected to (the best one when none was chosen), once known.</summary>
    public string? Region => link?.Region ?? roomRegion;

    /// <summary>How far connecting got; changes are announced by <see cref="StatusChanged"/>.</summary>
    public PhotonStatus Status { get; private set; }

    /// <summary>Why the last connection attempt failed or the connection ended; <see cref="PhotonError.None"/> while all is well.</summary>
    public PhotonError LastError { get; private set; }

    /// <summary>
    /// Whether this transport hosted a room with other players in it and lost its connection to
    /// Photon: the others carry on with a new host, so this device's hosted session is over (the
    /// player can come back to the match as a client).
    /// </summary>
    public bool HostingLost { get; private set; }

    /// <inheritdoc />
    public bool SupportsHostMigration => true;

    /// <inheritdoc />
    /// <remarks><c>actor:N</c>, this peer's actor in the room, while it is a client in a room.</remarks>
    public string? MigrationAddress =>
        Role == TransportRole.Client && link is { State: LinkState.Room } inRoom ? PhotonAddress.Actor(inRoom.LocalActor) : null;

    /// <inheritdoc />
    /// <remarks><c>actor:N</c> from the room property <c>h</c>, while that actor is in the room.</remarks>
    public string? RoomHostAddress => RecordedHost() is > 0 and int host && IsActorPresent(host) ? PhotonAddress.Actor(host) : null;

    /// <summary>The address of the room this transport is in or was last in, or null.</summary>
    private string? RoomAddress => roomCode is { } code && (link?.Region ?? roomRegion) is { } region ? PhotonAddress.Room(region, code) : null;

    /// <inheritdoc />
    public event EventHandler<PeerEventArgs>? PeerConnected;

    /// <inheritdoc />
    public event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;

    /// <inheritdoc />
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary><see cref="Status"/> or <see cref="LastError"/> changed; raised from <see cref="Poll"/>.</summary>
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    /// <remarks>
    /// Either a room address (<c>region/CODE</c>, or a code alone for the region connected to or
    /// <see cref="PhotonSettings.Region"/>) or, in the room this transport is in (or was last in),
    /// <c>actor:N</c> to reach the peer that hosts it now (after a host migration; it connects once
    /// the room records that actor as host). An attempt still under way is given up (it ends with
    /// <see cref="DisconnectReason.Local"/>): during a host migration the session moves on to the
    /// next peer in line while the room has not named its new host yet.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="address"/> is neither.</exception>
    public void Connect(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role == TransportRole.Host)
        {
            throw new InvalidOperationException("A hosting transport cannot connect to another host.");
        }

        if (hostConnection is { IsConnected: true })
        {
            throw new InvalidOperationException("The transport is already connected.");
        }

        if (pending is { } abandoned)
        {
            pending = null;
            abandoned.Connection.ClosedByThisSide = true;
            abandoned.Connection.State = ConnectionState.Closed;
            Queue(TransportEventKind.Disconnected, abandoned.Connection, DisconnectReason.Local);
        }

        StartConnect(address);
    }

    /// <inheritdoc />
    public void SendToMany(IReadOnlyList<IConnection> recipients, ReadOnlySpan<byte> message)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        CheckSize(message);
        var targets = new List<PhotonConnection>(recipients.Count);
        foreach (IConnection recipient in recipients)
        {
            if (recipient is PhotonConnection connection && CanSendOn(connection) && !targets.Contains(connection))
            {
                targets.Add(connection);
            }
        }

        if (targets.Count > 0)
        {
            Enqueue(targets, message.ToArray());
        }
    }

    /// <inheritdoc />
    public void Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (link is { } current)
        {
            ServiceLink(current);
        }

        FlushOutboxes();
        TimeSpan now = clock.Now;
        CheckHostSilence(now);
        CheckPendingTimeout(now);
        CheckRehost(now);
        PingConnections(now);
        RaiseQueued();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Closes every connection, sends what is still queued (a host leaving tells the others, up to
    /// <see cref="PhotonSettings.FlushTimeout"/>) and disconnects from Photon. The room goes on
    /// without this peer (its clients see <see cref="DisconnectReason.HostLeft"/> when it hosted) or
    /// disappears when nobody is left.
    /// </remarks>
    public void Shutdown()
    {
        if (pending is { } attempt)
        {
            pending = null;
            attempt.Connection.ClosedByThisSide = true;
            attempt.Connection.State = ConnectionState.Closed;
            Queue(TransportEventKind.Disconnected, attempt.Connection, DisconnectReason.Local);
        }

        // What is queued goes out first. A client says goodbye; a host just leaves, so its clients
        // learn that the host left.
        foreach (PhotonConnection connection in OpenConnections())
        {
            connection.ClosedByThisSide = true;
            if (Role == TransportRole.Client)
            {
                EnqueueClose(connection);
            }
        }

        DrainOutboxes();
        foreach (PhotonConnection connection in OpenConnections())
        {
            CloseConnection(connection, DisconnectReason.Local);
        }

        foreach (PhotonConnection connection in draining.ToArray())
        {
            DropQueued(connection);
        }

        DropLink(flush: true);
        ResetHosting();
        silentHost = 0;
        roomCode = null;
        roomRegion = null;
        Role = TransportRole.None;
        SetStatus(PhotonStatus.Offline, PhotonError.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Shutdown();
        disposed = true;
    }

    /// <summary>Sends one message on <paramref name="connection"/> (see <see cref="IConnection.Send"/>).</summary>
    internal void SendOn(PhotonConnection connection, ReadOnlySpan<byte> message)
    {
        CheckSize(message);
        if (CanSendOn(connection))
        {
            Enqueue([connection], message.ToArray());
        }
    }

    /// <summary>
    /// Ends <paramref name="connection"/> from this side. A client leaves the room and Photon with
    /// it (it connects again with <see cref="Connect"/>); a host only stops talking to that actor,
    /// which then leaves on its own.
    /// </summary>
    internal void CloseLocally(PhotonConnection connection)
    {
        if (!ReferenceEquals(connection.Owner, this))
        {
            return;
        }

        // Also when the other side closed it first: nothing more is delivered on it from now on.
        connection.ClosedByThisSide = true;
        if (connection.State == ConnectionState.Closed)
        {
            return;
        }

        if (ReferenceEquals(pending?.Connection, connection))
        {
            pending = null;
            connection.State = ConnectionState.Closed;
            Queue(TransportEventKind.Disconnected, connection, DisconnectReason.Local);
        }
        else
        {
            EnqueueClose(connection);
            CloseConnection(connection, DisconnectReason.Local);
        }

        if (Role == TransportRole.Client)
        {
            DrainOutboxes();
            DropQueued(connection);
            DropLink(flush: true);
            SetStatus(PhotonStatus.Offline, PhotonError.None);
        }
    }

    private bool CanSendOn(PhotonConnection connection) => connection.IsConnected && ReferenceEquals(connection.Owner, this);

    private void CheckSize(ReadOnlySpan<byte> message)
    {
        if (message.Length > settings.MaxMessageBytes)
        {
            throw new ArgumentException($"A message may be at most {settings.MaxMessageBytes} bytes.", nameof(message));
        }
    }

    private void SendControl(byte code, byte[] content, int actor)
    {
        if (actor > 0 && link is { State: LinkState.Room } current)
        {
            current.RaiseEvent(code, content, [actor]);
        }
    }

    private int NextNonce() => nextNonce = nextNonce == int.MaxValue ? 1 : nextNonce + 1;

    private PhotonConnection NewConnection(int nonce, int actor, string remoteAddress, ConnectionState state) =>
        new(this, nextConnectionId++, nonce, actor, remoteAddress, state, settings.MaxMessageBytes);

    private void Queue(TransportEventKind kind, IConnection connection, DisconnectReason reason, ReadOnlyMemory<byte> data = default) =>
        events.Enqueue(new TransportEvent(kind, connection, reason, data));

    /// <summary>Opens <paramref name="connection"/>: messages flow from now on; the event follows in this poll.</summary>
    private void OpenConnection(PhotonConnection connection)
    {
        connection.State = ConnectionState.Open;
        connection.LastPing = clock.Now - settings.PingInterval;
        Queue(TransportEventKind.Connected, connection, DisconnectReason.None);
    }

    private void CloseConnection(PhotonConnection connection, DisconnectReason reason)
    {
        if (connection.State == ConnectionState.Closed)
        {
            return;
        }

        bool wasOpen = connection.State == ConnectionState.Open;
        connection.State = ConnectionState.Closed;
        connection.Assembler.Reset();
        if (reason != DisconnectReason.Local)
        {
            DropQueued(connection);
        }

        ForgetConnection(connection);
        Queue(TransportEventKind.Disconnected, connection, wasOpen ? reason : DisconnectReason.ConnectFailed);
    }

    /// <summary>The connections that are open now (also those whose event is still queued).</summary>
    private List<PhotonConnection> OpenConnections()
    {
        var open = new List<PhotonConnection>(clientsByActor.Values);
        if (hostConnection is { IsConnected: true } toHost)
        {
            open.Add(toHost);
        }

        return open;
    }

    private void RaiseQueued()
    {
        while (events.Count > 0)
        {
            TransportEvent e = events.Dequeue();
            switch (e.Kind)
            {
                case TransportEventKind.Connected:
                    connections.Add(e.Connection);
                    PeerConnected?.Invoke(this, new PeerEventArgs(e.Connection));
                    break;
                case TransportEventKind.Disconnected:
                    connections.Remove(e.Connection);
                    PeerDisconnected?.Invoke(this, new PeerDisconnectedEventArgs(e.Connection, e.Reason));
                    break;
                default:
                    // Messages that arrived before the other side closed are delivered; once this
                    // side closed a connection, nothing more is delivered on it.
                    if (e.Connection is PhotonConnection { ClosedByThisSide: false })
                    {
                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(e.Connection, e.Data));
                    }

                    break;
            }
        }

        if (statusDirty)
        {
            statusDirty = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetStatus(PhotonStatus newStatus, PhotonError error)
    {
        if (Status == newStatus && LastError == error)
        {
            return;
        }

        PhotonLog.Write($"PHOTON transport ({Role}): {newStatus}{(error == PhotonError.None ? string.Empty : ", " + error)}");
        Status = newStatus;
        LastError = error;
        statusDirty = true;
    }

    private void PingConnections(TimeSpan now)
    {
        foreach (PhotonConnection connection in OpenConnections())
        {
            if (now - connection.LastPing >= settings.PingInterval)
            {
                connection.LastPing = now;
                SendControl(Wire.Ping, Wire.EncodeStamp(now), connection.Actor);
            }
        }
    }

    /// <summary>A new link that reports to this transport (the previous one is dropped first).</summary>
    private void StartLink(string? region)
    {
        DropLink(flush: false);
        IPhotonLink created = createLink();
        created.Listener = new LinkCallbacks(this, created);
        link = created;
        linkReachedServer = false;
        othersInRoom = 0;
        var options = new LinkConnectOptions(settings.AppId, settings.AppVersion, region, settings.UseWebSockets, settings.AllowWebSocketFallback);
        if (!created.Connect(options))
        {
            OnLinkLost(LinkFailure.ServerError);
        }
    }

    /// <summary>Disconnects the link, sending what it has queued first when <paramref name="flush"/>.</summary>
    private void DropLink(bool flush)
    {
        if (link is not { } current)
        {
            return;
        }

        link = null;
        current.Listener = null;
        if (flush && current.State != LinkState.Disconnected)
        {
            TimeSpan end = clock.Now + settings.FlushTimeout;
            while (current.SendOutgoing() && clock.Now < end)
            {
                wait(PumpStep);
                current.Service();
            }
        }

        current.Disconnect();
        if (servicing > 0)
        {
            // Inside one of the link's own callbacks: it is let go after its Service call returns.
            retired.Add(current);
        }
        else
        {
            current.Dispose();
        }
    }

    /// <summary>Runs <see cref="IPhotonLink.Service"/>; links dropped by its callbacks are disposed afterwards.</summary>
    private void ServiceLink(IPhotonLink current)
    {
        servicing++;
        try
        {
            current.Service();
        }
        finally
        {
            servicing--;
        }

        if (servicing == 0 && retired.Count > 0)
        {
            foreach (IPhotonLink old in retired)
            {
                old.Dispose();
            }

            retired.Clear();
        }
    }

    private void ForgetConnection(PhotonConnection connection)
    {
        if (clientsByActor.TryGetValue(connection.Actor, out PhotonConnection? known) && ReferenceEquals(known, connection))
        {
            clientsByActor.Remove(connection.Actor);
        }

        if (ReferenceEquals(hostConnection, connection))
        {
            hostConnection = null;
        }
    }

    private object? RoomProperty(string key) => link is { State: LinkState.Room } current ? current.RoomProperty(key) : null;

    /// <summary>The actor the room records as host (property h), or 0.</summary>
    private int RecordedHost() => RoomProperty(Wire.HostProperty) is int host ? host : 0;

    /// <summary>Whether <paramref name="actor"/> is in the room; a host this client found silent counts as gone.</summary>
    private bool IsActorPresent(int actor) => link is { State: LinkState.Room } current && actor != silentHost && current.Actors.Contains(actor);

    private bool InRoom(string? code) => link is { State: LinkState.Room } current && code is not null && current.RoomName == code;
}
