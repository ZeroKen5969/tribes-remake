using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>The host leaves or drops out mid-match and the match goes on under another peer.</summary>
public sealed class MigrationTests
{
    private static LoopbackLinkOptions Link(int latencyMs, int jitterMs = 0) => new()
    {
        Latency = TimeSpan.FromMilliseconds(latencyMs),
        Jitter = TimeSpan.FromMilliseconds(jitterMs),
    };

    /// <summary>Three players besides the host; a has the lowest slot, so it is next in line.</summary>
    private static (LoopbackCluster Cluster, TestPeer A, TestPeer B, TestPeer C) Match(int latencyA = 10, int latencyB = 30, int latencyC = 50)
    {
        var cluster = new LoopbackCluster(RoomMode.Endless, seed: latencyA + latencyB + latencyC);

        // One after the other, so the slots follow the order of joining whatever the latencies.
        TestPeer a = cluster.AddClient("a", Link(latencyA, 5));
        cluster.RunUntilJoined(a);
        TestPeer b = cluster.AddClient("b", Link(latencyB, 10));
        cluster.RunUntilJoined(b);
        TestPeer c = cluster.AddClient("c", Link(latencyC, 10));
        cluster.StartMatch([1]);
        Assert.Equal([1, 2, 3], new[] { a, b, c }.Select(p => p.Session.LocalPlayer!.Slot));
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        return (cluster, a, b, c);
    }

    /// <summary>Every command each surviving client sent ran exactly once in the match the new host carries on.</summary>
    private static void AssertEveryCommandRanOnce(HashChainSimulation finalHost, params TestPeer[] clients)
    {
        foreach (TestPeer client in clients)
        {
            int slot = client.Session.LocalPlayer!.Slot;
            long[] ran = [.. finalHost.Log
                .Where(e => e.Command.Slot == slot && !e.Command.IsSystem)
                .Select(e => e.Command.Sequence)];
            Assert.Equal(Enumerable.Range(1, client.CommandsSent).Select(i => (long)i), ran.Order());
        }
    }

    private static void RunUntilFollowing(LoopbackCluster cluster, HostSession oldHost, params TestPeer[] followers)
    {
        try
        {
            cluster.RunUntil(
                () => cluster.Host != oldHost && followers.All(f => f.Session.Phase == ClientPhase.InGame),
                "the match to move to the next host");
        }
        catch (Xunit.Sdk.XunitException)
        {
            string states = string.Join("; ", cluster.Clients.Select(c => $"{c.Name}: {string.Join(">", c.Phases)} {c.Session.EndReason} {c.Session.EndMessage}"));
            throw new Xunit.Sdk.XunitException($"The match did not move on (new host: {cluster.Host != oldHost}). {states}");
        }
    }

    /// <summary>Lets the commands still on their way run, without sending more.</summary>
    private static void Settle(LoopbackCluster cluster) => cluster.Run(TimeSpan.FromSeconds(2));

    [Fact]
    public void HostThatLeaves_HandsTheMatchToTheNextPlayer()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        HashChainSimulation oldSimulation = cluster.HostSimulation;
        long tickBefore = oldSimulation.Tick;

