using System;
using System.Collections.Generic;

namespace Tribes.Net.Transport;

/// <summary>What a transport is doing.</summary>
public enum TransportRole
{
    /// <summary>Neither hosting nor connected.</summary>
    None = 0,

    /// <summary>Accepting connections from clients.</summary>
    Host = 1,

    /// <summary>Connected (or connecting) to one host.</summary>
    Client = 2,
}

/// <summary>Why a connection ended.</summary>
public enum DisconnectReason
{
    /// <summary>Not disconnected.</summary>
    None = 0,

    /// <summary>This side closed it.</summary>
    Local = 1,

    /// <summary>The other side closed it.</summary>
    Remote = 2,

    /// <summary>The network failed.</summary>
    ConnectionLost = 3,

    /// <summary>Nothing arrived for too long.</summary>
    Timeout = 4,

    /// <summary>The connection could not be made.</summary>
    ConnectFailed = 5,

    /// <summary>The other side sent bytes that do not follow the transport format.</summary>
    ProtocolError = 6,

    /// <summary>
    /// The host left the room (or its device dropped out) while this peer stays in it with the
    /// others. Only transports with <see cref="ITransport.SupportsHostMigration"/> report it; the
    /// session then moves the host role to another peer.
    /// </summary>
    HostLeft = 7,
}

/// <summary>
/// Moves messages between a host and its clients in a star: clients talk only to the host.
/// Messages are byte arrays, delivered reliably and in order on each connection. All events are
/// raised from <see cref="Poll"/> on the caller's thread, so the session code needs no locks.
/// </summary>
/// <remarks>
/// Implementations: <see cref="Loopback.LoopbackTransport"/> (in-process, for tests) and
/// <see cref="Lan.LanTransport"/> (TCP with UDP discovery). An internet transport keeps the same
/// contract: hosting creates a room, an address names a room, the host's connections are the
/// other members of the room.
/// </remarks>
public interface ITransport : IDisposable
{
    /// <summary>Hosting, client or neither.</summary>
    TransportRole Role { get; }

    /// <summary>Open connections: one per client on the host, the host connection on a client.</summary>
    IReadOnlyList<IConnection> Connections { get; }

    /// <summary>
    /// While hosting, the address clients pass to <see cref="Connect"/> (for LAN, "ip:port");
    /// otherwise null.
    /// </summary>
    string? HostAddress { get; }

    /// <summary>A connection opened: a client joined the host, or this client reached its host.</summary>
    event EventHandler<PeerEventArgs>? PeerConnected;

    /// <summary>
    /// A connection ended, or a connection attempt failed (reason
    /// <see cref="DisconnectReason.ConnectFailed"/>). Raised once per connection.
    /// </summary>
    event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;

    /// <summary>A message arrived on an open connection.</summary>
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>Starts accepting clients and announcing the room.</summary>
    /// <param name="advertisement">What room lists show about the room.</param>
    void StartHost(RoomAdvertisement advertisement);

    /// <summary>Replaces what room lists show; the host calls it after every room change.</summary>
    /// <param name="advertisement">The new description.</param>
    void UpdateAdvertisement(RoomAdvertisement advertisement);

    /// <summary>
    /// Starts connecting to a host. The outcome arrives later from <see cref="Poll"/> as
    /// <see cref="PeerConnected"/> or <see cref="PeerDisconnected"/>. A client transport may
    /// connect again after its connection ended.
    /// </summary>
    /// <param name="address">A <see cref="RoomInfo.Address"/> or an address the user typed.</param>
    void Connect(string address);

    /// <summary>Sends the same message to several connections.</summary>
    /// <param name="recipients">Open connections of this transport.</param>
    /// <param name="message">The message; copied before the call returns.</param>
    void SendToMany(IReadOnlyList<IConnection> recipients, ReadOnlySpan<byte> message);

    /// <summary>Raises the events that arrived since the last call. Call it every frame.</summary>
    void Poll();

    /// <summary>
    /// Closes every connection (after their queued messages) and stops hosting; the transport
    /// can host or connect again afterwards.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Whether the host role can move to another peer when the host leaves. Such a transport
    /// keeps the peers together in a room that outlives the host (a relay such as Photon), tells
    /// the clients reliably that the host is gone (<see cref="DisconnectReason.HostLeft"/>), and
    /// lets one of them take over with <see cref="TakeOverHost"/>.
    /// </summary>
    bool SupportsHostMigration { get; }

