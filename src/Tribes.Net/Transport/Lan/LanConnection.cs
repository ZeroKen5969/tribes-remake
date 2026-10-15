using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Tribes.Net.Transport.Lan;

/// <summary>Kinds of TCP frames.</summary>
internal enum FrameKind : byte
{
    Handshake = 1,
    HandshakeAck = 2,
    Data = 3,
    Ping = 4,
    Pong = 5,
    Close = 6,
}

/// <summary>
/// A frame is a 4-byte little-endian length, then that many bytes: one kind byte and the payload.
/// The client's first frame is a handshake with the magic <c>TRBN</c> and the protocol version.
/// </summary>
internal static class LanFrames
{
    public const int HeaderBytes = 4;

    public static ReadOnlySpan<byte> Magic => "TRBN"u8;

    public static byte[] Encode(FrameKind kind, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderBytes + 1 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 1 + payload.Length);
        frame[HeaderBytes] = (byte)kind;
        payload.CopyTo(frame.AsSpan(HeaderBytes + 1));
        return frame;
    }

    public static byte[] Handshake()
    {
        Span<byte> payload = stackalloc byte[6];
        Magic.CopyTo(payload);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[4..], NetProtocol.Version);
        return Encode(FrameKind.Handshake, payload);
    }

    /// <summary>
    /// Whether the payload is a handshake. The frame format never changes, so any protocol version
    /// is let through: the session compares versions and tells the player why they cannot join.
    /// </summary>
    public static bool IsHandshake(ReadOnlySpan<byte> payload) =>
        payload.Length == 6 && payload[..4].SequenceEqual(Magic);

    public static byte[] Stamp(FrameKind kind, long stamp)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, stamp);
        return Encode(kind, payload);
    }
}

