using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class MatchSyncTests
{
    private static LoopbackLinkOptions Link(int latencyMs, int jitterMs) => new()
    {
        Latency = TimeSpan.FromMilliseconds(latencyMs),
        Jitter = TimeSpan.FromMilliseconds(jitterMs),
    };

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 80, 40)]
    [InlineData(2, 40, 30)]
    [InlineData(3, 120, 60)]
    public void AllPeers_StayInSync_UnderLatencyAndJitter(int clientCount, int latencyMs, int jitterMs)
    {
        using var cluster = new LoopbackCluster(seed: clientCount * 31 + latencyMs);
        for (int i = 0; i < clientCount; i++)
        {
            cluster.AddClient($"p{i}", Link(latencyMs + (i * 15), jitterMs));
        }

        cluster.StartMatch(new byte[] { 1, 2, 3 });
        cluster.RunWithCommands(TimeSpan.FromSeconds(12));
        cluster.Run(TimeSpan.FromSeconds(2));

        HashChainSimulation[] all = [cluster.HostSimulation, .. cluster.Clients.Select(c => c.Simulation)];
        LoopbackCluster.AssertInSync(80, all);
        Assert.Empty(cluster.HostDesyncs);
        Assert.All(cluster.Clients, c => Assert.Empty(c.Desyncs));
        Assert.True(cluster.HostSimulation.Log.Count(e => !e.Command.IsSystem) > 50, "Commands should have run.");
        Assert.All(all, s => Assert.Equal(cluster.HostSimulation.Log.Count, s.Log.Count));
    }

    [Fact]
    public void Simulation_RunsAtTheConfiguredTickRate()
    {
        using var cluster = new LoopbackCluster();
        TestPeer client = cluster.AddClient("a");
        cluster.StartMatch();
        long before = cluster.HostSimulation.Tick;

        cluster.Run(TimeSpan.FromSeconds(12));

        // 120 ms per tick: 100 ticks in 12 seconds.
        Assert.InRange(cluster.HostSimulation.Tick - before, 99, 101);
        Assert.InRange(client.Simulation.Tick, cluster.HostSimulation.Tick - 2, cluster.HostSimulation.Tick);
    }

    [Fact]
    public void ClientThatStalled_CatchesUpInOneUpdate()
    {
        using var cluster = new LoopbackCluster();
        TestPeer slow = cluster.AddClient("slow", Link(20, 0));
        cluster.StartMatch();
        cluster.Run(TimeSpan.FromSeconds(1));

        // The client's game thread stalls for two seconds while the host goes on.
        for (int frame = 0; frame < 200; frame++)
        {
            cluster.Clock.Advance(LoopbackCluster.Frame);
            cluster.Host.Update();
        }

        long before = slow.Simulation.Tick;
        slow.Session.Update();

        Assert.True(slow.Simulation.Tick - before >= 15, "The stalled client should run the missed ticks at once.");
        Assert.InRange(slow.Simulation.Tick, cluster.HostSimulation.Tick - 1, cluster.HostSimulation.Tick);
        cluster.Run(TimeSpan.FromSeconds(1));
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, slow.Simulation);
    }

    [Fact]
    public void BurstOfBatches_LeavesNoStandingBacklog()
    {
        using var cluster = new LoopbackCluster();
        TestPeer client = cluster.AddClient("client", Link(20, 0));
        cluster.StartMatch();
        cluster.Run(TimeSpan.FromSeconds(1));

        // The host stalls for 600 ms, then sends the missed batches in one burst.
        for (int frame = 0; frame < 60; frame++)
        {
            cluster.Clock.Advance(LoopbackCluster.Frame);
            client.Session.Update();
        }

        cluster.Run(TimeSpan.FromMilliseconds(150));
        Assert.True(client.Session.BufferedTicks >= 2, "The burst should leave batches waiting.");
        cluster.Run(TimeSpan.FromSeconds(5));

        Assert.False(client.Session.IsCatchingUp);
        Assert.True(client.Session.BufferedTicks <= 1, $"A standing backlog of {client.Session.BufferedTicks} batches should have drained.");
        Assert.InRange(client.Simulation.Tick, cluster.HostSimulation.Tick - 1, cluster.HostSimulation.Tick);
        LoopbackCluster.AssertInSync(40, cluster.HostSimulation, client.Simulation);
    }

    [Fact]
    public void InputDelay_AdaptsToTheRoundTrip()
    {
        using var cluster = new LoopbackCluster();
        TestPeer near = cluster.AddClient("near");
        TestPeer far = cluster.AddClient("far", Link(200, 20));
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        Assert.Equal(1, near.Session.InputDelayTicks);
        Assert.InRange(far.Session.RoundTripTime.TotalMilliseconds, 380, 480);
        Assert.InRange(far.Session.InputDelayTicks, 4, 6);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, near.Simulation, far.Simulation);
    }

    [Fact]
    public void Commands_RunAtTheSameTickOnEveryPeer_InHostOrder()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a", Link(30, 10));
        TestPeer b = cluster.AddClient("b", Link(60, 10));
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(6), chancePerFrame: 0.4);
        cluster.Run(TimeSpan.FromSeconds(1));

        var hostLog = cluster.HostSimulation.Log.Select(e => (e.Tick, e.Command.Slot, Payload: Convert.ToHexString(e.Command.Payload.Span))).ToList();
        Assert.Equal(hostLog, a.Simulation.Log.Select(e => (e.Tick, e.Command.Slot, Payload: Convert.ToHexString(e.Command.Payload.Span))));
        Assert.Equal(hostLog, b.Simulation.Log.Select(e => (e.Tick, e.Command.Slot, Payload: Convert.ToHexString(e.Command.Payload.Span))));
        Assert.Contains(hostLog, e => e.Slot == a.Session.LocalPlayer!.Slot);
        Assert.Contains(hostLog, e => e.Slot == b.Session.LocalPlayer!.Slot);
    }

    [Fact]
    public void Desync_IsDetected_AtTheFirstDivergingCheck_AndRepaired()
    {
        using var cluster = new LoopbackCluster();
        TestPeer good = cluster.AddClient("good");
        TestPeer bad = cluster.AddClient("bad", Link(20, 5));
        cluster.StartMatch();
        bad.Simulation.CorruptAtTick = 20;

        cluster.RunWithCommands(TimeSpan.FromSeconds(8));

        DesyncDetectedEventArgs report = Assert.Single(cluster.HostDesyncs);
        Assert.Equal(bad.PublicId, report.PlayerId);
        Assert.Equal(24, report.Tick);
        Assert.Equal(16, report.LastMatchingTick);
        Assert.NotEqual(report.ExpectedHash, report.ReportedHash);
        DesyncDetectedEventArgs notice = Assert.Single(bad.Desyncs);
        Assert.Equal(24, notice.Tick);
        Assert.Empty(good.Desyncs);

        // The host resynchronised the peer from a snapshot; from then on it agrees again.
        Assert.Equal(2, bad.Simulations.Count);
        Assert.True(bad.MatchStarts[^1].FromSnapshot);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, good.Simulation, bad.Simulation);
    }

    [Fact]
    public void Desync_WithoutResync_IsReportedOnce()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Lockstep = new LockstepOptions { ResyncOnDesync = false, HashIntervalTicks = 4 } });
        TestPeer bad = cluster.AddClient("bad");
        cluster.StartMatch();
        bad.Simulation.CorruptAtTick = 10;

        cluster.Run(TimeSpan.FromSeconds(5));

        DesyncDetectedEventArgs report = Assert.Single(cluster.HostDesyncs);
        Assert.Equal(12, report.Tick);
        Assert.Equal(8, report.LastMatchingTick);
        Assert.Single(bad.Simulations);
    }

    [Fact]
    public void Pause_StopsEveryPeerAtTheSameTick_AndResumeContinues()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a", Link(50, 20));
        TestPeer b = cluster.AddClient("b", Link(10, 0));
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Equal(RequestResult.Accepted, cluster.Host.Pause());
        cluster.Run(TimeSpan.FromSeconds(2));
        long pausedAt = cluster.HostSimulation.Tick;
        cluster.Run(TimeSpan.FromSeconds(2));

        Assert.True(cluster.Host.IsPaused);
        Assert.True(a.Session.IsPaused && b.Session.IsPaused);
        Assert.Equal(pausedAt, cluster.HostSimulation.Tick);
        Assert.Equal(pausedAt, a.Simulation.Tick);
        Assert.Equal(pausedAt, b.Simulation.Tick);

        Assert.Equal(RequestResult.Accepted, a.Session.RequestResume());
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        Assert.False(cluster.Host.IsPaused);
        Assert.True(cluster.HostSimulation.Tick > pausedAt + 10);

        Assert.Equal(RequestResult.Accepted, b.Session.RequestPause());
        cluster.Run(TimeSpan.FromSeconds(1));
        Assert.True(cluster.Host.IsPaused);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Resume());
        cluster.Run(TimeSpan.FromSeconds(1));
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, a.Simulation, b.Simulation);
    }

    [Fact]
    public void ClientPause_IsDenied_WhenTheHostDoesNotAllowIt()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Room = o.Room with { AllowClientPause = false } });
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();

        Assert.Equal(RequestResult.Accepted, a.Session.RequestPause());
        cluster.Run(TimeSpan.FromMilliseconds(200));

        Assert.False(cluster.Host.IsPaused);
        RequestDeniedEventArgs denied = Assert.Single(a.Denials);
        Assert.Equal(RequestKind.Pause, denied.Request);
    }

    [Fact]
    public void SpeedChange_IsDecidedByTheHost_AndAppliesToEveryPeer()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();

        Assert.Equal(RequestResult.Accepted, a.Session.RequestSpeed(200));
        cluster.Run(TimeSpan.FromMilliseconds(200));
        SpeedRequestedEventArgs request = Assert.Single(cluster.SpeedRequests);
        Assert.Equal(200, request.SpeedPercent);
        Assert.Equal(a.PublicId, request.PlayerId);
        Assert.Equal(100, cluster.Host.SpeedPercent);

        Assert.Equal(RequestResult.Accepted, cluster.Host.SetSpeed(request.SpeedPercent));
        cluster.Run(TimeSpan.FromMilliseconds(200));
        Assert.Equal(200, a.Session.SpeedPercent);
        long before = cluster.HostSimulation.Tick;
        cluster.Run(TimeSpan.FromSeconds(6));

        // Twice the normal rate: 100 ticks in 6 seconds.
        Assert.InRange(cluster.HostSimulation.Tick - before, 98, 102);
        Assert.InRange(a.Simulation.Tick, cluster.HostSimulation.Tick - 3, cluster.HostSimulation.Tick);
        Assert.Equal(RequestResult.InvalidValue, cluster.Host.SetSpeed(1000));
        Assert.Equal(RequestResult.InvalidValue, a.Session.RequestSpeed(5));
    }

    [Fact]
    public void SpeedRequests_ApplyAtOnce_WhenTheHostAcceptsThem()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Room = o.Room with { AcceptSpeedRequests = true } });
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();

        a.Session.RequestSpeed(50);
        cluster.Run(TimeSpan.FromMilliseconds(300));

        Assert.Equal(50, cluster.Host.SpeedPercent);
        Assert.Equal(50, a.Session.SpeedPercent);
    }

    [Fact]
    public void LargeSnapshot_IsSentInChunks_ToAPlayerJoiningLate()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, snapshotPadding: 300_000);
        TestPeer first = cluster.AddClient("first");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        TestPeer late = cluster.AddClient("late", Link(40, 10));
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.InGame, "the late player to load");
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.True(late.MatchStarts.Single().FromSnapshot);
        LoopbackCluster.AssertInSync(25, cluster.HostSimulation, first.Simulation, late.Simulation);
    }

    [Fact]
    public void HostCommands_AreRejected_OutsideAMatch()
    {
        using var cluster = new LoopbackCluster();
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.SendCommand([1]));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.Pause());
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.SetSpeed(150));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.Forfeit());
        Assert.Throws<ArgumentException>(() => cluster.Host.SendCommand(new byte[5000]));
        Assert.Throws<InvalidOperationException>(() => cluster.Host.SaveMatch());
    }

    [Fact]
    public void CommandsOverATicksShare_GoIntoTheNextTicks_InOrder()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Lockstep = new LockstepOptions { MaxCommandsPerPlayerPerTick = 2 } });
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();

        // A burst, as a client sends again after a reconnect: two per tick, in the order sent.
        for (int i = 0; i < 7; i++)
        {
            Assert.True(a.Session.SendCommand([(byte)i]));
        }

        cluster.Run(TimeSpan.FromSeconds(2));

        Assert.Empty(cluster.RejectedCommands);
        int slot = a.Session.LocalPlayer!.Slot;
        var ran = cluster.HostSimulation.Log.Where(e => e.Command.Slot == slot && !e.Command.IsSystem).ToList();
        Assert.Equal(Enumerable.Range(0, 7).Select(i => (byte)i), ran.Select(e => e.Command.Payload.Span[0]));
        Assert.All(ran.GroupBy(e => e.Tick), tick => Assert.InRange(tick.Count(), 1, 2));
        Assert.Equal(4, ran.Select(e => e.Tick).Distinct().Count());
    }

    [Fact]
    public void CommandsBeyondTheWholeWindow_AreRejected()
    {
        var lockstep = new LockstepOptions { MaxCommandsPerPlayerPerTick = 2 };
        using var cluster = new LoopbackCluster(configure: o => o with { Lockstep = lockstep });
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();
        Assert.Equal(RequestResult.Accepted, cluster.Host.Pause());
        cluster.Run(TimeSpan.FromMilliseconds(300));

        // The host's own commands aim one tick ahead; the latest tick is 2 x the largest input delay + 1 ahead.
        int window = 2 * lockstep.MaxInputDelayTicks + 1;
        int accepted = 0;
        while (cluster.Host.SendCommand([9]) == RequestResult.Accepted)
        {
            accepted++;
            Assert.True(accepted <= 1000, "The host should stop taking commands.");
        }

        Assert.Equal(2 * window, accepted);
        Assert.Equal(RequestResult.TooManyCommands, cluster.Host.SendCommand([9]));
        int sent = 2 * (window + 1) + 3;
        for (int i = 0; i < sent; i++)
        {
            Assert.True(a.Session.SendCommand([(byte)i]));
        }

        cluster.Run(TimeSpan.FromSeconds(1));
        Assert.InRange(cluster.RejectedCommands.Count, 3, sent);
        Assert.All(cluster.RejectedCommands, r => Assert.Equal(RequestResult.TooManyCommands, r.Result));
        Assert.Equal(cluster.RejectedCommands.Count, a.Denials.Count(d => d.Request == RequestKind.Command));
    }
}
