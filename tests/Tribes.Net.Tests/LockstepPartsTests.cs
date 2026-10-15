using System;
using System.Collections.Generic;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class LockstepPartsTests
{
    private static readonly MatchStart Start = new()
    {
        MatchId = Guid.NewGuid(),
        Mode = RoomMode.Versus,
        TickMilliseconds = 120,
        HashIntervalTicks = 8,
        Players = [],
    };

    private static TickBatch Batch(long tick) => new(tick, []);

    [Fact]
    public void Engine_StepsOnlyWithTheNextBatch_InOrder()
    {
        var simulation = new HashChainSimulation(Start);
        var engine = new LockstepEngine(simulation);

        Assert.False(engine.TryStep(out _));
        engine.Enqueue(Batch(0));
        engine.Enqueue(Batch(1));
        engine.Enqueue(Batch(0));
        Assert.Throws<ProtocolException>(() => engine.Enqueue(Batch(5)));
        Assert.Equal(2, engine.Buffered);
        Assert.Equal(1, engine.LatestTick);

        Assert.True(engine.TryStep(out TickBatch? first));
        Assert.Equal(0, first.Tick);
        Assert.Equal(1, simulation.Tick);
        engine.ClearBuffered();
        Assert.Equal(0, engine.Buffered);
        Assert.Equal(1, engine.NextExpectedTick);
        Assert.True(LockstepEngine.IsHashTick(16, 8));
        Assert.False(LockstepEngine.IsHashTick(17, 8));
        Assert.False(LockstepEngine.IsHashTick(16, 0));
    }

    [Fact]
    public void Engine_ReportsASimulationThatDoesNotFollowTheTicks()
    {
        var lagging = new LockstepEngine(new StuckSimulation());
        lagging.Enqueue(Batch(0));
        Assert.Throws<InvalidOperationException>(() => lagging.TryStep(out _));

        var simulation = new HashChainSimulation(Start);
        var engine = new LockstepEngine(simulation);
        engine.Enqueue(Batch(0));
        simulation.Step([]);
        Assert.Throws<InvalidOperationException>(() => engine.TryStep(out _));
    }

    [Fact]
    public void Pacer_CountsTicksAtTheCurrentSpeed_AndDropsLongStalls()
    {
        var pacer = new TickPacer(100);
        TimeSpan now = TimeSpan.FromSeconds(10);
        Assert.Equal(0, pacer.Advance(now, 10));
        Assert.Equal(2, pacer.Advance(now + TimeSpan.FromMilliseconds(250), 10));
        Assert.Equal(1, pacer.Advance(now + TimeSpan.FromMilliseconds(300), 10));
        pacer.SetSpeed(200);
        Assert.Equal(TimeSpan.FromMilliseconds(50), pacer.Interval);
        Assert.Equal(4, pacer.Advance(now + TimeSpan.FromMilliseconds(500), 4));
        Assert.Equal(0, pacer.Advance(now + TimeSpan.FromMilliseconds(510), 4));
        pacer.CreditOne();
        pacer.CreditOne();
        Assert.Equal(1, pacer.Advance(now + TimeSpan.FromMilliseconds(520), 4));
        pacer.Reset(now);
        Assert.Equal(0, pacer.Advance(now + TimeSpan.FromMilliseconds(40), 4));
    }

    [Fact]
    public void InputDelay_GrowsAtOnce_AndShrinksSlowly()
    {
        TimeSpan tick = TimeSpan.FromMilliseconds(120);
        Assert.Equal(1, InputDelayController.Compute(TimeSpan.FromMilliseconds(5), TimeSpan.Zero, tick, 1, 12));
        Assert.Equal(3, InputDelayController.Compute(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(10), tick, 1, 12));
        Assert.Equal(12, InputDelayController.Compute(TimeSpan.FromSeconds(5), TimeSpan.Zero, tick, 1, 12));

        var controller = new InputDelayController(1, 12);
        TimeSpan now = TimeSpan.Zero;
        controller.Update(now, TimeSpan.FromMilliseconds(500), TimeSpan.Zero, tick);
        Assert.Equal(5, controller.Current);
        controller.Update(now + TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero, tick);
        Assert.Equal(5, controller.Current);
        controller.Update(now + TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.Zero, tick);
        Assert.Equal(4, controller.Current);
    }

    [Fact]
    public void HashLedger_ComparesRecentTicks_AndForgetsOldOnes()
    {
        var ledger = new HashLedger(2);
        ledger.Record(8, 100);
        ledger.Record(16, 200);
        ledger.Record(16, 999);
        Assert.Equal(HashCheck.Match, ledger.Check(16, 200, out _));
        Assert.Equal(HashCheck.Mismatch, ledger.Check(8, 101, out uint expected));
        Assert.Equal(100u, expected);
        ledger.Record(24, 300);
        Assert.Equal(HashCheck.Unknown, ledger.Check(8, 100, out _));
        Assert.Equal(HashCheck.Unknown, ledger.Check(32, 1, out _));
    }

    [Fact]
    public void BatchHistory_ReturnsContiguousRanges_OrNothing()
    {
        var history = new BatchHistory(3);
        for (long tick = 0; tick < 5; tick++)
        {
            history.Add(tick, [(byte)tick]);
        }

        Assert.Empty(history.Range(5, 5)!);
        Assert.Equal([new byte[] { 3 }, new byte[] { 4 }], history.Range(3, 5));
        Assert.Null(history.Range(1, 5));
        Assert.Null(history.Range(3, 7));

        var disabled = new BatchHistory(0);
        disabled.Add(0, [0]);
        Assert.Null(disabled.Range(0, 1));
    }

    [Fact]
    public void RttEstimator_SmoothsSamples()
    {
        var rtt = new RttEstimator();
        Assert.Equal(TimeSpan.Zero, rtt.Smoothed);
        rtt.AddSample(TimeSpan.FromMilliseconds(100));
        Assert.Equal(TimeSpan.FromMilliseconds(100), rtt.Smoothed);
        Assert.Equal(TimeSpan.FromMilliseconds(50), rtt.Variance);
        rtt.AddSample(TimeSpan.FromMilliseconds(180));
        Assert.Equal(TimeSpan.FromMilliseconds(110), rtt.Smoothed);
        Assert.Equal(TimeSpan.FromMilliseconds(57.5), rtt.Variance);
    }

    [Fact]
    public void HostMatch_SchedulesLateCommandsIntoTheNextBatch_AndCapsFarOnes()
    {
        var match = new HostMatch(Start, new HashChainSimulation(Start), new LockstepOptions { MaxInputDelayTicks = 2 }, new MatchLedger([]));
        Assert.Equal(RequestResult.Accepted, match.Schedule(1, -5, new byte[] { 1 }, 1));
        Assert.Equal(RequestResult.Accepted, match.Schedule(1, 1000, new byte[] { 2 }, 2));
        match.QueueSystem(PlayerCommand.System(2, CommandKind.PlayerAbsent));

        TickBatch first = match.TakeBatch(0);
        Assert.Equal([CommandKind.PlayerAbsent, CommandKind.Game], [first.Commands[0].Kind, first.Commands[1].Kind]);
        match.Engine.Enqueue(first);
        match.Engine.TryStep(out _);
        Assert.Contains(match.ScheduledCommands, c => c.Tick == 5);
        match.DropScheduled(1);
        Assert.Empty(match.ScheduledCommands);
        Assert.Null(match.MissedBatches(Guid.NewGuid(), 0));
        Assert.Null(match.MissedBatches(Start.MatchId, 99));
        (long tick, PackedSnapshot snapshot) = match.TakeSnapshot();
        Assert.Same(snapshot, match.TakeSnapshot().Snapshot);
        Assert.Equal(1, tick);
    }

    private sealed class StuckSimulation : ILockstepSimulation
    {
        public long Tick => 0;

        public void Step(IReadOnlyList<PlayerCommand> commands)
        {
        }

        public uint StateHash() => 0;

        public byte[] SaveSnapshot() => [];

        public void LoadSnapshot(byte[] data)
        {
        }
    }
}
