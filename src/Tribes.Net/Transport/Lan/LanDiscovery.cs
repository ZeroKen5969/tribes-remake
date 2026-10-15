using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Net.Protocol;

namespace Tribes.Net.Transport.Lan;

internal sealed record DiscoveryReply(int ProtocolVersion, long Nonce, int TcpPort, RoomAdvertisement Room);

/// <summary>
/// UDP datagrams of room discovery. A query is <c>TRBQ</c>, the protocol version (u16) and a
/// nonce (i64). A reply is <c>TRBR</c>, the protocol version, the query's nonce, the host's TCP
/// port (u16) and the room advertisement (<see cref="AdvertisementCodec"/>). This layout stays the
/// same in later protocol versions, so browsers can show rooms they cannot join.
/// </summary>
internal static class DiscoveryCodec
{
    public const int MaxDatagramBytes = 1400;
    private const int QueryBytes = 14;

    private static ReadOnlySpan<byte> QueryMagic => "TRBQ"u8;

    private static ReadOnlySpan<byte> ReplyMagic => "TRBR"u8;

    public static byte[] EncodeQuery(long nonce)
    {
        var writer = new PacketWriter(QueryBytes);
        writer.WriteRaw(QueryMagic);
        writer.WriteUInt16(NetProtocol.Version);
        writer.WriteInt64(nonce);
        return writer.ToArray();
    }

    public static bool TryDecodeQuery(ReadOnlySpan<byte> data, out long nonce)
    {
        nonce = 0;
        if (data.Length != QueryBytes || !data[..4].SequenceEqual(QueryMagic))
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadInt64LittleEndian(data[6..]);
        return true;
    }

    public static byte[] EncodeReply(long nonce, int tcpPort, RoomAdvertisement room)
    {
        var writer = new PacketWriter(256);
        writer.WriteRaw(ReplyMagic);
        writer.WriteUInt16(NetProtocol.Version);
        writer.WriteInt64(nonce);
        writer.WriteUInt16((ushort)tcpPort);
        AdvertisementCodec.Write(writer, room);
        return writer.ToArray();
    }

    public static DiscoveryReply? TryDecodeReply(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 4 || !data.Span[..4].SequenceEqual(ReplyMagic))
        {
            return null;
        }

        try
        {
            var reader = new PacketReader(data[4..]);
            int version = reader.ReadUInt16();
            long nonce = reader.ReadInt64();
            int port = reader.ReadUInt16();
            RoomAdvertisement room = AdvertisementCodec.Read(reader);
            return port == 0 ? null : new DiscoveryReply(version, nonce, port, room);
        }
        catch (ProtocolException)
        {
            // Not ours, or damaged: the datagram is ignored.
            return null;
        }
    }
}

/// <summary>Local network addresses.</summary>
internal static class LanNetworkInfo
{
    /// <summary>The directed broadcast address of every IPv4 network this device is on.</summary>
    public static IReadOnlyList<IPAddress> BroadcastAddresses()
    {
        var result = new List<IPAddress>();
        foreach (UnicastIPAddressInformation unicast in LanUnicastAddresses())
        {
            byte[] address = unicast.Address.GetAddressBytes();
            byte[] mask = unicast.IPv4Mask.GetAddressBytes();
            if (mask.Length != 4 || mask.All(b => b == 0))
            {
                continue;
            }

            for (int i = 0; i < 4; i++)
            {
                address[i] |= (byte)~mask[i];
            }

            result.Add(new IPAddress(address));
        }

        return result;
    }

    /// <summary>
    /// An IPv4 address other devices on the LAN can reach this one at, or null. Interfaces with a
    /// default gateway (the real LAN) come before virtual ones such as VM switches.
    /// </summary>
    public static IPAddress? FirstLanAddress() => LanUnicastAddresses().Select(u => u.Address).FirstOrDefault();

    /// <summary>IPv4 addresses of the interfaces that are up, those with a gateway first.</summary>
    private static List<UnicastIPAddressInformation> LanUnicastAddresses()
    {
        var withGateway = new List<UnicastIPAddressInformation>();
        var result = new List<UnicastIPAddressInformation>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                IPInterfaceProperties properties = nic.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                (hasGateway ? withGateway : result).AddRange(properties.UnicastAddresses.Where(
                    u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address)));
            }

            result.InsertRange(0, withGateway);
        }
        catch (NetworkInformationException)
        {
            // Interface details are unavailable; directed broadcasts are skipped.
        }
        catch (PlatformNotSupportedException)
        {
            // Some mobile platforms do not expose interface details.
        }

        return result;
    }
}

