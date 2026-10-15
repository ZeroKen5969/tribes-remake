using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>
/// Saves are captured on the caller's thread at a tick boundary and written on a background
/// thread, one at a time, in order; results come back as notices from the runner's update.
/// </summary>
public sealed class SaveQueueTests
{
    private static readonly DateTime Fixed = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static RunnerOptions Options(ISaveStore store, bool background, int autosave = 0) => TestContent.Runner(store, autosave) with
    {
        WriteInBackground = background,
        UtcNow = () => Fixed,
    };

    private static LocalMatchRunner Start(ISaveStore store, bool background, int autosave = 0) =>
        LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8, Seed = 5 }, Options(store, background, autosave));

    [Fact]
    public void BackgroundSaves_AreByteForByteTheSynchronousOnes()
    {
        var syncStore = new MemorySaveStore();
        var asyncStore = new MemorySaveStore();
        using LocalMatchRunner sync = Start(syncStore, background: false);
        using LocalMatchRunner async = Start(asyncStore, background: true);
        var script = new ScriptedOrders(9);
        var script2 = new ScriptedOrders(9);
        for (int i = 0; i < 150; i++)
        {
            if (script.Next(sync.Simulation, 0) is { } a)
            {
                sync.Orders.Submit(a);
            }
            if (script2.Next(async.Simulation, 0) is { } b)
            {
                async.Orders.Submit(b);
            }
            sync.StepOnce();
            async.StepOnce();
        }
        Assert.NotNull(sync.Save("1"));
        Assert.NotNull(async.Save("1"));
        Assert.True(async.FlushSaves());
        Assert.Equal(syncStore.Files["custom-1"], asyncStore.Files["custom-1"]);
        Assert.Equal(Fixed, MatchSaveFile.ReadHeader(asyncStore.Files["custom-1"]).SavedAt);
    }

    [Fact]
    public void Writes_RunOneAtATime_InTheOrderAsked()
    {
        var store = new SlowStore(TimeSpan.FromMilliseconds(3));
        using LocalMatchRunner runner = Start(store, background: true);
        var asked = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            string slot = (1 + (i % 5)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            asked.Add("custom-" + slot);
            Assert.NotNull(runner.Save(slot));
            runner.StepOnce();
        }
        Assert.True(runner.FlushSaves());
        Assert.Equal(asked, store.Order);
        Assert.Equal(1, store.MostAtOnce);
        // Each file holds the state of the tick it was asked at, not a later one.
        Assert.Equal(11, MatchSaveFile.ReadHeader(store.Last["custom-2"]).Tick);
    }

    [Fact]
    public void TheSave_IsCapturedWhenAsked_NotWhenWritten()
    {
        var store = new GatedStore();
        using LocalMatchRunner runner = Start(store, background: true);
        runner.StepOnce();
        long asked = runner.Simulation.Tick;
        Assert.NotNull(runner.Save("3"));
        for (int i = 0; i < 40; i++)
        {
            runner.StepOnce();
        }
        store.Open.Set();
        Assert.True(runner.FlushSaves());
        Assert.Equal(asked, MatchSaveFile.ReadHeader(store.Files["custom-3"]).Tick);
    }

    [Fact]
    public void Notices_ComeFromTheUpdate_AfterTheFileIsWritten()
    {
        var store = new GatedStore();
        using LocalMatchRunner runner = Start(store, background: true, autosave: 5);
        // The dry run made while the match opens is not a save anyone hears of.
        Assert.True(runner.FlushSaves());
        var notices = new List<MatchNoticeKind>();
        runner.Notice += (_, e) => notices.Add(e.Notice.Kind);
        for (int i = 0; i < 5; i++)
        {
            runner.StepOnce();
        }
        Assert.Equal(1, runner.PendingSaves);
        runner.Update(0.001);
        Assert.Empty(notices);

        store.Open.Set();
        SpinWait.SpinUntil(() => runner.PendingSaves == 0, TimeSpan.FromSeconds(10));
        Assert.Empty(notices);
        runner.Update(0.001);
        Assert.Equal([MatchNoticeKind.Autosaved], notices);

        Assert.NotNull(runner.Save("4"));
        Assert.True(runner.FlushSaves());
        Assert.Equal([MatchNoticeKind.Autosaved, MatchNoticeKind.Saved], notices);
    }

    [Fact]
    public void AFailedWrite_BecomesASaveFailedNotice_AndLaterSavesStillWork()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store, background: true);
        var notices = new List<MatchNotice>();
        runner.Notice += (_, e) => notices.Add(e.Notice);
        store.Fail = true;
        Assert.NotNull(runner.Save("1"));
        Assert.False(runner.FlushSaves());
        MatchNotice failed = Assert.Single(notices);
        Assert.Equal(MatchNoticeKind.SaveFailed, failed.Kind);
        Assert.Equal("disk full", failed.Detail);

        store.Fail = false;
        Assert.NotNull(runner.Save("1"));
        Assert.True(runner.FlushSaves());
        Assert.Equal(MatchNoticeKind.Saved, notices[^1].Kind);
        Assert.True(store.Files.ContainsKey("custom-1"));
    }

    [Fact]
    public void Quit_WaitsForThePendingWrites()
    {
        var store = new SlowStore(TimeSpan.FromMilliseconds(30));
        LocalMatchRunner runner = Start(store, background: true);
        runner.Save("1");
        runner.Save("2");
        runner.Quit();
        Assert.Equal(["custom-1", "custom-2", "custom-auto"], store.Order);
        Assert.Equal(0, runner.PendingSaves);
        runner.Dispose();
        Assert.True(runner.FlushSaves());
    }

    [Fact]
    public void HostedMatch_SavesInTheBackgroundToo()
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 61, autosaveTicks: 100) { BackgroundSaves = true };
        cluster.AddClient("Guest");
        cluster.StartMatch(new MatchPlan { Mission = 8, Seed = 61 });
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(250);
        Assert.True(cluster.HostPeer.Runner.FlushSaves());
        Assert.Equal(2, cluster.HostPeer.Notices.Count(n => n.Kind == MatchNoticeKind.Autosaved));
        SaveHeader header = MatchSaveFile.ReadHeader(cluster.Store.Files["versus-auto"]);
        Assert.Equal(SaveKind.HostedMatch, header.Kind);
        Assert.InRange(header.Tick, 200, 210);
    }

    /// <summary>A store that takes a while per write and checks that writes never overlap.</summary>
    private sealed class SlowStore(TimeSpan delay) : ISaveStore
    {
        private int inside;

        public List<string> Order { get; } = [];

        public Dictionary<string, byte[]> Last { get; } = [];

        public int MostAtOnce { get; private set; }

        public void Write(SaveSlot slot, byte[] data)
        {
            int now = Interlocked.Increment(ref inside);
            MostAtOnce = Math.Max(MostAtOnce, now);
            Thread.Sleep(delay);
            lock (Order)
            {
                Order.Add(slot.Stem);
                Last[slot.Stem] = data;
            }
            Interlocked.Decrement(ref inside);
        }

        public void WritePicture(SaveSlot slot, byte[] png)
        {
        }
    }

    /// <summary>A store whose writes wait until the test opens the gate.</summary>
    private sealed class GatedStore : ISaveStore
    {
        public ManualResetEventSlim Open { get; } = new(false);

        public Dictionary<string, byte[]> Files { get; } = [];

        public void Write(SaveSlot slot, byte[] data)
        {
            if (!Open.Wait(TimeSpan.FromSeconds(20)))
            {
                throw new IOException("the gate never opened");
            }
            lock (Files)
            {
                Files[slot.Stem] = data;
            }
        }

        public void WritePicture(SaveSlot slot, byte[] png)
        {
        }
    }
}
