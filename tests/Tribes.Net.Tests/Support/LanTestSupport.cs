using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Tests.Support;

/// <summary>
/// A fact that runs only when UDP broadcasts sent on this machine reach its own sockets; some
/// systems (no active IPv4 network, strict firewall) block them. The loopback discovery test
/// covers the same protocol without broadcasts.
/// </summary>
public sealed class BroadcastFactAttribute : FactAttribute
{
    public BroadcastFactAttribute()
    {
        if (BroadcastProbe.Problem is { } problem)
        {
            Skip = problem;
        }
    }
}

internal static class BroadcastProbe
{
    private static readonly Lazy<string?> Result = new(Run);

    public static string? Problem => Result.Value;

    private static string? Run()
    {
        try
        {
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.Bind(new IPEndPoint(IPAddress.Any, 0));
            receiver.ReceiveTimeout = 1500;
            int port = ((IPEndPoint)receiver.LocalEndPoint!).Port;
            using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
            sender.SendTo([0x42], new IPEndPoint(IPAddress.Broadcast, port));
            var buffer = new byte[8];
            receiver.Receive(buffer);
            return null;
        }
        catch (SocketException ex)
        {
            return $"Skipped: UDP broadcast does not reach this machine's own sockets ({ex.SocketErrorCode}). LanTransportTests.Discovery_FindsTheRoom_OverLoopback covers the same protocol.";
        }
    }
}

internal static class LanTestSupport
{
    public static int FreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Polls the transports until the condition holds, failing the test after the timeout.</summary>
    public static void PollUntil(Func<bool> condition, string waitingFor, double seconds, params ITransport[] transports) =>
        PollUntil(condition, () => waitingFor, seconds, transports);

    /// <summary>As above; <paramref name="waitingFor"/> is asked only when the wait fails, so it can report the state then.</summary>
    public static void PollUntil(Func<bool> condition, Func<string> waitingFor, double seconds, params ITransport[] transports)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            foreach (ITransport transport in transports)
            {
                transport.Poll();
            }

            if (condition())
            {
                return;
            }

            if (watch.Elapsed.TotalSeconds > seconds)
            {
                Assert.Fail($"Timed out after {seconds} s waiting for {waitingFor()}.");
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>Runs <paramref name="update"/> every few milliseconds until the condition holds.</summary>
    public static void RunUntil(Action update, Func<bool> condition, string waitingFor, double seconds)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            update();
            if (condition())
            {
                return;
            }

            if (watch.Elapsed.TotalSeconds > seconds)
            {
                Assert.Fail($"Timed out after {seconds} s waiting for {waitingFor}.");
            }

            Thread.Sleep(5);
        }
    }
}
