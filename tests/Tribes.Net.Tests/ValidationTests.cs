using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>What the host does with requests it must refuse, and the options both sessions check.</summary>
public sealed class ValidationTests
{
    [Fact]
    public void Host_RefusesRequestsThatDoNotFitThePhase()
    {
        using var cluster = new LoopbackCluster();
        using var raw = new RawLoopbackClient(cluster);
        raw.Hello(Guid.NewGuid());
        cluster.Run(TimeSpan.FromMilliseconds(50));
        Assert.IsType<WelcomeMessage>(raw.Messages[0]);

        raw.Send(new CommandMessage(5, 1, new byte[] { 1 }));
        raw.Send(new StateHashMessage(0, 1));
        raw.Send(new ControlRequestMessage(ControlKind.Pause, 0));
        raw.Send(new ForfeitMessage());
        cluster.Run(TimeSpan.FromMilliseconds(50));

        RequestDeniedMessage[] denied = [.. raw.Messages.OfType<RequestDeniedMessage>()];
        Assert.Equal(
            [(RequestKind.Command, RequestResult.NotAllowedNow), (RequestKind.Pause, RequestResult.NotAllowedNow), (RequestKind.Forfeit, RequestResult.NotAllowedNow)],
            denied.Select(d => (d.Request, d.Result)));
        Assert.Single(cluster.RejectedCommands);

        raw.Send(new WelcomeMessage(cluster.Host.Room, RoomExtras.Empty, -1, Guid.Empty, 0));
        cluster.RunUntil(() => raw.Disconnected, "the host to drop the peer");
        Assert.Equal(SessionEndReason.ProtocolError, raw.Messages.OfType<SessionEndMessage>().Single().Reason);
        Assert.Single(cluster.Host.Room.Players);
    }

