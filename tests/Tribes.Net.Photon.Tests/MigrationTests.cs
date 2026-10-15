using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>
/// Host migration over Photon: the host leaves or its device drops out mid-match, another peer
/// takes over the room (compare-and-swap of the room property h) and the match goes on.
/// </summary>
public sealed class MigrationTests
{
    /// <summary>Three players besides the host; a has the lowest slot, so it is next in line.</summary>
    private static (PhotonCluster Cluster, PhotonNode A, PhotonNode B, PhotonNode C) Match(int latencyA = 20, int latencyB = 40, int latencyC = 60, int seed = 1)
    {
        var cluster = new PhotonCluster(RoomMode.Endless, seed: seed + latencyA + latencyB + latencyC);
        PhotonNode a = cluster.AddClient("a", latencyA);
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b", latencyB);
        cluster.RunUntilJoined(b);
        PhotonNode c = cluster.AddClient("c", latencyC);
        cluster.StartMatch([1]);
        Assert.Equal([1, 2, 3], new[] { a, b, c }.Select(p => p.Session.LocalPlayer!.Slot));
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        return (cluster, a, b, c);
    }

    private static void AssertEveryCommandRanOnce(HashChainSimulation finalHost, params PhotonNode[] clients)
    {
        foreach (PhotonNode client in clients)
        {
            int slot = client.Session.LocalPlayer!.Slot;
            long[] ran = [.. finalHost.Log
                .Where(e => e.Command.Slot == slot && !e.Command.IsSystem)
                .Select(e => e.Command.Sequence)];
            Assert.Equal(Enumerable.Range(1, client.CommandsSent).Select(i => (long)i), ran.Order());
        }
    }

    private static void RunUntilFollowing(PhotonCluster cluster, HostSession oldHost, params PhotonNode[] followers) =>
        cluster.RunUntil(
            () => cluster.Host != oldHost && followers.All(f => f.Session.Phase == ClientPhase.InGame),
            "the match to move to the next host",
            TimeSpan.FromSeconds(30));

    private static void Settle(PhotonCluster cluster) => cluster.Run(TimeSpan.FromSeconds(2));

    [Fact]
    public void HostThatLeaves_HandsTheMatchToTheNextPlayer()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        HashChainSimulation oldSimulation = cluster.HostSimulation;
        long tickBefore = oldSimulation.Tick;
        TimeSpan leftAt = cluster.Clock.Now;

