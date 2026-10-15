using System;
using System.Collections.Generic;

namespace Tribes.Net.Transport.Loopback;

internal enum LoopbackPacketKind
{
    Open,
    OpenAck,
    Refused,
    Data,
    Ping,
    Pong,
    Close,
    HostLeft,
    Lost,
}

internal enum EndpointState
{
    Pending,
    Open,
    Lost,
    Closed,
}

internal readonly record struct LoopbackPacket(TimeSpan DeliverAt, LoopbackPacketKind Kind, byte[]? Data, TimeSpan Stamp);

/// <summary>
/// An in-process transport on a <see cref="LoopbackNetwork"/>. Hosting registers its
/// <see cref="Address"/>; clients connect to that address. Use it for tests and for local
/// play-throughs of the network code.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    private readonly List<LoopbackEndpoint> endpoints = [];
    private readonly List<IConnection> connections = [];
    private readonly Queue<TransportEvent> localEvents = new();
    private bool disposed;

    /// <summary>Creates a transport.</summary>
    /// <param name="network">The network it lives on.</param>
    /// <param name="address">Its name: the address clients connect to when it hosts.</param>
    public LoopbackTransport(LoopbackNetwork network, string address)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Network = network;
        Address = address;
        network.Add(this);
    }

    /// <summary>Its name on the network.</summary>
    public string Address { get; }

    /// <summary>Delay, jitter and loss of the connections this transport opens as a client.</summary>
    public LoopbackLinkOptions Link { get; set; } = new();

    /// <inheritdoc />
    public TransportRole Role { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<IConnection> Connections => connections;

    /// <inheritdoc />
    public string? HostAddress => Role == TransportRole.Host ? Address : null;

    /// <inheritdoc />
    public bool SupportsHostMigration => Network.SupportsHostMigration;

    /// <inheritdoc />
    /// <remarks>The transport's <see cref="Address"/> while it is a client in a room.</remarks>
    public string? MigrationAddress =>
        Network.SupportsHostMigration && Room is not null && Role == TransportRole.Client ? Address : null;

    /// <inheritdoc />
    public string? RoomHostAddress =>
        Room?.Host is { } host && Network.IsPresent(host.Address, Room) ? host.Address : null;

    internal LoopbackNetwork Network { get; }

    internal RoomAdvertisement? Advertisement { get; private set; }

    /// <summary>The room this transport hosts or joined, while it is in one.</summary>
    internal LoopbackRoom? Room { get; private set; }

    /// <summary>Whether <see cref="SimulateCrash"/> took the transport off the network.</summary>
    internal bool IsCrashed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<PeerEventArgs>? PeerConnected;

    /// <inheritdoc />
    public event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;

    /// <inheritdoc />
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <inheritdoc />
    public void StartHost(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role != TransportRole.None)
        {
            throw new InvalidOperationException("The transport is already hosting or connected.");
        }

        Network.Register(Address, this);
        LeaveRoom();
        Room = new LoopbackRoom(this);
        Advertisement = advertisement;
        Role = TransportRole.Host;
    }

    /// <inheritdoc />
    public bool IsPeerPresent(string migrationAddress) => Network.IsPresent(migrationAddress, Room);

    /// <inheritdoc />
    public string? TakeOverHost(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!Network.SupportsHostMigration)
        {
            throw new NotSupportedException("This loopback network does not support host migration.");
        }

        if (Room is not { } current || Role != TransportRole.Client)
        {
            throw new InvalidOperationException("Only a client in a room can take over hosting it.");
        }

        // As a relay's room property: the first peer to claim the empty host seat wins.
        if (current.Host is { } host && !ReferenceEquals(host, this) && Network.IsPresent(host.Address, current))
        {
            return host.Address;
        }

        Network.Register(Address, this);
        current.Host = this;
        Advertisement = advertisement;
        Role = TransportRole.Host;
        return null;
    }

    /// <summary>
    /// Takes the transport off the network at once, as a crashed device: no goodbye is sent, its
    /// connections drop, and with host migration its room goes on without it. Nobody can reach
    /// it afterwards.
    /// </summary>
    public void SimulateCrash()
    {
        if (IsCrashed || disposed)
        {
            return;
        }

        IsCrashed = true;
        bool hostGone = Role == TransportRole.Host && Network.SupportsHostMigration;
        foreach (LoopbackEndpoint endpoint in endpoints.ToArray())
        {
            Network.Sever(endpoint, hostGone);
        }

        if (Role == TransportRole.Host)
        {
            Network.Unregister(Address, this);
        }

        LeaveRoom();
        Advertisement = null;
        Role = TransportRole.None;
    }

    /// <inheritdoc />
    public void UpdateAdvertisement(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        if (Role != TransportRole.Host)
        {
            throw new InvalidOperationException("Only a hosting transport has an advertisement.");
        }

        Advertisement = advertisement;
    }

    /// <inheritdoc />
    public void Connect(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role == TransportRole.Host)
        {
            throw new InvalidOperationException("A hosting transport cannot connect to another host.");
        }

        if (endpoints.Exists(e => e.State is EndpointState.Pending or EndpointState.Open))
        {
            throw new InvalidOperationException("The transport is already connected or connecting.");
        }

        Role = TransportRole.Client;
        TimeSpan now = Network.Clock.Now;
        var local = new LoopbackEndpoint(this, Network.NextConnectionId(), address, Link, now, outgoing: true);
        endpoints.Add(local);
        LoopbackTransport? host = Network.FindHost(address);
        if (host is null)
        {
            local.Deliver(LoopbackPacketKind.Refused, null, now);
            return;
        }

        LoopbackEndpoint remote = host.AddIncoming(local, Address, Link, now);
        LeaveRoom();
        Room = host.Room;
        Room?.Members.Add(this);
        remote.Deliver(LoopbackPacketKind.Open, null, now);
    }

    /// <inheritdoc />
    public void SendToMany(IReadOnlyList<IConnection> recipients, ReadOnlySpan<byte> message)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        foreach (IConnection connection in recipients)
        {
            connection.Send(message);
        }
    }

    /// <inheritdoc />
    public void Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TimeSpan now = Network.Clock.Now;
        while (localEvents.Count > 0)
        {
            Raise(localEvents.Dequeue());
        }

        foreach (LoopbackEndpoint endpoint in endpoints.ToArray())
        {
            while (endpoint.TryTakeDue(now, out LoopbackPacket packet))
            {
                Handle(endpoint, packet, now);
            }

            endpoint.PingIfDue(now);
        }

        endpoints.RemoveAll(e => e.State == EndpointState.Closed);
    }

    /// <summary>Drops one connection as if the network failed; both sides see <see cref="DisconnectReason.ConnectionLost"/>.</summary>
    /// <param name="connection">A connection of this transport.</param>
    public void SimulateConnectionLoss(IConnection connection)
    {
        if (connection is not LoopbackEndpoint endpoint || !ReferenceEquals(endpoint.Owner, this))
        {
            throw new ArgumentException("The connection does not belong to this transport.", nameof(connection));
        }

        Network.Sever(endpoint);
    }

    /// <summary>Drops every connection of this transport as if the network failed.</summary>
    public void SimulateConnectionLoss()
    {
        foreach (LoopbackEndpoint endpoint in endpoints.ToArray())
        {
            Network.Sever(endpoint);
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        // A host leaving a room that outlives it tells its clients so, after their queued messages.
        bool hostLeaving = Role == TransportRole.Host && Network.SupportsHostMigration;
        foreach (LoopbackEndpoint endpoint in endpoints.ToArray())
        {
            CloseLocally(endpoint, hostLeaving);
        }

        if (Role == TransportRole.Host)
        {
            Network.Unregister(Address, this);
        }

        LeaveRoom();
        Advertisement = null;
        Role = TransportRole.None;
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

    private void LeaveRoom()
    {
        Room?.Leave(this);
        Room = null;
    }

    internal LoopbackEndpoint AddIncoming(LoopbackEndpoint client, string clientAddress, LoopbackLinkOptions link, TimeSpan now)
    {
        var endpoint = new LoopbackEndpoint(this, Network.NextConnectionId(), clientAddress, link, now, outgoing: false)
        {
            Peer = client,
        };
        client.Peer = endpoint;
        endpoints.Add(endpoint);
        return endpoint;
    }

    internal void CloseLocally(LoopbackEndpoint endpoint, bool hostLeaving = false)
    {
        EndpointState previous = endpoint.State;
        if (previous == EndpointState.Closed)
        {
            return;
        }

        endpoint.State = EndpointState.Closed;
        endpoint.Peer?.Deliver(hostLeaving ? LoopbackPacketKind.HostLeft : LoopbackPacketKind.Close, null, TimeSpan.Zero);
        bool wasOpen = previous == EndpointState.Open || (previous == EndpointState.Lost && endpoint.LostWhileOpen);
        if (wasOpen || endpoint.IsOutgoing)
        {
            localEvents.Enqueue(new TransportEvent(TransportEventKind.Disconnected, endpoint, DisconnectReason.Local, default));
        }
    }

    private void Handle(LoopbackEndpoint endpoint, LoopbackPacket packet, TimeSpan now)
    {
        switch (packet.Kind)
        {
            case LoopbackPacketKind.Open:
                Accept(endpoint, packet);
                break;
            case LoopbackPacketKind.OpenAck when endpoint.State == EndpointState.Pending:
                endpoint.State = EndpointState.Open;
                endpoint.AddRttSample(now - packet.Stamp);
                Raise(new TransportEvent(TransportEventKind.Connected, endpoint, DisconnectReason.None, default));
                break;
            case LoopbackPacketKind.Refused when endpoint.State == EndpointState.Pending:
                End(endpoint, DisconnectReason.ConnectFailed);
                break;
            case LoopbackPacketKind.Data when endpoint.State == EndpointState.Open:
                Raise(new TransportEvent(TransportEventKind.Message, endpoint, DisconnectReason.None, packet.Data));
                break;
            case LoopbackPacketKind.Ping when endpoint.State == EndpointState.Open:
                endpoint.Peer?.Deliver(LoopbackPacketKind.Pong, null, packet.Stamp);
                break;
            case LoopbackPacketKind.Pong:
                endpoint.AddRttSample(now - packet.Stamp);
                break;
            case LoopbackPacketKind.Close when endpoint.State is EndpointState.Open or EndpointState.Pending:
                End(endpoint, endpoint.State == EndpointState.Open ? DisconnectReason.Remote : DisconnectReason.ConnectFailed);
                break;
            case LoopbackPacketKind.HostLeft when endpoint.State is EndpointState.Open or EndpointState.Pending:
                End(endpoint, endpoint.State == EndpointState.Open ? DisconnectReason.HostLeft : DisconnectReason.ConnectFailed);
                break;
            case LoopbackPacketKind.Lost when endpoint.State == EndpointState.Lost:
                End(endpoint, endpoint.LostWhileOpen ? endpoint.LostReason : DisconnectReason.ConnectFailed);
                break;
            default:
                // Packets for a connection that already ended (or a duplicate handshake) are dropped.
                break;
        }
    }

    private void Accept(LoopbackEndpoint endpoint, LoopbackPacket packet)
    {
        if (endpoint.State != EndpointState.Pending)
        {
            return;
        }

        if (Role != TransportRole.Host)
        {
            endpoint.State = EndpointState.Closed;
            endpoint.Peer?.Deliver(LoopbackPacketKind.Refused, null, packet.Stamp);
            return;
        }

        endpoint.State = EndpointState.Open;
        endpoint.Peer?.Deliver(LoopbackPacketKind.OpenAck, null, packet.Stamp);
        Raise(new TransportEvent(TransportEventKind.Connected, endpoint, DisconnectReason.None, default));
    }

    private void End(LoopbackEndpoint endpoint, DisconnectReason reason)
    {
        bool announce = endpoint.State == EndpointState.Open || endpoint.LostWhileOpen || endpoint.IsOutgoing;
        endpoint.State = EndpointState.Closed;
        if (announce)
        {
            Raise(new TransportEvent(TransportEventKind.Disconnected, endpoint, reason, default));
        }
    }

    private void Raise(TransportEvent e)
    {
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
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(e.Connection, e.Data));
                break;
        }
    }
}

