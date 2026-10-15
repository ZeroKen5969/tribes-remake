using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Net.Transport.Lan;

/// <summary>
/// The LAN transport: the host listens on TCP and answers UDP room queries; clients find it with
/// <see cref="LanRoomBrowser"/> or connect directly by "ip:port". No server is involved. Sockets
/// work on background threads; events wait in a queue until <see cref="Poll"/>.
/// </summary>
public sealed class LanTransport : ITransport
{
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly LanTransportOptions options;
    private readonly ConcurrentQueue<TransportEvent> events = new();
    private readonly List<IConnection> connections = [];
    private readonly object gate = new();
    private readonly HashSet<LanConnection> live = [];
    private Socket? listenSocket;
    private CancellationTokenSource? hostLifetime;
    private LanDiscoveryResponder? responder;
    private RoomAdvertisement? advertisement;
    private Exception? lastError;
    private int nextConnectionId;
    private bool disposed;

    /// <summary>Creates a transport.</summary>
    /// <param name="options">Ports and timeouts; null for the defaults.</param>
    public LanTransport(LanTransportOptions? options = null)
    {
        this.options = options ?? new LanTransportOptions();
        string? problem = this.options.FindProblem();
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(options));
        }
    }

    /// <inheritdoc />
    public TransportRole Role { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<IConnection> Connections => connections;

    /// <summary>The TCP port the host listens on, or 0 when not hosting.</summary>
    public int LocalPort { get; private set; }

    /// <inheritdoc />
    public string? HostAddress
    {
        get
        {
            if (Role != TransportRole.Host)
            {
                return null;
            }

            IPAddress address = options.BindAddress.Equals(IPAddress.Any)
                ? LanNetworkInfo.FirstLanAddress() ?? IPAddress.Loopback
                : options.BindAddress;
            return new IPEndPoint(address, LocalPort).ToString();
        }
    }

    /// <summary>Why the host cannot be discovered (for example the port is taken), or null.</summary>
    public string? DiscoveryError { get; private set; }

    /// <summary>The most recent socket error, for diagnostics; connections report their own end.</summary>
    public Exception? LastError => Volatile.Read(ref lastError);

    /// <inheritdoc />
    public event EventHandler<PeerEventArgs>? PeerConnected;

    /// <inheritdoc />
    public event EventHandler<PeerDisconnectedEventArgs>? PeerDisconnected;

    /// <inheritdoc />
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <inheritdoc />
    /// <exception cref="SocketException">The TCP port is in use.</exception>
    public void StartHost(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role != TransportRole.None)
        {
            throw new InvalidOperationException("The transport is already hosting or connected.");
        }

        var socket = new Socket(options.BindAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(options.BindAddress, options.Port));
            socket.Listen(16);
        }
        catch (SocketException)
        {
            socket.Dispose();
            throw;
        }

        listenSocket = socket;
        LocalPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
        lock (gate)
        {
            this.advertisement = advertisement;
        }

        hostLifetime = new CancellationTokenSource();
        _ = AcceptLoopAsync(socket, hostLifetime.Token);
        StartDiscovery();
        Role = TransportRole.Host;
    }

    /// <inheritdoc />
    public void UpdateAdvertisement(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        if (Role != TransportRole.Host)
        {
            throw new InvalidOperationException("Only a hosting transport has an advertisement.");
        }

        lock (gate)
        {
            this.advertisement = advertisement;
        }
    }

    /// <inheritdoc />
    /// <param name="address">"ip:port", "ip", "hostname:port" or "[ipv6]:port".</param>
    public void Connect(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role == TransportRole.Host)
        {
            throw new InvalidOperationException("A hosting transport cannot connect to another host.");
        }

        lock (gate)
        {
            if (live.Any(c => !c.IsHostSide))
            {
                throw new InvalidOperationException("The transport is already connected or connecting.");
            }
        }

        Role = TransportRole.Client;
        LanConnection.Dial(this, options, Interlocked.Increment(ref nextConnectionId), address);
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
        while (events.TryDequeue(out TransportEvent e))
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
                    if (connections.Contains(e.Connection))
                    {
                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(e.Connection, e.Data));
                    }

                    break;
            }
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        hostLifetime?.Cancel();
        hostLifetime?.Dispose();
        hostLifetime = null;
        listenSocket?.Dispose();
        listenSocket = null;
        responder?.Dispose();
        responder = null;
        LanConnection[] all;
        lock (gate)
        {
            all = [.. live];
            advertisement = null;
        }

        foreach (LanConnection connection in all)
        {
            connection.Disconnect();
        }

        LocalPort = 0;
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

    /// <summary>
    /// False: the clients are connected only to the host, so nobody can tell a host that left from
    /// a broken link of its own, and the match cannot move to another peer. A LAN host that leaves
    /// ends the match; it can save it first and resume it later.
    /// </summary>
    public bool SupportsHostMigration => false;

    /// <inheritdoc />
    public string? MigrationAddress => null;

    /// <inheritdoc />
    public string? RoomHostAddress => null;

    /// <inheritdoc />
    public bool IsPeerPresent(string migrationAddress) => false;

    /// <inheritdoc />
    public string? TakeOverHost(RoomAdvertisement advertisement) =>
        throw new NotSupportedException("A LAN match cannot move to another host.");

    internal void Track(LanConnection connection)
    {
        lock (gate)
        {
            live.Add(connection);
        }
    }

    internal void QueueConnected(LanConnection connection) =>
        events.Enqueue(new TransportEvent(TransportEventKind.Connected, connection, DisconnectReason.None, default));

    internal void QueueMessage(LanConnection connection, ReadOnlyMemory<byte> data) =>
        events.Enqueue(new TransportEvent(TransportEventKind.Message, connection, DisconnectReason.None, data));

    internal void OnClosed(LanConnection connection, DisconnectReason reason, bool wasOpen)
    {
        lock (gate)
        {
            live.Remove(connection);
        }

        if (wasOpen)
        {
            events.Enqueue(new TransportEvent(TransportEventKind.Disconnected, connection, reason, default));
        }
        else if (!connection.IsHostSide)
        {
            DisconnectReason failure = reason == DisconnectReason.Local ? DisconnectReason.Local : DisconnectReason.ConnectFailed;
            events.Enqueue(new TransportEvent(TransportEventKind.Disconnected, connection, failure, default));
        }
    }

    internal void RecordError(Exception error) => Volatile.Write(ref lastError, error);

    private void StartDiscovery()
    {
        DiscoveryError = null;
        if (!options.EnableDiscovery)
        {
            return;
        }

        try
        {
            responder = new LanDiscoveryResponder(options.DiscoveryBindAddress, options.DiscoveryPort, CurrentReply, RecordError);
        }
        catch (SocketException ex)
        {
            DiscoveryError = $"LAN discovery is unavailable on UDP port {options.DiscoveryPort}: {ex.Message}";
            RecordError(ex);
        }
    }

    private (RoomAdvertisement? Room, int Port) CurrentReply()
    {
        lock (gate)
        {
            return (advertisement, LocalPort);
        }
    }

    private async Task AcceptLoopAsync(Socket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Socket? accepted = null;
            try
            {
                accepted = await socket.AcceptAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                // A client that gave up before being accepted; keep listening, but do not spin
                // if the listening socket itself has failed.
                RecordError(ex);
            }

            if (accepted is null)
            {
                await Task.Delay(AcceptRetryDelay, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            AcceptOne(accepted);
        }
    }

    /// <summary>Takes over one accepted socket; a socket that fails while being set up is dropped, the loop goes on.</summary>
    private void AcceptOne(Socket accepted)
    {
        try
        {
            LanConnection.Accept(this, options, Interlocked.Increment(ref nextConnectionId), accepted);
        }
        catch (SocketException ex)
        {
            RecordError(ex);
            accepted.Dispose();
        }
        catch (IOException ex)
        {
            RecordError(ex);
            accepted.Dispose();
        }
        catch (ObjectDisposedException ex)
        {
            RecordError(ex);
        }
    }
}