/// <summary>
/// One TCP connection. A receive loop, a send loop (fed by a queue, so <see cref="Send"/> never
/// blocks the game thread) and a keep-alive loop run in the background; they report to the
/// owning transport, which raises the events on the polling thread.
/// </summary>
internal sealed class LanConnection : IConnection, IDisposable
{
    private const int StateConnecting = 0;
    private const int StateOpen = 1;
    private const int StateClosed = 2;
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2);

    private readonly LanTransport owner;
    private readonly LanTransportOptions options;
    private readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly RttEstimator rtt = new();
    private readonly long createdAt = Stopwatch.GetTimestamp();
    private readonly object stateGate = new();
    private Socket? socket;
    private NetworkStream? stream;
    private Task sendLoop = Task.CompletedTask;
    private long lastReceivedAt;
    private int state;
    private int closed;
    private int socketReleased;
    private string remoteAddress;

    private LanConnection(LanTransport owner, LanTransportOptions options, int id, bool hostSide, string remoteAddress)
    {
        this.owner = owner;
        this.options = options;
        Id = id;
        IsHostSide = hostSide;
        this.remoteAddress = remoteAddress;
        lastReceivedAt = createdAt;
    }

    public int Id { get; }

    public string RemoteAddress => Volatile.Read(ref remoteAddress);

    public bool IsConnected => Volatile.Read(ref state) == StateOpen;

    public TimeSpan RoundTripTime => rtt.Smoothed;

    public TimeSpan RoundTripVariance => rtt.Variance;

    internal bool IsHostSide { get; }

    /// <summary>Takes over a socket the listener accepted; waits for the client's handshake.</summary>
    public static LanConnection Accept(LanTransport owner, LanTransportOptions options, int id, Socket accepted)
    {
        var connection = new LanConnection(owner, options, id, hostSide: true, LanAddress.Format(accepted.RemoteEndPoint, "?"));
        owner.Track(connection);
        connection.Attach(accepted);
        connection.StartLoops();
        return connection;
    }

    /// <summary>Starts connecting to a host.</summary>
    public static LanConnection Dial(LanTransport owner, LanTransportOptions options, int id, string address)
    {
        var connection = new LanConnection(owner, options, id, hostSide: false, address);
        owner.Track(connection);
        _ = connection.DialAsync(address);
        return connection;
    }

    public void Send(ReadOnlySpan<byte> message)
    {
        if (message.Length > options.MaxMessageBytes)
        {
            throw new ArgumentException($"A message may be at most {options.MaxMessageBytes} bytes.", nameof(message));
        }

        if (IsConnected)
        {
            outgoing.Writer.TryWrite(LanFrames.Encode(FrameKind.Data, message));
        }
    }

    public void Disconnect() => Close(DisconnectReason.Local, graceful: true);

    public void Dispose()
    {
        Close(DisconnectReason.Local, graceful: false);
        lifetime.Dispose();
    }

    private void Attach(Socket connected)
    {
        connected.NoDelay = true;
        stream = new NetworkStream(connected, ownsSocket: true);
        Volatile.Write(ref socket, connected);
    }

    private void StartLoops()
    {
        sendLoop = SendLoopAsync();
        _ = ReceiveLoopAsync();
        _ = KeepAliveLoopAsync();
    }

    private async Task DialAsync(string address)
    {
        if (!LanAddress.TryParse(address, options.DefaultConnectPort, out string host, out int port))
        {
            Close(DisconnectReason.ConnectFailed, graceful: false);
            return;
        }

        var dialing = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(options.ConnectTimeout);
            IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? ip)
                ? [ip]
                : await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
            await dialing.ConnectAsync(addresses, port, timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            DialFailed(dialing, ex);
            return;
        }
        catch (OperationCanceledException ex)
        {
            DialFailed(dialing, ex);
            return;
        }
        catch (ObjectDisposedException ex)
        {
            DialFailed(dialing, ex);
            return;
        }
        catch (ArgumentException ex)
        {
            // A host name the resolver rejects (for example one that is too long).
            DialFailed(dialing, ex);
            return;
        }

        Attach(dialing);
        Volatile.Write(ref remoteAddress, LanAddress.Format(dialing.RemoteEndPoint, address));
        outgoing.Writer.TryWrite(LanFrames.Handshake());
        StartLoops();
        if (Volatile.Read(ref closed) == 1)
        {
            ReleaseSocket();
        }
    }

    private void DialFailed(Socket dialing, Exception error)
    {
        dialing.Dispose();
        owner.RecordError(error);
        Close(DisconnectReason.ConnectFailed, graceful: false);
    }

    private async Task ReceiveLoopAsync()
    {
        CancellationToken token = lifetime.Token;
        var header = new byte[LanFrames.HeaderBytes];
        DisconnectReason reason = DisconnectReason.ConnectionLost;
        try
        {
            while (true)
            {
                await stream!.ReadExactlyAsync(header, token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length < 1 || length > options.MaxMessageBytes + 1)
                {
                    reason = DisconnectReason.ProtocolError;
                    break;
                }

                var frame = new byte[length];
                await stream.ReadExactlyAsync(frame, token).ConfigureAwait(false);
                Interlocked.Exchange(ref lastReceivedAt, Stopwatch.GetTimestamp());
                DisconnectReason? end = HandleFrame((FrameKind)frame[0], frame.AsMemory(1));
                if (end is { } endReason)
                {
                    reason = endReason;
                    break;
                }
            }
        }
        catch (EndOfStreamException)
        {
            reason = DisconnectReason.ConnectionLost;
        }
        catch (IOException ex)
        {
            owner.RecordError(ex);
        }
        catch (SocketException ex)
        {
            owner.RecordError(ex);
        }
        catch (OperationCanceledException)
        {
            // Closed locally.
        }
        catch (ObjectDisposedException)
        {
            // Closed locally.
        }

        Close(reason, graceful: false);
    }

    /// <summary>Handles one frame; returns a reason when the connection must end.</summary>
    private DisconnectReason? HandleFrame(FrameKind kind, ReadOnlyMemory<byte> payload)
    {
        int current = Volatile.Read(ref state);
        if (current == StateClosed)
        {
            return null;
        }

        switch (kind)
        {
            case FrameKind.Handshake when IsHostSide && current == StateConnecting:
                if (!LanFrames.IsHandshake(payload.Span))
                {
                    return DisconnectReason.ProtocolError;
                }

                outgoing.Writer.TryWrite(LanFrames.Encode(FrameKind.HandshakeAck, ReadOnlySpan<byte>.Empty));
                Open();
                return null;
            case FrameKind.HandshakeAck when !IsHostSide && current == StateConnecting:
                rtt.AddSample(Stopwatch.GetElapsedTime(createdAt));
                Open();
                return null;
            case FrameKind.Data when current == StateOpen:
                owner.QueueMessage(this, payload);
                return null;
            case FrameKind.Ping when payload.Length == 8 && current == StateOpen:
                outgoing.Writer.TryWrite(LanFrames.Encode(FrameKind.Pong, payload.Span));
                return null;
            case FrameKind.Pong when payload.Length == 8 && current == StateOpen:
                rtt.AddSample(Stopwatch.GetElapsedTime(BinaryPrimitives.ReadInt64LittleEndian(payload.Span)));
                return null;
            case FrameKind.Close:
                return DisconnectReason.Remote;
            default:
                return DisconnectReason.ProtocolError;
        }
    }

    /// <summary>
    /// Opens the connection. Under the same lock as <see cref="Close"/>, so the owner always
    /// learns of the opening before the end, or of the end only.
    /// </summary>
    private void Open()
    {
        lock (stateGate)
        {
            if (state != StateConnecting)
            {
                return;
            }

            Volatile.Write(ref state, StateOpen);
            owner.QueueConnected(this);
        }
    }

    private async Task SendLoopAsync()
    {
        CancellationToken token = lifetime.Token;
        try
        {
            while (await outgoing.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (outgoing.Reader.TryRead(out byte[]? frame))
                {
                    await stream!.WriteAsync(frame, token).ConfigureAwait(false);
                }
            }

            socket?.Shutdown(SocketShutdown.Send);
        }
        catch (IOException ex)
        {
            owner.RecordError(ex);
            Close(DisconnectReason.ConnectionLost, graceful: false);
        }
        catch (SocketException ex)
        {
            owner.RecordError(ex);
            Close(DisconnectReason.ConnectionLost, graceful: false);
        }
        catch (OperationCanceledException)
        {
            // Closed locally.
        }
        catch (ObjectDisposedException)
        {
            // Closed locally.
        }
    }

    private async Task KeepAliveLoopAsync()
    {
        CancellationToken token = lifetime.Token;
        try
        {
            using var timer = new PeriodicTimer(options.PingInterval);
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (!KeepAlive())
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closed locally.
        }
        catch (ObjectDisposedException)
        {
            // Closed locally.
        }
    }

    /// <summary>One keep-alive round; false when the connection is over.</summary>
    private bool KeepAlive()
    {
        int current = Volatile.Read(ref state);
        if (current == StateClosed)
        {
            return false;
        }

        if (current == StateConnecting)
        {
            if (Stopwatch.GetElapsedTime(createdAt) <= options.ConnectTimeout)
            {
                return true;
            }

            Close(IsHostSide ? DisconnectReason.Timeout : DisconnectReason.ConnectFailed, graceful: false);
            return false;
        }

        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastReceivedAt)) > options.Timeout)
        {
            Close(DisconnectReason.Timeout, graceful: false);
            return false;
        }

        outgoing.Writer.TryWrite(LanFrames.Stamp(FrameKind.Ping, Stopwatch.GetTimestamp()));
        return true;
    }

    /// <summary>Ends the connection once; the owner learns why.</summary>
    internal void Close(DisconnectReason reason, bool graceful)
    {
        int previous;
        lock (stateGate)
        {
            if (closed == 1)
            {
                return;
            }

            Volatile.Write(ref closed, 1);
            previous = state;
            Volatile.Write(ref state, StateClosed);
        }

        if (graceful && previous == StateOpen)
        {
            outgoing.Writer.TryWrite(LanFrames.Encode(FrameKind.Close, ReadOnlySpan<byte>.Empty));
            outgoing.Writer.TryComplete();
            _ = ReleaseAfterSendingAsync();
        }
        else
        {
            outgoing.Writer.TryComplete();
            ReleaseSocket();
        }

        owner.OnClosed(this, reason, previous == StateOpen);
    }

    private async Task ReleaseAfterSendingAsync()
    {
        await Task.WhenAny(sendLoop, Task.Delay(Linger)).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        ReleaseSocket();
    }

    private void ReleaseSocket()
    {
        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed together with the connection.
        }

        Socket? current = Volatile.Read(ref socket);
        if (current is null || Interlocked.Exchange(ref socketReleased, 1) == 1)
        {
            return;
        }

        stream?.Dispose();
        current.Dispose();
    }
}
