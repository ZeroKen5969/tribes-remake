using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Lan;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>
/// Real sockets on this machine. The collection runs alone, so the busy loopback tests cannot
/// starve the thread pool that delivers socket callbacks.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanCollection
{
    public const string Name = "LAN sockets";
}

[Collection(LanCollection.Name)]
public sealed class LanTransportTests
{
    private static readonly RoomAdvertisement Advertisement = new()
    {
        RoomId = Guid.NewGuid(),
        Name = "LAN room",
        HostName = "Host",
        Mode = RoomMode.Endless,
        Players = 1,
        MaxPlayers = 4,
        GameVersion = "1.0",
        ContentVersion = "c",
        IsOpen = true,
        JoinableInProgress = true,
    };

    private static LanTransportOptions HostOptions(bool discovery = false, int discoveryPort = 0, IPAddress? discoveryBind = null) => new()
    {
        BindAddress = IPAddress.Loopback,
        Port = 0,
        EnableDiscovery = discovery,
        DiscoveryPort = discoveryPort == 0 ? NetProtocol.DefaultDiscoveryPort : discoveryPort,
        DiscoveryBindAddress = discoveryBind ?? IPAddress.Loopback,
        PingInterval = TimeSpan.FromMilliseconds(50),
        Timeout = TimeSpan.FromSeconds(3),
        ConnectTimeout = TimeSpan.FromSeconds(3),
    };

    [Fact]
    public void TcpConnection_CarriesMessagesInOrder_AndMeasuresTheRoundTrip()
    {
        using var host = new LanTransport(HostOptions());
        using var client = new LanTransport(HostOptions());
        var received = new List<byte[]>();
        var events = new List<string>();
        host.MessageReceived += (_, e) => received.Add(e.Data.ToArray());
        host.PeerDisconnected += (_, e) => events.Add("host saw " + e.Reason);
        client.PeerDisconnected += (_, e) => events.Add("client saw " + e.Reason);
        host.StartHost(Advertisement);
        Assert.Equal($"127.0.0.1:{host.LocalPort}", host.HostAddress);
        client.Connect($"127.0.0.1:{host.LocalPort}");
        LanTestSupport.PollUntil(() => host.Connections.Count == 1 && client.Connections.Count == 1, "the connection", 5, host, client);

        IConnection toHost = client.Connections.Single();
        Assert.True(toHost.IsConnected);
        Assert.StartsWith("127.0.0.1:", toHost.RemoteAddress, StringComparison.Ordinal);
        byte[] large = new byte[600_000];
        new Random(1).NextBytes(large);
        for (int i = 0; i < 200; i++)
        {
            toHost.Send(i == 100 ? large : BitConverter.GetBytes(i));
        }

        LanTestSupport.PollUntil(() => received.Count == 200, "every message", 10, host, client);
        Assert.Equal(large, received[100]);
        Assert.All(Enumerable.Range(0, 200).Where(i => i != 100), i => Assert.Equal(i, BitConverter.ToInt32(received[i])));

        var echoes = new List<byte[]>();
        client.MessageReceived += (_, e) => echoes.Add(e.Data.ToArray());
        host.SendToMany(host.Connections, [1, 2, 3]);
        LanTestSupport.PollUntil(() => echoes.Count == 1 && toHost.RoundTripTime > TimeSpan.Zero, "the echo and a ping", 5, host, client);
        Assert.Equal(new byte[] { 1, 2, 3 }, echoes[0]);
        Assert.True(toHost.RoundTripTime < TimeSpan.FromSeconds(1));
        Assert.True(host.Connections.Single().RoundTripVariance >= TimeSpan.Zero);

        toHost.Disconnect();
        LanTestSupport.PollUntil(() => events.Count == 2, "both sides to see the end", 5, host, client);
        Assert.Contains("client saw Local", events);
        Assert.Contains("host saw Remote", events);
        Assert.False(toHost.IsConnected);
        toHost.Send([9]);
    }

    [Fact]
    public void Send_RejectsMessagesAboveTheLimit()
    {
        using var host = new LanTransport(HostOptions());
        using var client = new LanTransport(HostOptions());
        host.StartHost(Advertisement);
        client.Connect($"127.0.0.1:{host.LocalPort}");
        LanTestSupport.PollUntil(() => client.Connections.Count == 1, "the connection", 5, host, client);

        Assert.Throws<ArgumentException>(() => client.Connections.Single().Send(new byte[NetProtocol.MaxMessageBytes + 1]));
        Assert.Throws<ArgumentException>(() => new LanTransport(new LanTransportOptions { Port = -1 }));
        Assert.Throws<ArgumentException>(() => new LanTransport(new LanTransportOptions { Timeout = TimeSpan.FromMilliseconds(10) }));
        Assert.Throws<ArgumentException>(() => new LanTransport(new LanTransportOptions { MaxMessageBytes = 10 }));
    }