    /// <summary>
    /// While this peer is in a room as a client: the address the others pass to
    /// <see cref="Connect"/> if it takes over hosting. Null without host migration.
    /// </summary>
    string? MigrationAddress { get; }

    /// <summary>
    /// The address of the peer that hosts the room now, as the room records it: after a host
    /// left, the peer that won <see cref="TakeOverHost"/>. Null while nobody hosts it, or when the
    /// transport cannot tell. Followers use it to find the new host even when their own idea of
    /// who is next in line is out of date.
    /// </summary>
    string? RoomHostAddress { get; }

    /// <summary>Whether the peer with this <see cref="MigrationAddress"/> is still in the room.</summary>
    /// <param name="migrationAddress">An address a peer reported as its <see cref="MigrationAddress"/>.</param>
    bool IsPeerPresent(string migrationAddress);

    /// <summary>
    /// After the host left, makes this client the room's host: from now on the others connect to
    /// it. Only one peer can win: if another peer already took over, nothing changes and its
    /// address is returned.
    /// </summary>
    /// <param name="advertisement">What room lists show from now on.</param>
    /// <returns>Null when this peer is the host now; otherwise the address of the peer that is.</returns>
    /// <exception cref="NotSupportedException">The transport does not support host migration.</exception>
    string? TakeOverHost(RoomAdvertisement advertisement);
}

/// <summary>One reliable, ordered message channel to another peer.</summary>
public interface IConnection
{
    /// <summary>Unique among the connections of a transport.</summary>
    int Id { get; }

    /// <summary>The other side, for display.</summary>
    string RemoteAddress { get; }

    /// <summary>Whether messages can be sent now.</summary>
    bool IsConnected { get; }

    /// <summary>Smoothed round-trip time to the other side; zero until measured.</summary>
    TimeSpan RoundTripTime { get; }

    /// <summary>How much the round-trip time varies (mean deviation).</summary>
    TimeSpan RoundTripVariance { get; }

    /// <summary>
    /// Queues a message; it arrives after all earlier ones. Sending on a closed connection does
    /// nothing: the disconnect event is already on its way.
    /// </summary>
    /// <param name="message">The message; copied before the call returns.</param>
    void Send(ReadOnlySpan<byte> message);

    /// <summary>Closes the connection after the messages already queued have been sent.</summary>
    void Disconnect();
}

/// <summary>Finds rooms to join.</summary>
public interface IRoomBrowser : IDisposable
{
    /// <summary>The rooms seen recently.</summary>
    IReadOnlyList<RoomInfo> Rooms { get; }

    /// <summary><see cref="Rooms"/> changed; raised from <see cref="Poll"/>.</summary>
    event EventHandler? RoomsChanged;

    /// <summary>Starts looking.</summary>
    void Start();

    /// <summary>Asks for rooms now instead of waiting for the next periodic query.</summary>
    void Refresh();

    /// <summary>Applies what arrived and drops rooms not seen for a while. Call it every frame.</summary>
    void Poll();

    /// <summary>Stops looking and forgets the rooms.</summary>
    void Shutdown();
}

/// <summary>A connection opened.</summary>
public sealed class PeerEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="connection">The connection.</param>
    public PeerEventArgs(IConnection connection)
    {
        Connection = connection;
    }

    /// <summary>The connection.</summary>
    public IConnection Connection { get; }
}

/// <summary>A connection ended or could not be made.</summary>
public sealed class PeerDisconnectedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="reason">Why it ended.</param>
    public PeerDisconnectedEventArgs(IConnection connection, DisconnectReason reason)
    {
        Connection = connection;
        Reason = reason;
    }

    /// <summary>The connection.</summary>
    public IConnection Connection { get; }

    /// <summary>Why it ended.</summary>
    public DisconnectReason Reason { get; }
}

/// <summary>A message arrived.</summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="connection">The connection it came on.</param>
    /// <param name="data">The message.</param>
    public MessageReceivedEventArgs(IConnection connection, ReadOnlyMemory<byte> data)
    {
        Connection = connection;
        Data = data;
    }

    /// <summary>The connection it came on.</summary>
    public IConnection Connection { get; }

    /// <summary>The message. The transport does not reuse the memory, so it may be kept.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}