        oldHost.Leave();
        Assert.Equal("a", oldHost.SuccessorAddress);
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.Equal(SessionEndReason.BecameHost, a.Session.EndReason);
        Assert.Same(a.Simulation, cluster.Host.Simulation);
        Assert.Equal(a.PublicId, cluster.Host.LocalPlayer.Id);
        Assert.Equal([a.PublicId], b.HostChanges);
        Assert.Equal([a.PublicId], c.HostChanges);
        Assert.True(cluster.HostSimulation.Tick > tickBefore + 25, "The match should keep running.");
        Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.PlayerInSlot(0)!.Presence);
        Assert.Contains(0, b.Simulation.AwaySlots);
        Assert.Single(b.Simulations);
        Assert.Empty(cluster.HostDesyncs);
        LoopbackCluster.AssertInSync(25, oldSimulation, a.Simulation, b.Simulation, c.Simulation);
        LoopbackCluster.AssertInSync(40, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
    }

    [Fact]
    public void HostThatCrashes_IsReplaced_AndNoCommandIsLostOrRunTwice()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;

        // Commands in flight to the host when it goes.
        Assert.True(b.Session.SendCommand([0xB1]));
        Assert.True(c.Session.SendCommand([0xC1]));
        b.CommandsSent++;
        c.CommandsSent++;
        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.Empty(cluster.HostDesyncs);
        Assert.All([b, c], p => Assert.Empty(p.Desyncs));
        LoopbackCluster.AssertInSync(40, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
        Assert.Contains(a.Simulation.Log, e => e.Command.Payload.Span.SequenceEqual(new byte[] { 0xB1 }));
    }

    [Fact]
    public void SuccessorThatMissedTheLastBatches_ResynchronisesThePeersAheadOfIt()
    {
        // The next in line is far away, so the others hold batches it never got.
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match(latencyA: 200, latencyB: 5, latencyC: 5);
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.Run(TimeSpan.FromMilliseconds(50));
        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.True(b.MatchStarts[^1].FromSnapshot, "b ran batches the new host never had, so it needs its state.");
        Assert.True(c.MatchStarts[^1].FromSnapshot);
        LoopbackCluster.AssertInSync(30, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
    }

    [Fact]
    public void HostThatCrashesRightAfterAHashCheck_LeavesNoFalseDesync()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.RunUntil(() => cluster.HostSimulation.Tick % 8 == 1, "a hash check to run");
        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Empty(cluster.HostDesyncs);
        Assert.All([b, c], p => Assert.Empty(p.Desyncs));
        LoopbackCluster.AssertInSync(30, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void OldHost_CanRejoin_AsAnOrdinaryPlayer()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        oldHost.Leave();
        RunUntilFollowing(cluster, oldHost, b, c);

        TestPeer back = cluster.JoinWith("old host", cluster.HostTransport, cluster.HostOptions.PlayerId, oldHost.SuccessorAddress!);
        cluster.RunUntil(() => back.Session.Phase == ClientPhase.InGame, "the old host to rejoin");
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        PlayerInfo rejoined = back.Session.LocalPlayer!;
        Assert.Equal(0, rejoined.Slot);
        Assert.False(rejoined.IsHost);
        Assert.Equal(PlayerPresence.Active, rejoined.Presence);
        Assert.True(back.Session.CanSendCommands);
        Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned && p.Slot == 0);
        Assert.Contains(a.Simulation.Log, e => e.Command.Slot == 0 && !e.Command.IsSystem && e.Tick > back.Simulation.FirstTick);
        LoopbackCluster.AssertInSync(25, a.Simulation, b.Simulation, c.Simulation, back.Simulation);
    }

    [Fact]
    public void MatchSurvives_ASecondHostChange()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession first = cluster.Host;
        first.Leave();
        RunUntilFollowing(cluster, first, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        HostSession second = cluster.Host;
        a.Transport.SimulateCrash();
        RunUntilFollowing(cluster, second, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(b.Hosting, cluster.Host);
        Assert.Equal([a.PublicId, b.PublicId], c.HostChanges);
        Assert.All([0, 1], slot => Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.PlayerInSlot(slot)!.Presence));
        Assert.Empty(cluster.HostDesyncs);
        LoopbackCluster.AssertInSync(40, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(b.Simulation, c);
    }

    [Fact]
    public void PlayerWhoIsSlowToComeBack_CountsAsAbsentAfterTheGracePeriod()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        c.Session.Dispose();
        cluster.Clients.Remove(c);
        oldHost.Leave();
        RunUntilFollowing(cluster, oldHost, b);
        Assert.Equal(PlayerPresence.Active, cluster.Host.Room.PlayerInSlot(3)!.Presence);
        cluster.Run(TimeSpan.FromSeconds(6));

        Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.PlayerInSlot(3)!.Presence);
        Assert.Contains(3, b.Simulation.AwaySlots);
    }

    [Fact]
    public void Followers_RetryUntilTheSuccessorIsReady()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;

        // The successor's device is the last to notice, so the others reach it before it hosts.
        cluster.Clients.Remove(a);
        cluster.Clients.Add(a);
        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Same(a.Hosting, cluster.Host);
        LoopbackCluster.AssertInSync(20, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void SuccessorThatNeverAnswers_IsSkipped_AfterTheMigrationTimeout()
    {
        var cluster = new LoopbackCluster(RoomMode.Endless);
        using LoopbackCluster owned = cluster;
        Func<ClientOptions, ClientOptions> quick = o => o with { MigrationTimeout = TimeSpan.FromSeconds(2) };
        TestPeer a = cluster.AddClient("a", Link(10), quick);
        cluster.RunUntilJoined(a);
        TestPeer b = cluster.AddClient("b", Link(20), quick);
        cluster.RunUntilJoined(b);
        TestPeer c = cluster.AddClient("c", Link(30), quick);
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        HostSession oldHost = cluster.Host;

        // a's game hangs: its device stays in the room but its session never runs again.
        cluster.Clients.Remove(a);
        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Same(b.Hosting, cluster.Host);
        Assert.Equal([b.PublicId], c.HostChanges);
        Assert.True(cluster.Clock.Now > TimeSpan.FromSeconds(4), "c should have waited for a first.");
        LoopbackCluster.AssertInSync(20, b.Simulation, c.Simulation);
    }

    [Fact]
    public void ForfeitTheOldHostHadNotSentYet_IsKeptByTheNewHost()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        Assert.Equal(RequestResult.Accepted, oldHost.Pause());
        cluster.Run(TimeSpan.FromMilliseconds(300));
        Assert.Equal(RequestResult.Accepted, c.Session.Forfeit());
        cluster.RunUntil(() => a.Session.Room!.FindPlayer(c.PublicId)!.Presence == PlayerPresence.Forfeited, "the forfeit to show");
        Assert.DoesNotContain(3, a.Simulation.ForfeitedSlots);

        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Resume());
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Contains(3, a.Simulation.ForfeitedSlots);
        Assert.Contains(3, b.Simulation.ForfeitedSlots);
        Assert.False(c.Session.CanSendCommands);
        LoopbackCluster.AssertInSync(5, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void SuccessorWhoseReturnWasNotSentYet_ReturnsUnderItsOwnHosting()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        a.Transport.SimulateConnectionLoss();
        cluster.RunUntil(() => b.Simulation.AwaySlots.Contains(1), "a to be away on the other peers");
        Assert.Equal(RequestResult.Accepted, oldHost.Pause());
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.InGame && oldHost.Room.PlayerInSlot(1)!.Presence == PlayerPresence.Active, "a to be back");

        cluster.HostTransport.SimulateCrash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.Host.Resume();
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.DoesNotContain(1, b.Simulation.AwaySlots);
        Assert.Equal(PlayerPresence.Active, cluster.Host.LocalPlayer.Presence);
        LoopbackCluster.AssertInSync(5, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void TakeOver_ThatLosesTheRace_FollowsTheWinner()
    {
        (LoopbackCluster cluster, TestPeer a, TestPeer b, TestPeer c) = Match();
        using LoopbackCluster owned = cluster;
        cluster.HostTransport.SimulateCrash();
        Assert.Null(c.Transport.TakeOverHost(new RoomAdvertisement { RoomId = Guid.NewGuid(), Name = "c", GameVersion = "1", ContentVersion = "1" }));

        var state = new SuccessorState
        {
            Transport = b.Transport,
            Options = new ClientOptions { GameVersion = LoopbackCluster.GameVersion, ContentVersion = LoopbackCluster.ContentVersion, PlayerId = b.Id, PlayerName = "b" },
            SimulationFactory = start => new HashChainSimulation(start),
            Clock = cluster.Clock,
            SelfId = b.PublicId,
            Room = b.Session.Room!,
            Banned = [],
            Ledger = new MatchLedger(b.Session.Room!.Players),
            Start = b.Session.Match!,
            Simulation = b.Simulation,
            TimelineId = Guid.NewGuid(),
            History = [],
            Hashes = [],
            Paused = false,
            SpeedPercent = 100,
            PendingCommands = [],
        };

        Assert.Null(HostSession.TakeOver(state, out string? winner));
        Assert.Equal("c", winner);
        Assert.Equal(TransportRole.Client, b.Transport.Role);
    }

    [Fact]
    public void WithoutHostMigration_AHostThatLeaves_EndsTheMatch()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, hostMigration: false);
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();
        cluster.Host.Leave();
        cluster.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal(SessionEndReason.HostClosed, a.Session.EndReason);
        Assert.Null(cluster.Host.SuccessorAddress);
    }

    [Fact]
    public void HostLeavingTheLobby_ClosesTheRoom()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        cluster.Host.Leave();
        cluster.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal(SessionEndReason.HostClosed, a.Session.EndReason);
    }

    [Fact]
    public void Spectator_TakesOver_WhenNoPlayerIsLeft_EvenWhileReconnecting()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer watcher = cluster.AddClient("watcher", configure: o => o with { Spectate = true, ReconnectDelay = TimeSpan.FromMilliseconds(100) });
        cluster.StartMatch();
        cluster.Run(TimeSpan.FromSeconds(1));
        HostSession oldHost = cluster.Host;

        // The watcher's own link drops first; while it reconnects, the host goes too.
        watcher.Transport.SimulateConnectionLoss();
        cluster.RunUntil(() => watcher.Session.Phase == ClientPhase.Reconnecting, "the link loss");
        cluster.HostTransport.SimulateCrash();
        cluster.RunUntil(() => cluster.Host != oldHost, "the watcher to take over");
        long tick = cluster.HostSimulation.Tick;
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Same(watcher.Hosting, cluster.Host);
        Assert.False(cluster.Host.LocalPlayer.HasSlot);
        Assert.True(cluster.HostSimulation.Tick > tick + 5);
    }

    [Fact]
    public void ClientStillLoadingTheMatch_CannotTakeOver_AndEnds()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, snapshotPadding: 200_000);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Start());
        cluster.Run(TimeSpan.FromSeconds(1));
        TestPeer late = cluster.AddClient("late", Link(300, 300));
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.Loading, "the snapshot to start");
        cluster.HostTransport.SimulateCrash();
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.Ended, "the client to give up");

        Assert.Equal(SessionEndReason.HostClosed, late.Session.EndReason);
        Assert.Null(late.Hosting);
    }
}