    [Theory]
    [InlineData("closed-port")]
    [InlineData("not an address")]
    [InlineData("host.invalid:47801")]
    public void Connect_FailsCleanly_WhenNothingAnswers(string target)
    {
        string address = target == "closed-port" ? $"127.0.0.1:{LanTestSupport.FreeTcpPort()}" : target;
        using var client = new LanTransport(HostOptions() with { ConnectTimeout = TimeSpan.FromSeconds(2) });
        DisconnectReason? reason = null;
        client.PeerDisconnected += (_, e) => reason = e.Reason;
        client.Connect(address);
        LanTestSupport.PollUntil(() => reason is not null, "the failure", 10, client);

        Assert.Equal(DisconnectReason.ConnectFailed, reason);
        Assert.Empty(client.Connections);
    }

    [Fact]
    public void Roles_AreEnforced_AndPortsInUseAreReported()
    {
        using var host = new LanTransport(HostOptions());
        host.StartHost(Advertisement);
        Assert.Throws<InvalidOperationException>(() => host.StartHost(Advertisement));
        Assert.Throws<InvalidOperationException>(() => host.Connect("127.0.0.1:1"));
        using var taken = new LanTransport(HostOptions() with { Port = host.LocalPort });
        Assert.Throws<SocketException>(() => taken.StartHost(Advertisement));
        Assert.Equal(TransportRole.None, taken.Role);

        using var client = new LanTransport(HostOptions());
        Assert.Throws<InvalidOperationException>(() => client.UpdateAdvertisement(Advertisement));
        client.Connect($"127.0.0.1:{host.LocalPort}");
        Assert.Throws<InvalidOperationException>(() => client.Connect($"127.0.0.1:{host.LocalPort}"));
        Assert.Null(client.HostAddress);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Poll());
    }

    [Fact]
    public void HostShutdown_ClosesItsClients_Gracefully()
    {
        using var host = new LanTransport(HostOptions());
        using var client = new LanTransport(HostOptions());
        DisconnectReason? reason = null;
        var last = new List<byte[]>();
        client.PeerDisconnected += (_, e) => reason = e.Reason;
        client.MessageReceived += (_, e) => last.Add(e.Data.ToArray());
        host.StartHost(Advertisement);
        client.Connect($"127.0.0.1:{host.LocalPort}");
        LanTestSupport.PollUntil(() => host.Connections.Count == 1 && client.Connections.Count == 1, "the connection", 5, host, client);

        host.Connections.Single().Send([42]);
        host.Shutdown();
        LanTestSupport.PollUntil(() => reason is not null, "the close", 5, host, client);

        Assert.Equal(DisconnectReason.Remote, reason);
        Assert.Equal(new byte[] { 42 }, Assert.Single(last));
        Assert.Equal(TransportRole.None, host.Role);
        Assert.Equal(0, host.LocalPort);
    }

    [Fact]
    public void Garbage_InsteadOfAHandshake_IsRefused()
    {
        using var host = new LanTransport(HostOptions());
        int connected = 0;
        host.PeerConnected += (_, _) => connected++;
        host.StartHost(Advertisement);

        using var raw = new TcpClient();
        raw.Connect(IPAddress.Loopback, host.LocalPort);
        NetworkStream stream = raw.GetStream();
        stream.Write(LanFrame(FrameKind.Handshake, "JUNK!!"u8.ToArray()));
        stream.ReadTimeout = 5000;
        Assert.Equal(0, ReadUntilClosed(stream));
        host.Poll();
        Assert.Equal(0, connected);
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void SilentPeer_TimesOut_AndOversizedFramesCloseTheConnection()
    {
        using var host = new LanTransport(HostOptions() with { Timeout = TimeSpan.FromMilliseconds(400) });
        var reasons = new List<DisconnectReason>();
        host.PeerDisconnected += (_, e) => reasons.Add(e.Reason);
        host.StartHost(Advertisement);

        using var silent = new TcpClient();
        silent.Connect(IPAddress.Loopback, host.LocalPort);
        silent.GetStream().Write(LanFrames.Handshake());
        LanTestSupport.PollUntil(() => reasons.Count == 1, "the timeout", 5, host);
        Assert.Equal(DisconnectReason.Timeout, reasons[0]);

        using var greedy = new TcpClient();
        greedy.Connect(IPAddress.Loopback, host.LocalPort);
        NetworkStream stream = greedy.GetStream();
        stream.Write(LanFrames.Handshake());
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 10_000_000);
        stream.Write(header);
        LanTestSupport.PollUntil(() => reasons.Count == 2, "the oversized frame", 5, host);
        Assert.Equal(DisconnectReason.ProtocolError, reasons[1]);

        using var confused = new TcpClient();
        confused.Connect(IPAddress.Loopback, host.LocalPort);
        NetworkStream confusedStream = confused.GetStream();
        confusedStream.Write(LanFrames.Handshake());
        confusedStream.Write(LanFrame((FrameKind)77, [1]));
        LanTestSupport.PollUntil(() => reasons.Count == 3, "the unknown frame", 5, host);
        Assert.Equal(DisconnectReason.ProtocolError, reasons[2]);
    }

    [Fact]
    public void FullSession_RunsInSync_OverTcp()
    {
        var lockstep = new LockstepOptions { TickMilliseconds = 30, HashIntervalTicks = 4 };
        using var hostTransport = new LanTransport(HostOptions());
        var hostSims = new List<HashChainSimulation>();
        using var host = new HostSession(
            hostTransport,
            new HostOptions
            {
                GameVersion = "1",
                ContentVersion = "1",
                PlayerId = Guid.NewGuid(),
                PlayerName = "Host",
                Room = new RoomSettings { Mode = RoomMode.Endless },
                Lockstep = lockstep,
            },
            start =>
            {
                var sim = new HashChainSimulation(start);
                hostSims.Add(sim);
                return sim;
            });
        var clients = new List<(LanTransport Transport, ClientSession Session, List<HashChainSimulation> Sims)>();
        for (int i = 0; i < 2; i++)
        {
            var transport = new LanTransport(HostOptions());
            var sims = new List<HashChainSimulation>();
            var session = new ClientSession(
                transport,
                new ClientOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "p" + i, Lockstep = lockstep },
                start =>
                {
                    var sim = new HashChainSimulation(start);
                    sims.Add(sim);
                    return sim;
                });
            session.Connect(host.Address!);
            clients.Add((transport, session, sims));
        }

        void UpdateAll()
        {
            host.Update();
            foreach ((_, ClientSession session, _) in clients)
            {
                session.Update();
            }
        }

        LanTestSupport.RunUntil(UpdateAll, () => clients.All(c => c.Session.Phase == ClientPhase.Lobby), "the lobby", 10);
        Assert.All(clients, c => Assert.Equal(RequestResult.Accepted, c.Session.SetReady(true)));
        LanTestSupport.RunUntil(UpdateAll, () => host.CanStart() == RequestResult.Accepted, "everyone to be ready", 10);
        Assert.Equal(RequestResult.Accepted, host.Start(new byte[] { 3 }));
        LanTestSupport.RunUntil(UpdateAll, () => clients.All(c => c.Session.Phase == ClientPhase.InGame), "the match", 10);

        var random = new Random(9);
        var watch = Stopwatch.StartNew();
        LanTestSupport.RunUntil(
            () =>
            {
                foreach ((_, ClientSession session, _) in clients)
                {
                    if (random.Next(4) == 0)
                    {
                        session.SendCommand([(byte)random.Next(256)]);
                    }
                }

                UpdateAll();
            },
            () => watch.Elapsed > TimeSpan.FromSeconds(2.5),
            "the match to run",
            10);
        Assert.Equal(RequestResult.Accepted, host.Pause());
        LanTestSupport.RunUntil(UpdateAll, () => clients.All(c => c.Sims[^1].Tick == hostSims[^1].Tick && c.Session.IsPaused), "the clients to reach the pause", 5);

        LoopbackCluster.AssertInSync(20, [hostSims[^1], .. clients.Select(c => c.Sims[^1])]);
        Assert.Contains(hostSims[^1].Log, e => !e.Command.IsSystem);
        Assert.Empty(clients.SelectMany(c => c.Session.Room!.Players.Where(p => p.Presence == PlayerPresence.Absent)));

        clients[0].Session.Leave();
        LanTestSupport.RunUntil(UpdateAll, () => host.Room.Players.Any(p => p.Presence == PlayerPresence.Absent), "the absence", 5);
        foreach ((LanTransport transport, ClientSession session, _) in clients)
        {
            session.Dispose();
            transport.Dispose();
        }
    }

    [Fact]
    public void Discovery_FindsTheRoom_OverLoopback()
    {
        int discoveryPort = LanTestSupport.FreeUdpPort();
        using var host = new LanTransport(HostOptions(discovery: true, discoveryPort: discoveryPort));
        host.StartHost(Advertisement);
        Assert.Null(host.DiscoveryError);
        using var browser = new LanRoomBrowser(new LanDiscoveryOptions
        {
            Port = discoveryPort,
            BindAddress = IPAddress.Loopback,
            UseBroadcast = false,
            ExtraTargets = [IPAddress.Loopback],
            QueryInterval = TimeSpan.FromMilliseconds(100),
            RoomTimeout = TimeSpan.FromMilliseconds(700),
        });
        int changes = 0;
        browser.RoomsChanged += (_, _) => changes++;
        browser.Start();
        browser.Start();
        Assert.True(browser.IsRunning);

        LanTestSupport.PollUntil(() => { browser.Poll(); return browser.Rooms.Count == 1; }, "the room", 5, host);
        RoomInfo room = browser.Rooms[0];
        Assert.Equal($"127.0.0.1:{host.LocalPort}", room.Address);
        Assert.Equal(Advertisement, room.Room);
        Assert.Equal(NetProtocol.Version, room.ProtocolVersion);
        Assert.True(room.Ping < TimeSpan.FromSeconds(1));
        Assert.True(room.IsCompatible("1.0", "c"));

        host.UpdateAdvertisement(Advertisement with { Players = 2, IsOpen = false });
        browser.Refresh();
        LanTestSupport.PollUntil(() => { browser.Poll(); return browser.Rooms[0].Room.Players == 2; }, "the update", 5, host);
        Assert.False(browser.Rooms[0].Room.IsOpen);

        using var client = new LanTransport(HostOptions());
        client.Connect(browser.Rooms[0].Address);
        LanTestSupport.PollUntil(() => client.Connections.Count == 1, "a connection to the found room", 5, host, client);

        host.Shutdown();
        LanTestSupport.PollUntil(() => { browser.Poll(); return browser.Rooms.Count == 0; }, "the room to expire", 5, host, client);
        Assert.True(changes >= 3);
        browser.Shutdown();
        Assert.False(browser.IsRunning);
    }

    [BroadcastFact]
    public void Discovery_FindsTheRoom_ByBroadcast()
    {
        int discoveryPort = LanTestSupport.FreeUdpPort();
        using var host = new LanTransport(HostOptions(discovery: true, discoveryPort: discoveryPort, discoveryBind: IPAddress.Any));
        host.StartHost(Advertisement with { RoomId = Guid.NewGuid() });
        Assert.Null(host.DiscoveryError);
        using var browser = new LanRoomBrowser(new LanDiscoveryOptions { Port = discoveryPort, QueryInterval = TimeSpan.FromMilliseconds(200) });
        browser.Start();

        LanTestSupport.PollUntil(
            () =>
            {
                browser.Poll();
                return browser.Rooms.Count == 1;
            },
            () => $"the room (rooms seen: {browser.Rooms.Count}, browser error: {browser.LastError?.Message}, host error: {host.LastError?.Message})",
            15,
            host);
        Assert.EndsWith($":{host.LocalPort}", browser.Rooms[0].Address, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryPortInUse_IsReported_AndHostingStillWorks()
    {
        int discoveryPort = LanTestSupport.FreeUdpPort();
        using var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
        blocker.Bind(new IPEndPoint(IPAddress.Loopback, discoveryPort));
        using var host = new LanTransport(HostOptions(discovery: true, discoveryPort: discoveryPort));
        host.StartHost(Advertisement);

        Assert.NotNull(host.DiscoveryError);
        Assert.Contains(discoveryPort.ToString(System.Globalization.CultureInfo.InvariantCulture), host.DiscoveryError, StringComparison.Ordinal);
        Assert.Equal(TransportRole.Host, host.Role);
        Assert.NotNull(host.LastError);
    }

    [Fact]
    public void DiscoveryDatagrams_RoundTrip_AndJunkIsIgnored()
    {
        byte[] query = DiscoveryCodec.EncodeQuery(123456789);
        Assert.True(DiscoveryCodec.TryDecodeQuery(query, out long nonce));
        Assert.Equal(123456789, nonce);
        Assert.False(DiscoveryCodec.TryDecodeQuery(query.AsSpan(1), out _));
        byte[] wrongMagic = [.. query];
        wrongMagic[0] = (byte)'X';
        Assert.False(DiscoveryCodec.TryDecodeQuery(wrongMagic, out _));

        byte[] reply = DiscoveryCodec.EncodeReply(77, 47801, Advertisement);
        DiscoveryReply decoded = DiscoveryCodec.TryDecodeReply(reply)!;
        Assert.Equal((NetProtocol.Version, 77L, 47801), (decoded.ProtocolVersion, decoded.Nonce, decoded.TcpPort));
        Assert.Equal(Advertisement, decoded.Room);
        Assert.Null(DiscoveryCodec.TryDecodeReply(reply.AsMemory(0, 20)));
        Assert.Null(DiscoveryCodec.TryDecodeReply(query));
        Assert.Null(DiscoveryCodec.TryDecodeReply(DiscoveryCodec.EncodeReply(1, 0, Advertisement)));
    }

    [Fact]
    public void Lan_DoesNotMoveTheHost_SoAHostLeavingEndsTheMatch()
    {
        using var transport = new LanTransport(HostOptions());
        Assert.False(transport.SupportsHostMigration);
        Assert.Null(transport.MigrationAddress);
        Assert.Null(transport.RoomHostAddress);
        Assert.False(transport.IsPeerPresent("127.0.0.1:1"));
        Assert.Throws<NotSupportedException>(() => transport.TakeOverHost(Advertisement));

        var lockstep = new LockstepOptions { TickMilliseconds = 30 };
        using var host = new HostSession(
            transport,
            new HostOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "h", Room = new RoomSettings { Mode = RoomMode.Endless }, Lockstep = lockstep },
            start => new HashChainSimulation(start));
        using var clientTransport = new LanTransport(HostOptions());
        using var client = new ClientSession(
            clientTransport,
            new ClientOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "c", Lockstep = lockstep },
            start => new HashChainSimulation(start));
        client.Connect(host.Address!);
        void UpdateBoth()
        {
            host.Update();
            client.Update();
        }

        LanTestSupport.RunUntil(UpdateBoth, () => client.Phase == ClientPhase.Lobby, "the lobby", 10);
        client.SetReady(true);
        LanTestSupport.RunUntil(UpdateBoth, () => host.CanStart() == RequestResult.Accepted, "ready", 10);
        host.Start();
        LanTestSupport.RunUntil(UpdateBoth, () => client.Phase == ClientPhase.InGame, "the match", 10);
        byte[] save = host.SaveMatch();
        host.Leave();
        LanTestSupport.RunUntil(client.Update, () => client.Phase == ClientPhase.Ended, "the end", 10);

        Assert.Equal(SessionEndReason.HostClosed, client.EndReason);
        Assert.Null(host.SuccessorAddress);
        Assert.True(HostSession.ReadSaveInfo(save).Tick >= 0);
    }

    [Theory]
    [InlineData("192.168.1.5:47000", true, "192.168.1.5", 47000)]
    [InlineData("192.168.1.5", true, "192.168.1.5", 47801)]
    [InlineData("[::1]:5000", true, "::1", 5000)]
    [InlineData("localhost", true, "localhost", 47801)]
    [InlineData("game-pc:1234", true, "game-pc", 1234)]
    [InlineData("game-pc:99999", false, "", 0)]
    [InlineData("game-pc:abc", false, "", 0)]
    [InlineData("a:b:c", false, "", 0)]
    [InlineData("   ", false, "", 0)]
    [InlineData("bad host", false, "", 0)]
    public void Addresses_AreParsed(string text, bool valid, string host, int port)
    {
        Assert.Equal(valid, LanAddress.TryParse(text, 47801, out string parsedHost, out int parsedPort));
        if (valid)
        {
            Assert.Equal((host, port), (parsedHost, parsedPort));
        }
    }

    private static byte[] LanFrame(FrameKind kind, byte[] payload) => LanFrames.Encode(kind, payload);

    private static int ReadUntilClosed(NetworkStream stream)
    {
        var buffer = new byte[256];
        try
        {
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                // Drain until the host closes the socket.
            }

            return read;
        }
        catch (System.IO.IOException)
        {
            return 0;
        }
    }
}
