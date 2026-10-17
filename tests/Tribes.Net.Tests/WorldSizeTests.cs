using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>
/// Worlds too large to send (<see cref="LockstepOptions.MaxSnapshotBytes"/>): the player who needs the
/// world is told why, so is the host, and the match goes on; and a large world's progress as it comes in.
/// </summary>
public sealed class WorldSizeTests
{
    private const int Padding = 300_000, Limit = 100_000;

    [Fact]
    public void AWorldTooLargeToSend_TurnsTheJoinerDown_AndTellsTheHost()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, Limited, snapshotPadding: Padding);
        TestPeer first = cluster.AddClient("first");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        TestPeer late = cluster.AddClient("late");
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.Ended, "the late player to be turned down");
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        SessionEndedEventArgs end = Assert.Single(late.Ends);
        Assert.Equal((SessionEndReason.Rejected, RejectReason.WorldTooLarge), (end.Reason, end.Rejection));
        Assert.Null(late.Session.Simulation);
        WorldTooLargeEventArgs told = Assert.Single(cluster.HostWorldTooLarge);
        Assert.Equal("late", told.PlayerName);
        Assert.Equal(Limit, told.LimitBytes);
        Assert.True(told.WorldBytes > Limit);
        Assert.DoesNotContain(cluster.Host.Room.Players, p => p.Name == "late");
        Assert.Equal(ClientPhase.InGame, first.Session.Phase);
        LoopbackCluster.AssertInSync(10, cluster.HostSimulation, first.Simulation);
    }

    [Fact]
    public void AStateThatCannotBeSentAgain_LetsThePlayerGo_WithTheReason()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, Limited, snapshotPadding: Padding);
        TestPeer good = cluster.AddClient("good");
        TestPeer bad = cluster.AddClient("bad");
        cluster.StartMatch();
        bad.Simulation.CorruptAtTick = 20;

        cluster.RunWithCommands(TimeSpan.FromSeconds(8));

        Assert.Single(cluster.HostDesyncs);
        Assert.Equal(SessionEndReason.WorldTooLarge, Assert.Single(bad.Ends).Reason);
        Assert.Equal("bad", Assert.Single(cluster.HostWorldTooLarge).PlayerName);
        Assert.Equal(PlayerPresence.Absent, cluster.Host.Room.Players.Single(p => p.Name == "bad").Presence);
        Assert.Equal(ClientPhase.InGame, good.Session.Phase);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, good.Simulation);
    }

    [Fact]
    public void AWorldWithinTheLimit_ComesIn_WithItsProgressShown()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, snapshotPadding: Padding);
        cluster.AddClient("first");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        // Jitter spreads the pieces over several frames, so the world is seen half there.
        TestPeer late = cluster.AddClient("late", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(40), Jitter = TimeSpan.FromMilliseconds(60) });
        cluster.RunUntil(() => late.Session.SnapshotDownload is { ChunksReceived: > 0 }, "the first pieces of the world");
        SnapshotProgress progress = late.Session.SnapshotDownload!;
        Assert.InRange(progress.Fraction, 0.01, 0.99);
        Assert.True(progress.WorldBytes > Padding);

        cluster.RunUntil(() => late.Session.Phase == ClientPhase.InGame, "the late player to load");
        Assert.Null(late.Session.SnapshotDownload);
        Assert.Empty(cluster.HostWorldTooLarge);
    }

    [Fact]
    public void TheLimit_LetsAWorldOfAQuarterGigabyteThrough()
    {
        Assert.Equal(256 * 1024 * 1024, NetProtocol.MaxSnapshotBytes);
        Assert.Equal(NetProtocol.MaxSnapshotBytes, new LockstepOptions().MaxSnapshotBytes);
    }

    private static HostOptions Limited(HostOptions options) =>
        options with { Lockstep = new LockstepOptions { MaxSnapshotBytes = Limit } };
}
