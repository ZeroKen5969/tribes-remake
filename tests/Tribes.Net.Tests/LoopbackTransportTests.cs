using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class LoopbackTransportTests
{
    private static readonly RoomAdvertisement Advertisement = new()
    {
        RoomId = Guid.NewGuid(),
        Name = "room",
        GameVersion = "1",
        ContentVersion = "1",
    };

    private readonly ManualClock clock = new();
    private readonly LoopbackNetwork network;
    private readonly LoopbackTransport host;
    private readonly List<string> hostLog = [];
    private readonly List<string> clientLog = [];

    public LoopbackTransportTests()
    {
        network = new LoopbackNetwork(clock, seed: 3);
        host = new LoopbackTransport(network, "h");
        Record(host, hostLog);
        host.StartHost(Advertisement);
    }

    [Fact]
    public void Messages_ArriveInOrder_AfterTheLatency_EvenWithJitter()
    {
        LoopbackTransport client = Client(new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(50), Jitter = TimeSpan.FromMilliseconds(40) });
        RunFor(TimeSpan.FromMilliseconds(300), client);
        Assert.Equal(["connected"], hostLog);
        Assert.Equal(["connected"], clientLog);
        Assert.Equal("c", host.Connections.Single().RemoteAddress);
        Assert.Equal("h", client.Connections.Single().RemoteAddress);

        IConnection toHost = client.Connections.Single();
        TimeSpan sentAt = clock.Now;
        for (byte i = 0; i < 50; i++)
        {
            toHost.Send([i]);
        }

        RunFor(TimeSpan.FromMilliseconds(40), client);
        Assert.Equal(["connected"], hostLog);
        RunFor(TimeSpan.FromMilliseconds(200), client);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => "message " + i), hostLog.Skip(1));
        Assert.True(clock.Now - sentAt >= TimeSpan.FromMilliseconds(50));

        host.SendToMany(host.Connections, [7]);
        RunFor(TimeSpan.FromMilliseconds(200), client);
        Assert.Equal("message 7", clientLog[^1]);
    }

    [Fact]
    public void Pings_MeasureTheRoundTrip()
    {
        LoopbackTransport client = Client(new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(40), PingInterval = TimeSpan.FromMilliseconds(100) });
        RunFor(TimeSpan.FromSeconds(3), client);

        Assert.InRange(client.Connections.Single().RoundTripTime.TotalMilliseconds, 79, 95);
        Assert.InRange(host.Connections.Single().RoundTripTime.TotalMilliseconds, 79, 95);
        Assert.True(client.Connections.Single().RoundTripVariance < TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public void ConnectingToAnUnknownAddress_Fails()
    {
        var client = new LoopbackTransport(network, "lost");
        Record(client, clientLog);
        client.Connect("nowhere");
        RunFor(TimeSpan.FromMilliseconds(20), client);

        Assert.Equal(["disconnected ConnectFailed"], clientLog);
        Assert.Empty(client.Connections);
        Assert.Equal(TransportRole.Client, client.Role);
        Assert.Null(client.HostAddress);
    }

    [Fact]
    public void Disconnect_ClosesBothSides_AfterQueuedMessages()
    {
        LoopbackTransport client = Client(new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(10) });
        RunFor(TimeSpan.FromMilliseconds(100), client);
        IConnection toHost = client.Connections.Single();
        toHost.Send([1]);
        toHost.Disconnect();
        toHost.Send([2]);
        RunFor(TimeSpan.FromMilliseconds(100), client);

        Assert.Equal(["connected", "message 1", "disconnected Remote"], hostLog);
        Assert.Equal(["connected", "disconnected Local"], clientLog);
        Assert.False(toHost.IsConnected);
        Assert.Empty(host.Connections);

        client.Connect("h");
        RunFor(TimeSpan.FromMilliseconds(100), client);
        Assert.Single(client.Connections);
        host.Shutdown();
        RunFor(TimeSpan.FromMilliseconds(100), client);
        Assert.Equal("disconnected HostLeft", clientLog[^1]);
        Assert.Equal(TransportRole.None, host.Role);
        Assert.Empty(network.ListRooms());
    }

    [Fact]
    public void ConnectionLoss_IsSeenByBothSides()
    {
        LoopbackTransport client = Client(new LoopbackLinkOptions { LossDetectionDelay = TimeSpan.FromMilliseconds(200) });
        RunFor(TimeSpan.FromMilliseconds(50), client);
        client.Connections.Single().Send([1]);
        host.SimulateConnectionLoss(host.Connections.Single());
        RunFor(TimeSpan.FromMilliseconds(100), client);
        Assert.Equal(["connected"], clientLog);
        RunFor(TimeSpan.FromMilliseconds(200), client);

        Assert.Equal(["connected", "disconnected ConnectionLost"], clientLog);
        Assert.Equal(["connected", "disconnected ConnectionLost"], hostLog);
        Assert.Throws<ArgumentException>(() => client.SimulateConnectionLoss(new LoopbackTransport(network, "x").Connections.FirstOrDefault()!));
    }

    [Fact]
    public void RandomConnectionLoss_DropsTheLinkSooner_OrLater()
    {
        LoopbackTransport client = Client(new LoopbackLinkOptions { ConnectionLossChance = 0.2 });
        RunFor(TimeSpan.FromMilliseconds(50), client);
        for (int i = 0; i < 100 && client.Connections.Count > 0; i++)
        {
            client.Connections.Single().Send([1]);
            RunFor(TimeSpan.FromMilliseconds(10), client);
        }

        Assert.Contains("disconnected ConnectionLost", clientLog);
    }

    [Fact]
    public void Roles_AreEnforced()
    {
        Assert.Throws<InvalidOperationException>(() => host.Connect("h"));
        Assert.Throws<InvalidOperationException>(() => host.StartHost(Advertisement));
        Assert.Throws<InvalidOperationException>(() => new LoopbackTransport(network, "h").StartHost(Advertisement));
        Assert.Equal("h", host.HostAddress);
        host.UpdateAdvertisement(Advertisement with { Name = "renamed" });
        Assert.Equal("renamed", network.ListRooms().Single().Room.Name);

        LoopbackTransport client = Client(new LoopbackLinkOptions());
        Assert.Throws<InvalidOperationException>(() => client.Connect("h"));
        Assert.Throws<InvalidOperationException>(() => client.UpdateAdvertisement(Advertisement));
        Assert.Throws<InvalidOperationException>(() => client.StartHost(Advertisement));

        var refusing = new LoopbackTransport(network, "refusing");
        refusing.StartHost(Advertisement);
        refusing.Shutdown();
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Poll());
        client.Dispose();
    }

    [Fact]
    public void AHostThatStoppedBeforeAccepting_RefusesTheConnection()
    {
        var client = new LoopbackTransport(network, "late") { Link = new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(30) } };
        Record(client, clientLog);
        client.Connect("h");
        host.Shutdown();
        RunFor(TimeSpan.FromMilliseconds(200), client);

        Assert.Equal(["disconnected ConnectFailed"], clientLog);
        Assert.Empty(hostLog);
    }

    [Fact]
    public void Room_OutlivesItsHost_AndOneClientTakesOver()
    {
        var a = new LoopbackTransport(network, "a");
        var b = new LoopbackTransport(network, "b");
        var aLog = new List<string>();
        var bLog = new List<string>();
        Record(a, aLog);
        Record(b, bLog);
        a.Connect("h");
        b.Connect("h");
        RunFor(TimeSpan.FromMilliseconds(50), a, b);
        Assert.True(a.SupportsHostMigration);
        Assert.Equal("a", a.MigrationAddress);
        Assert.Null(host.MigrationAddress);
        Assert.True(a.IsPeerPresent("b") && b.IsPeerPresent("a") && a.IsPeerPresent("h"));
        Assert.False(a.IsPeerPresent("nobody"));
        Assert.Equal("h", a.RoomHostAddress);

        host.SimulateCrash();
        RunFor(TimeSpan.FromMilliseconds(50), a, b);
        Assert.Equal("disconnected HostLeft", aLog[^1]);
        Assert.Equal("disconnected HostLeft", bLog[^1]);
        Assert.False(b.IsPeerPresent("h"));
        Assert.Null(b.RoomHostAddress);

        Assert.Null(a.TakeOverHost(Advertisement with { Name = "a's room" }));
        Assert.Equal("a", b.TakeOverHost(Advertisement));
        Assert.Equal("a", b.RoomHostAddress);
        Assert.Equal(TransportRole.Host, a.Role);
        Assert.Equal("a's room", network.ListRooms().Single().Room.Name);
        b.Connect("a");
        RunFor(TimeSpan.FromMilliseconds(50), a, b);
        Assert.Single(a.Connections);
        Assert.Throws<InvalidOperationException>(() => a.TakeOverHost(Advertisement));
        host.SimulateCrash();
        Assert.Throws<InvalidOperationException>(() => new LoopbackTransport(network, "outsider").TakeOverHost(Advertisement));
    }

    [Fact]
    public void WithoutHostMigration_AHostThatLeaves_TakesTheRoomAlong()
    {
        var lan = new LoopbackNetwork(clock, supportsHostMigration: false);
        var lanHost = new LoopbackTransport(lan, "lan-host");
        lanHost.StartHost(Advertisement);
        var client = new LoopbackTransport(lan, "lan-client");
        var log = new List<string>();
        Record(client, log);
        client.Connect("lan-host");
        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            lanHost.Poll();
            client.Poll();
        }

        Assert.False(client.SupportsHostMigration);
        Assert.Null(client.MigrationAddress);
        Assert.Throws<NotSupportedException>(() => client.TakeOverHost(Advertisement));
        lanHost.Shutdown();
        clock.Advance(TimeSpan.FromMilliseconds(5));
        client.Poll();
        Assert.Equal("disconnected Remote", log[^1]);
    }

    private LoopbackTransport Client(LoopbackLinkOptions link)
    {
        var client = new LoopbackTransport(network, "c") { Link = link };
        Record(client, clientLog);
        client.Connect("h");
        return client;
    }

    private void RunFor(TimeSpan duration, params LoopbackTransport[] clients)
    {
        for (TimeSpan end = clock.Now + duration; clock.Now < end;)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            host.Poll();
            foreach (LoopbackTransport client in clients)
            {
                client.Poll();
            }
        }
    }

    private static void Record(LoopbackTransport transport, List<string> log)
    {
        transport.PeerConnected += (_, _) => log.Add("connected");
        transport.PeerDisconnected += (_, e) => log.Add("disconnected " + e.Reason);
        transport.MessageReceived += (_, e) => log.Add("message " + string.Join(",", e.Data.ToArray()));
    }
}