/// <summary>One side of a loopback connection.</summary>
internal sealed class LoopbackEndpoint : IConnection
{
    private readonly Queue<LoopbackPacket> inbound = new();
    private readonly RttEstimator rtt = new();
    private TimeSpan lastArrival;
    private TimeSpan lastPing;

    public LoopbackEndpoint(LoopbackTransport owner, int id, string remoteAddress, LoopbackLinkOptions link, TimeSpan now, bool outgoing)
    {
        Owner = owner;
        Id = id;
        RemoteAddress = remoteAddress;
        Link = link;
        IsOutgoing = outgoing;
        lastPing = now;
    }

    public int Id { get; }

    public string RemoteAddress { get; }

    public bool IsConnected => State == EndpointState.Open;

    public TimeSpan RoundTripTime => rtt.Smoothed;

    public TimeSpan RoundTripVariance => rtt.Variance;

    internal LoopbackTransport Owner { get; }

    internal LoopbackLinkOptions Link { get; }

    internal LoopbackEndpoint? Peer { get; set; }

    internal EndpointState State { get; set; }

    internal bool IsOutgoing { get; }

    internal bool LostWhileOpen { get; private set; }

    /// <summary>What the owner reports when a loss is noticed.</summary>
    internal DisconnectReason LostReason { get; private set; } = DisconnectReason.ConnectionLost;