/// <summary>The host's side of discovery: answers every room query with the current advertisement.</summary>
internal sealed class LanDiscoveryResponder : IDisposable
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMilliseconds(100);
    private readonly Socket socket;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<(RoomAdvertisement? Room, int Port)> source;
    private readonly Action<Exception> onError;

    public LanDiscoveryResponder(IPAddress bindAddress, int port, Func<(RoomAdvertisement? Room, int Port)> source, Action<Exception> onError)
    {
        this.source = source;
        this.onError = onError;
        socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // Several hosts on one device share the port; broadcast queries reach all of them.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(bindAddress, port));
        }
        catch (SocketException)
        {
            socket.Dispose();
            lifetime.Dispose();
            throw;
        }

        _ = RunAsync(lifetime.Token);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        socket.Dispose();
        lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var buffer = new byte[DiscoveryCodec.MaxDatagramBytes];
        var anyone = new IPEndPoint(IPAddress.Any, 0);
        while (!token.IsCancellationRequested)
        {
            bool failed = false;
            try
            {
                SocketReceiveFromResult received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, anyone, token).ConfigureAwait(false);
                if (DiscoveryCodec.TryDecodeQuery(buffer.AsSpan(0, received.ReceivedBytes), out long nonce)
                    && source() is ({ } room, int port))
                {
                    byte[] reply = DiscoveryCodec.EncodeReply(nonce, port, room);
                    await socket.SendToAsync(reply, SocketFlags.None, received.RemoteEndPoint, token).ConfigureAwait(false);
                }
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
                // Windows reports an unreachable earlier reply on the next receive; keep answering.
                onError(ex);
                failed = true;
            }

            if (failed)
            {
                await Task.Delay(ErrorBackoff, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Finds LAN rooms: broadcasts a query every <see cref="LanDiscoveryOptions.QueryInterval"/> and
/// lists the hosts that answer. Rooms not heard from for <see cref="LanDiscoveryOptions.RoomTimeout"/>
/// leave the list.
/// </summary>
public sealed class LanRoomBrowser : IRoomBrowser
{
    private static readonly TimeSpan MaxQueryAge = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMilliseconds(100);
    private const int MaxRooms = 256;
    private readonly LanDiscoveryOptions options;
    private readonly ConcurrentDictionary<Guid, Sighting> sightings = new();
    private Socket? socket;
    private CancellationTokenSource? lifetime;
    private IReadOnlyList<RoomInfo> rooms = [];
    private Exception? lastError;
    private int changed;

    /// <summary>Creates a browser.</summary>
    /// <param name="options">Port, targets and timing; null for the defaults.</param>
    public LanRoomBrowser(LanDiscoveryOptions? options = null)
    {
        this.options = options ?? new LanDiscoveryOptions();
    }

    /// <inheritdoc />
    public IReadOnlyList<RoomInfo> Rooms => rooms;

    /// <summary>Whether the browser is looking.</summary>
    public bool IsRunning => socket is not null;

    /// <summary>The most recent socket error, for diagnostics.</summary>
    public Exception? LastError => Volatile.Read(ref lastError);

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <inheritdoc />
    /// <exception cref="SocketException">The socket could not be opened.</exception>
    public void Start()
    {
        if (socket is not null)
        {
            return;
        }

        var created = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
        try
        {
            created.Bind(new IPEndPoint(options.BindAddress, 0));
        }
        catch (SocketException)
        {
            created.Dispose();
            throw;
        }

        socket = created;
        lifetime = new CancellationTokenSource();
        _ = ReceiveLoopAsync(created, lifetime.Token);
        _ = QueryLoopAsync(created, lifetime.Token);
    }

    /// <inheritdoc />
    public void Refresh()
    {
        if (socket is { } current)
        {
            SendQueries(current);
        }
    }

    /// <inheritdoc />
    public void Poll()
    {
        long now = Stopwatch.GetTimestamp();
        foreach ((Guid id, Sighting sighting) in sightings)
        {
            if (Stopwatch.GetElapsedTime(sighting.SeenAt, now) > options.RoomTimeout && sightings.TryRemove(KeyValuePair.Create(id, sighting)))
            {
                Interlocked.Exchange(ref changed, 1);
            }
        }

        if (Interlocked.Exchange(ref changed, 0) == 1)
        {
            rooms = [.. sightings.Values
                .Select(s => s.Info)
                .OrderBy(r => r.Room.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Address, StringComparer.Ordinal)];
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        lifetime?.Cancel();
        lifetime?.Dispose();
        lifetime = null;
        socket?.Dispose();
        socket = null;
        sightings.Clear();
        Interlocked.Exchange(ref changed, 0);
        if (rooms.Count > 0)
        {
            rooms = [];
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Shutdown();

    private async Task QueryLoopAsync(Socket current, CancellationToken token)
    {
        try
        {
            SendQueries(current);
            using var timer = new PeriodicTimer(options.QueryInterval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                SendQueries(current);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }

    private void SendQueries(Socket current)
    {
        byte[] query = DiscoveryCodec.EncodeQuery(Stopwatch.GetTimestamp());
        foreach (IPAddress target in QueryTargets())
        {
            try
            {
                current.SendTo(query, new IPEndPoint(target, options.Port));
            }
            catch (SocketException ex)
            {
                // One unreachable network must not stop the queries to the others.
                Volatile.Write(ref lastError, ex);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private IEnumerable<IPAddress> QueryTargets()
    {
        var targets = new List<IPAddress>();
        if (options.UseBroadcast)
        {
            targets.Add(IPAddress.Broadcast);
            targets.AddRange(LanNetworkInfo.BroadcastAddresses());
        }

        targets.AddRange(options.ExtraTargets);
        return targets.Distinct();
    }

    private async Task ReceiveLoopAsync(Socket current, CancellationToken token)
    {
        var buffer = new byte[DiscoveryCodec.MaxDatagramBytes];
        var anyone = new IPEndPoint(IPAddress.Any, 0);
        while (!token.IsCancellationRequested)
        {
            bool failed = false;
            try
            {
                SocketReceiveFromResult received = await current.ReceiveFromAsync(buffer, SocketFlags.None, anyone, token).ConfigureAwait(false);
                DiscoveryReply? reply = DiscoveryCodec.TryDecodeReply(buffer.AsSpan(0, received.ReceivedBytes).ToArray());
                if (reply is not null && received.RemoteEndPoint is IPEndPoint from)
                {
                    Record(reply, from);
                }
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
                // Windows reports an unreachable earlier query on the next receive; keep listening.
                Volatile.Write(ref lastError, ex);
                failed = true;
            }

            if (failed)
            {
                await Task.Delay(ErrorBackoff, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void Record(DiscoveryReply reply, IPEndPoint from)
    {
        TimeSpan age = Stopwatch.GetElapsedTime(reply.Nonce);
        TimeSpan ping = age > TimeSpan.Zero && age < MaxQueryAge ? age : TimeSpan.Zero;
        string address = LanAddress.Format(new IPEndPoint(from.Address, reply.TcpPort), from.ToString());
        if (sightings.Count >= MaxRooms && !sightings.ContainsKey(reply.Room.RoomId))
        {
            // A flood of made-up rooms must not grow the list without end.
            return;
        }

        sightings.AddOrUpdate(
            reply.Room.RoomId,
            _ =>
            {
                Interlocked.Exchange(ref changed, 1);
                return new Sighting(Info(address, reply, ping), Stopwatch.GetTimestamp());
            },
            (_, previous) =>
            {
                // Keep the first address a room answered from, so the list does not flip between
                // the loopback and the LAN address of a host on this device.
                RoomInfo info = Info(previous.Info.Address, reply, ping);
                if (!info.Room.Equals(previous.Info.Room) || info.ProtocolVersion != previous.Info.ProtocolVersion)
                {
                    Interlocked.Exchange(ref changed, 1);
                }

                return new Sighting(info, Stopwatch.GetTimestamp());
            });
    }

    private static RoomInfo Info(string address, DiscoveryReply reply, TimeSpan ping) => new()
    {
        Address = address,
        Room = reply.Room,
        ProtocolVersion = reply.ProtocolVersion,
        Ping = ping,
    };

    private sealed record Sighting(RoomInfo Info, long SeenAt);
}