        oldHost.Leave();
        Assert.Equal("actor:2", oldHost.SuccessorAddress);
        RunUntilFollowing(cluster, oldHost, b, c);
        TimeSpan migration = cluster.Clock.Now - leftAt;
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.True(migration < TimeSpan.FromSeconds(1), $"Taking over took {migration}.");
        Assert.Equal(TransportRole.Host, a.Transport.Role);
        Assert.Equal(cluster.RoomAddress, a.Transport.HostAddress);
        Assert.Equal([a.PublicId], b.HostChanges);
        Assert.Equal([a.PublicId], c.HostChanges);
        Assert.True(cluster.HostSimulation.Tick > tickBefore + 25, "The match should keep running.");
        Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.PlayerInSlot(0)!.Presence);
        Assert.Empty(cluster.HostDesyncs);
        PhotonCluster.AssertInSync(25, oldSimulation, a.Simulation, b.Simulation, c.Simulation);
        PhotonCluster.AssertInSync(40, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
        Assert.Equal(2, cluster.Cloud.Rooms.Values.Single().Master);
    }

    [Fact]
    public void HostThatCrashes_IsReplaced_AndNoCommandIsLostOrRunTwice()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        Assert.True(b.Session.SendCommand([0xB1]));
        Assert.True(c.Session.SendCommand([0xC1]));
        b.CommandsSent++;
        c.CommandsSent++;
        cluster.Run(TimeSpan.FromMilliseconds(30));

        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.Empty(cluster.HostDesyncs);
        Assert.All([b, c], p => Assert.Empty(p.Desyncs));
        PhotonCluster.AssertInSync(40, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
        Assert.Contains(a.Simulation.Log, e => e.Command.Payload.Span.SequenceEqual(new byte[] { 0xB1 }));
    }

    [Fact]
    public void SuccessorFarBehind_TakesOver_WithEveryoneInSync()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match(latencyA: 300, latencyB: 10, latencyC: 10);
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        // Photon relays what reached its server, so the far successor gets every batch the others
        // got: nobody has to start over from a snapshot.
        Assert.Same(a.Hosting, cluster.Host);
        Assert.All([b, c], p => Assert.Single(p.Simulations));
        Assert.Empty(cluster.HostDesyncs);
        PhotonCluster.AssertInSync(25, a.Simulation, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
    }

    [Fact]
    public void CommandsSentWhileTheHostIsGone_RunExactlyOnce_UnderTheNewHost()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.HostNode.Crash();

        // The players keep giving orders (many a second) until Photon notices the host is gone
        // and somebody hosts again; afterwards they all arrive at once.
        while (cluster.Host == oldHost)
        {
            cluster.RunWithCommands(TimeSpan.FromMilliseconds(100), chancePerFrame: 1.0);
            Assert.True(cluster.Clock.Now < TimeSpan.FromMinutes(2), "Nobody took over.");
        }

        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        Settle(cluster);

        Assert.All([b, c], p => Assert.True(p.CommandsSent > 150, $"{p.Name} sent only {p.CommandsSent}."));
        Assert.All([a, b, c], p => Assert.DoesNotContain(p.Session.Room!.Players, x => x.Presence == PlayerPresence.Forfeited));
        AssertEveryCommandRanOnce(a.Simulation, b, c);
        PhotonCluster.AssertInSync(30, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void HostThatCrashesRightAfterAHashCheck_LeavesNoFalseDesync()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.RunUntil(() => cluster.HostSimulation.Tick % 8 == 1, "a hash check to run");
        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Empty(cluster.HostDesyncs);
        Assert.All([a, b, c], p => Assert.Empty(p.Desyncs));
        PhotonCluster.AssertInSync(30, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void OldHost_CanRejoin_AsAnOrdinaryPlayer()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        oldHost.Leave();
        RunUntilFollowing(cluster, oldHost, b, c);

        PhotonNode back = cluster.JoinAs(cluster.HostNode, cluster.RoomAddress);
        cluster.RunUntil(() => back.Session.Phase == ClientPhase.InGame, "the old host to rejoin");
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        PlayerInfo rejoined = back.Session.LocalPlayer!;
        Assert.Equal(0, rejoined.Slot);
        Assert.False(rejoined.IsHost);
        Assert.Equal(PlayerPresence.Active, rejoined.Presence);
        Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned && p.Slot == 0);
        PhotonCluster.AssertInSync(25, a.Simulation, b.Simulation, c.Simulation, back.Simulation);
    }

    [Fact]
    public void MatchSurvives_ASecondHostChange()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession first = cluster.Host;
        first.Leave();
        RunUntilFollowing(cluster, first, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        HostSession second = cluster.Host;
        a.Crash();
        RunUntilFollowing(cluster, second, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Same(b.Hosting, cluster.Host);
        Assert.Equal([a.PublicId, b.PublicId], c.HostChanges);
        Assert.All([0, 1], slot => Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.PlayerInSlot(slot)!.Presence));
        Assert.Empty(cluster.HostDesyncs);
        PhotonCluster.AssertInSync(40, b.Simulation, c.Simulation);
        Settle(cluster);
        AssertEveryCommandRanOnce(b.Simulation, c);
        Assert.Equal(3, cluster.Cloud.Rooms.Values.Single().Properties[Wire.HostProperty]);
    }

    [Fact]
    public void Followers_ThatHearFirst_WaitForTheSuccessor()
    {
        // The successor is the last to hear that the host is gone.
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match(latencyA: 250, latencyB: 10, latencyC: 10);
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        oldHost.Leave();
        RunUntilFollowing(cluster, oldHost, b, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Same(a.Hosting, cluster.Host);
        PhotonCluster.AssertInSync(15, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void SuccessorThatNeverAnswers_IsSkipped_AfterTheMigrationTimeout()
    {
        var cluster = new PhotonCluster(RoomMode.Endless, seed: 9);
        using PhotonCluster owned = cluster;
        Func<ClientOptions, ClientOptions> quick = o => o with { MigrationTimeout = TimeSpan.FromSeconds(2) };
        PhotonNode a = cluster.AddClient("a", 10, quick);
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b", 20, quick);
        cluster.RunUntilJoined(b);
        PhotonNode c = cluster.AddClient("c", 30, quick);
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        HostSession oldHost = cluster.Host;

        // a's game hangs: its device stays in the room, but its session never runs again.
        a.Frozen = true;
        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Same(b.Hosting, cluster.Host);
        Assert.Equal([b.PublicId], c.HostChanges);
        PhotonCluster.AssertInSync(15, b.Simulation, c.Simulation);
    }

    [Fact]
    public void ForfeitTheOldHostHadNotSentYet_IsKeptByTheNewHost()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        Assert.Equal(RequestResult.Accepted, oldHost.Pause());
        cluster.Run(TimeSpan.FromMilliseconds(300));
        Assert.Equal(RequestResult.Accepted, c.Session.Forfeit());
        cluster.RunUntil(() => a.Session.Room!.FindPlayer(c.PublicId)!.Presence == PlayerPresence.Forfeited, "the forfeit to show");

        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, b, c);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Resume());
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Contains(3, a.Simulation.ForfeitedSlots);
        Assert.Contains(3, b.Simulation.ForfeitedSlots);
        Assert.False(c.Session.CanSendCommands);
        PhotonCluster.AssertInSync(5, a.Simulation, b.Simulation, c.Simulation);
    }

    [Fact]
    public void Spectator_TakesOver_WhenNoPlayerIsLeft()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless);
        PhotonNode watcher = cluster.AddClient("watcher", configure: o => o with { Spectate = true });
        cluster.StartMatch();
        cluster.Run(TimeSpan.FromSeconds(1));
        HostSession oldHost = cluster.Host;

        cluster.HostNode.Crash();
        cluster.RunUntil(() => cluster.Host != oldHost, "the watcher to take over", TimeSpan.FromSeconds(30));
        long tick = cluster.HostSimulation.Tick;
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Same(watcher.Hosting, cluster.Host);
        Assert.False(cluster.Host.LocalPlayer.HasSlot);
        Assert.True(cluster.HostSimulation.Tick > tick + 5);
    }

    [Fact]
    public void SuccessorThatLosesItsOwnLink_WhileTakingOver_IsReplacedByTheNext()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        a.Link.LoseNetwork();
        a.Frozen = true;
        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, c);
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Same(b.Hosting, cluster.Host);
        PhotonCluster.AssertInSync(15, b.Simulation, c.Simulation);
    }

    [Fact]
    public void TakeOver_ThatLosesTheRace_FollowsTheWinner()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        cluster.HostNode.Crash();
        RunUntilFollowing(cluster, oldHost, c);

        var state = new SuccessorState
        {
            Transport = b.Transport,
            Options = new ClientOptions { GameVersion = PhotonCluster.GameVersion, ContentVersion = PhotonCluster.ContentVersion, PlayerId = b.Id, PlayerName = "b" },
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

        // b is late: a already hosts the room, so b connects to it instead.
        Assert.Null(HostSession.TakeOver(state, out string? winner));
        Assert.Equal("actor:2", winner);
        Assert.Equal(TransportRole.Client, b.Transport.Role);
    }
}