    public void Send(ReadOnlySpan<byte> message)
    {
        if (State != EndpointState.Open || Peer is null)
        {
            return;
        }

        if (Owner.Network.RollConnectionLoss(Link))
        {
            Owner.Network.Sever(this);
            return;
        }

        Peer.Deliver(LoopbackPacketKind.Data, message.ToArray(), TimeSpan.Zero);
    }

    public void Disconnect() => Owner.CloseLocally(this);

    internal void Deliver(LoopbackPacketKind kind, byte[]? data, TimeSpan stamp)
    {
        if (State is EndpointState.Closed or EndpointState.Lost)
        {
            return;
        }

        TimeSpan at = Owner.Network.Clock.Now + Link.Latency + Owner.Network.NextJitter(Link.Jitter);
        if (at < lastArrival)
        {
            at = lastArrival;
        }

        lastArrival = at;
        inbound.Enqueue(new LoopbackPacket(at, kind, data, stamp));
    }

    internal void MarkLost(TimeSpan at, DisconnectReason reason)
    {
        if (State is not (EndpointState.Pending or EndpointState.Open))
        {
            return;
        }

        LostReason = reason;
        LostWhileOpen = State == EndpointState.Open;
        State = EndpointState.Lost;
        inbound.Clear();
        inbound.Enqueue(new LoopbackPacket(at, LoopbackPacketKind.Lost, null, TimeSpan.Zero));
    }

    internal bool TryTakeDue(TimeSpan now, out LoopbackPacket packet)
    {
        if (inbound.Count > 0 && inbound.Peek().DeliverAt <= now)
        {
            packet = inbound.Dequeue();
            return true;
        }

        packet = default;
        return false;
    }

    internal void PingIfDue(TimeSpan now)
    {
        if (State != EndpointState.Open || Peer is null || now - lastPing < Link.PingInterval)
        {
            return;
        }

        lastPing = now;
        Peer.Deliver(LoopbackPacketKind.Ping, null, now);
    }

    internal void AddRttSample(TimeSpan sample) => rtt.AddSample(sample);
}