    [Fact]
    public void Host_RefusesBadMatchRequests()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Start());
        using var player = new RawLoopbackClient(cluster, "player");
        using var watcher = new RawLoopbackClient(cluster, "watcher");
        player.Hello(Guid.NewGuid());
        watcher.Hello(Guid.NewGuid(), spectate: true);
        cluster.Run(TimeSpan.FromMilliseconds(100));

        player.Send(new ControlRequestMessage(ControlKind.Speed, 1000));
        player.Send(new CommandMessage(0, 1, new byte[2000]));
        watcher.Send(new CommandMessage(0, 1, new byte[] { 1 }));
        watcher.Send(new ControlRequestMessage(ControlKind.Pause, 0));
        player.Send(new StateHashMessage(123_456, 1));
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal(
            [(RequestKind.Speed, RequestResult.InvalidValue), (RequestKind.Command, RequestResult.InvalidValue)],
            player.Messages.OfType<RequestDeniedMessage>().Select(d => (d.Request, d.Result)));
        Assert.Equal(
            [(RequestKind.Command, RequestResult.NotAPlayer), (RequestKind.Pause, RequestResult.NotAllowedNow)],
            watcher.Messages.OfType<RequestDeniedMessage>().Select(d => (d.Request, d.Result)));
        Assert.Empty(cluster.HostDesyncs);
        Assert.False(cluster.Host.IsPaused);
    }

    [Fact]
    public void HostOptions_AreChecked()
    {
        var network = new LoopbackNetwork(new ManualClock());
        var good = new HostOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "h" };
        HostOptions[] bad =
        [
            good with { PlayerId = Guid.Empty },
            good with { GameVersion = "" },
            good with { ContentVersion = new string('x', RoomLimits.MaxVersionLength + 1) },
            good with { HelloTimeout = TimeSpan.Zero },
            good with { Room = new RoomSettings { MaxPlayers = 0 } },
            good with { Lockstep = new LockstepOptions { TickMilliseconds = 0 } },
        ];
        foreach (HostOptions options in bad)
        {
            Assert.Throws<ArgumentException>(() => new HostSession(new LoopbackTransport(network, "h"), options, start => new HashChainSimulation(start)));
        }

        Assert.Throws<ArgumentNullException>(() => new HostSession(new LoopbackTransport(network, "h"), null!, start => new HashChainSimulation(start)));
        using var host = new HostSession(
            new LoopbackTransport(network, "h"),
            good with { Room = new RoomSettings { Name = " \u0001 ", Mode = RoomMode.Endless } },
            start => new HashChainSimulation(start));
        Assert.Equal("Tribes", host.Room.Settings.Name);
        Assert.Equal("h", host.Address);
        Assert.Throws<ArgumentException>(() => host.Start(new byte[ProtocolLimits.MaxSetupBytes + 1]));
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() => host.Update());
    }

    [Fact]
    public void ClientOptions_AreChecked()
    {
        var network = new LoopbackNetwork(new ManualClock());
        var good = new ClientOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "c" };
        ClientOptions[] bad =
        [
            good with { PlayerId = Guid.Empty },
            good with { GameVersion = " " },
            good with { ReconnectAttempts = -1 },
            good with { ReconnectDelay = TimeSpan.FromSeconds(-1) },
            good with { PreferredColour = 8 },
            good with { Lockstep = new LockstepOptions { MaxCommandBytes = 0 } },
        ];
        foreach (ClientOptions options in bad)
        {
            Assert.Throws<ArgumentException>(() => new ClientSession(new LoopbackTransport(network, "c"), options, start => new HashChainSimulation(start)));
        }

        using var session = new ClientSession(new LoopbackTransport(network, "c"), good, start => new HashChainSimulation(start));
        Assert.Throws<ArgumentException>(() => session.SendCommand(new byte[2000]));
        Assert.False(session.SendCommand([1]));
        Assert.Equal(RequestResult.NotAllowedNow, session.ChooseColour(2));
        Assert.Equal(RequestResult.NotAllowedNow, session.ChooseTeam(2));
        Assert.Equal(RequestResult.NotAllowedNow, session.SetReady(true));
        Assert.Equal(RequestResult.NotAllowedNow, session.SendChat("hi"));
        session.Leave();
        Assert.Equal(ClientPhase.Idle, session.Phase);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Update());
    }

    [Fact]
    public void HostThatCouldNotStart_LetsGoOfTheTransport()
    {
        using var cluster = new LoopbackCluster();
        var second = new LoopbackTransport(cluster.Network, LoopbackCluster.HostAddress);
        HostOptions options = cluster.HostOptions with { PlayerId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => new HostSession(second, options, start => new HashChainSimulation(start), cluster.Clock));

        cluster.Host.Close();
        using var replacement = new HostSession(second, options, start => new HashChainSimulation(start), cluster.Clock);
        cluster.ReplaceHost(() => replacement);
        using var raw = new RawLoopbackClient(cluster);
        raw.Hello(Guid.NewGuid());
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.Single(raw.Messages.OfType<WelcomeMessage>());
        Assert.Equal(2, replacement.Room.Players.Count);
    }

    [Fact]
    public void ClientConnect_ThatTheTransportRefuses_LeavesTheSessionIdle()
    {
        using var cluster = new LoopbackCluster();
        using var session = new ClientSession(
            cluster.HostTransport,
            new ClientOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "c" },
            start => new HashChainSimulation(start),
            cluster.Clock);

        Assert.Throws<InvalidOperationException>(() => session.Connect("elsewhere"));
        Assert.Equal(ClientPhase.Idle, session.Phase);
    }

    [Fact]
    public void Reconnect_OnABusyTransport_EndsTheSession()
    {
        using var cluster = new LoopbackCluster();
        TestPeer peer = cluster.AddClient("p", configure: o => o with { ReconnectDelay = TimeSpan.FromMilliseconds(200) });
        cluster.StartMatch();
        peer.Transport.SimulateConnectionLoss();
        cluster.RunUntil(() => peer.Session.Phase == ClientPhase.Reconnecting, "the loss");

        peer.Transport.Link = new LoopbackLinkOptions { Latency = TimeSpan.FromSeconds(5) };
        peer.Transport.Connect("somewhere-else");
        cluster.RunUntil(() => peer.Session.Phase == ClientPhase.Ended, "the reconnect to give up");

        Assert.Equal(SessionEndReason.ConnectionLost, peer.Session.EndReason);
    }

    [Fact]
    public void Describe_HasASentenceForEveryResult()
    {
        foreach (RequestResult result in Enum.GetValues<RequestResult>())
        {
            Assert.EndsWith(".", SessionShared.Describe(result), StringComparison.Ordinal);
        }

        Assert.Equal("99", SessionShared.Describe((RequestResult)99));
    }

    [Fact]
    public void Events_ReportRoomTicksAndControl()
    {
        using var cluster = new LoopbackCluster();
        var hostRooms = new List<RoomState>();
        var hostTicks = new List<long>();
        var hostControl = new List<(bool, int)>();
        cluster.Host.RoomChanged += (_, e) => hostRooms.Add(e.Room);
        cluster.Host.TickStepped += (_, e) => hostTicks.Add(e.Tick);
        cluster.Host.ControlStateChanged += (_, e) => hostControl.Add((e.Paused, e.SpeedPercent));
        var matchStarts = new List<MatchStartedEventArgs>();
        cluster.Host.MatchStarted += (_, e) => matchStarts.Add(e);
        TestPeer a = cluster.AddClient("a");
        var clientRooms = new List<RoomState>();
        var clientTicks = new List<(long Tick, int Commands)>();
        var clientControl = new List<(bool, int)>();
        a.Session.RoomChanged += (_, e) => clientRooms.Add(e.Room);
        a.Session.TickStepped += (_, e) => clientTicks.Add((e.Tick, e.Commands.Count));
        a.Session.ControlStateChanged += (_, e) => clientControl.Add((e.Paused, e.SpeedPercent));

        cluster.StartMatch();
        Assert.True(a.Session.SendCommand([1, 2]));
        cluster.Run(TimeSpan.FromSeconds(1));
        cluster.Host.SetSpeed(200);
        cluster.Host.Pause();
        cluster.Run(TimeSpan.FromSeconds(1));
        a.Session.Leave();

        Assert.True(hostRooms.Count >= 3);
        Assert.Equal(RoomPhase.InGame, hostRooms[^1].Phase);
        Assert.Equal(hostRooms.Select(r => r.Revision).Order(), hostRooms.Select(r => r.Revision));
        Assert.Equal(RoomPhase.InGame, clientRooms[^1].Phase);
        Assert.Equal(Enumerable.Range(0, hostTicks.Count).Select(t => (long)t), hostTicks);
        Assert.Equal(hostTicks, clientTicks.Select(t => t.Tick));
        Assert.Equal(1, clientTicks.Sum(t => t.Commands));
        Assert.Equal([(false, 200), (true, 200)], hostControl);
        Assert.Equal(hostControl, clientControl);
        Assert.Same(cluster.Host.Simulation, Assert.Single(matchStarts).Simulation);
        Assert.False(matchStarts[0].FromSnapshot);
        Assert.Equal([ClientPhase.Connecting, ClientPhase.Lobby, ClientPhase.InGame, ClientPhase.Ended], a.Phases);
        Assert.Equal(SessionShared.PublicId(a.Id, cluster.Host.Room.RoomId), a.Session.LocalPlayerId);
        Assert.NotEqual(a.Id, a.Session.LocalPlayerId);
        Assert.Same(a.Simulation, a.Session.Simulation);
    }
}
