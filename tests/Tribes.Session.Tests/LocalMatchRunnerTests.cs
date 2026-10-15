using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

public sealed class LocalMatchRunnerTests
{
    private const double Tick = LocalMatchRunner.TickMilliseconds / 1000.0;
    private const int DefeatShown = 90;

    private static LocalMatchRunner Start(MemorySaveStore? store = null, int autosave = 1500, MatchKind kind = MatchKind.Custom, int mission = 8) =>
        LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup
        {
            Match = kind,
            Mission = mission,
            Difficulty = 1,
            Seed = 99,
            PlayerName = "Solo",
            Colour = 4,
            ComputerColour = 6,
        }, TestContent.Runner(store, autosave));

    private static MoveUnits Move(IMatchRunner runner)
    {
        GameWorld world = runner.Simulation.World;
        int tribe = runner.LocalTribe;
        int unit = world.UnitsOf(tribe).Select(u => u.Id).First(u => world.Unit(u).InUse);
        return new MoveUnits(tribe, UnitSet.Of(unit), new GridPoint(world.Unit(unit).X + 1, world.Unit(unit).Y + 1), 0, false);
    }

    [Fact]
    public void Start_SetsUpTheLocalPlayerAndTheComputer()
    {
        using LocalMatchRunner runner = Start();
        Assert.False(runner.IsNetwork);
        Assert.True(runner.IsHost);
        Assert.Equal(MatchKind.Custom, runner.Match);
        Assert.Equal(MatchPhase.Running, runner.Phase);
        Assert.Equal(LocalRole.Player, runner.Role);
        Assert.Equal(0, runner.LocalTribe);
        Assert.Equal(0, runner.ViewTribe);
        Assert.Equal(LocalMatchRunner.LocalId, runner.LocalPlayerId);
        Assert.Equal((4, "Solo"), (runner.Players.ById(LocalMatchRunner.LocalId)!.Colour, runner.Players.ById(LocalMatchRunner.LocalId)!.Name));
        PlayerEntry computer = runner.Players.ById(LocalMatchRunner.ComputerId)!;
        Assert.Equal((true, 1, 6, 1), (computer.IsComputer, computer.Tribe, computer.Colour, computer.AiLevel));
        Assert.True(runner.Simulation.Options.HintsPauseGame);
        Assert.True(runner.CanPause);
        Assert.True(runner.CanChangeSpeed);
        Assert.Equal(1, runner.Simulation.World.Difficulty);
    }

    [Fact]
    public void Start_RejectsVersusAndUnknownMissions()
    {
        Assert.Throws<ArgumentException>(() => Start(kind: MatchKind.Versus));
        Assert.Throws<ArgumentException>(() => Start(mission: 55));
        Assert.Throws<ArgumentNullException>(() => LocalMatchRunner.Start(null!, new LocalMatchSetup { Match = MatchKind.Custom }, TestContent.Runner()));
        Assert.Throws<ArgumentNullException>(() => LocalMatchRunner.Start(TestContent.Content, null!, TestContent.Runner()));
    }

    [Fact]
    public void Start_Endless_UsesTheFirstCustomMap_AndTutorialFlags()
    {
        using LocalMatchRunner endless = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Endless }, TestContent.Runner());
        Assert.Equal(Tribes.Core.Systems.Missions.MissionStart.FirstCustomMission, endless.Simulation.World.Mission);
        using LocalMatchRunner tutorial = LocalMatchRunner.Start(TestContent.Content,
            new LocalMatchSetup { Match = MatchKind.Campaign, Mission = 0, Tutorial = true, TutorialScenesSeen = 0b1110 }, TestContent.Runner());
        Assert.Equal(0, tutorial.Simulation.World.Mission);
    }

    [Fact]
    public void Update_RunsTicksAtTheFixedRate_WithSpeedAndStallLimit()
    {
        using LocalMatchRunner runner = Start();
        runner.Update(Tick * 3.5);
        Assert.Equal(3, runner.Simulation.Tick);
        Assert.InRange(runner.TickAlpha, 0.49, 0.51);
        Assert.Equal(RequestResult.Accepted, runner.SetSpeed(200));
        Assert.Equal(Tick / 2, runner.TickSeconds, 6);
        runner.Update(Tick);
        Assert.Equal(5, runner.Simulation.Tick);
        Assert.Equal(RequestResult.InvalidValue, runner.SetSpeed(1000));
        Assert.Equal(200, runner.SpeedPercent);
        runner.Update(60);
        Assert.Equal(5 + 8, runner.Simulation.Tick);
        Assert.Equal(0, runner.TickAlpha);
        runner.Update(0);
        runner.Update(-1);
        Assert.Equal(13, runner.Simulation.Tick);
    }

    [Fact]
    public void Orders_RunOnTheNextTick_AndReportTheirOutcomes()
    {
        using LocalMatchRunner runner = Start();
        var ticks = new List<MatchTickEventArgs>();
        runner.TickRan += (_, e) => ticks.Add(e);
        MoveUnits order = Move(runner);

        Assert.True(runner.Orders.CanSubmit);
        Assert.Equal(0, runner.Orders.Tribe);
        Assert.Equal(OrderSubmitResult.Queued, runner.Orders.Submit(order));
        Assert.Equal(OrderSubmitResult.WrongTribe, runner.Orders.Submit(order with { Tribe = 1 }));
        runner.Update(Tick);

        MatchTickEventArgs tick = Assert.Single(ticks);
        Assert.Same(order, Assert.Single(tick.LocalOrders));
        Assert.Equal(OrderOutcome.Applied, Assert.Single(tick.Result.Outcomes));
        Assert.Empty(tick.Changes);
        Assert.Throws<ArgumentNullException>(() => runner.Orders.Submit(null!));
    }

    [Fact]
    public void Held_TicksStillApplyOrders_ButTheWorldStands()
    {
        using LocalMatchRunner runner = Start();
        var ticks = new List<MatchTickEventArgs>();
        runner.TickRan += (_, e) => ticks.Add(e);
        runner.Update(Tick * 3);
        int frame = runner.Simulation.World.Frame;

        runner.SetHeld(true);
        Assert.True(runner.IsPaused);
        runner.Orders.Submit(Move(runner));
        runner.RosterOpen = true;
        runner.Update(Tick);
        Assert.Equal(TickKind.Paused, ticks[^1].Result.Kind);
        Assert.Equal(OrderOutcome.Applied, Assert.Single(ticks[^1].Result.Outcomes));
        Assert.Equal(frame, runner.Simulation.World.Frame);

        runner.SetHeld(false);
        runner.SetPaused(true);
        runner.Update(Tick);
        Assert.Equal(frame, runner.Simulation.World.Frame);
        runner.SetPaused(false);
        Assert.False(runner.IsPaused);
        runner.Update(Tick);
        Assert.True(ticks[^1].Result.WorldAdvanced);
        Assert.Equal(RequestResult.NotAllowedNow, runner.Forfeit());
    }

    [Fact]
    public void Autosave_EveryIntervalOfPlay_AndOnQuit()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store, autosave: 10);
        var notices = new List<MatchNotice>();
        runner.Notice += (_, e) => notices.Add(e.Notice);
        runner.SetHeld(true);
        for (int i = 0; i < 30; i++)
        {
            runner.StepOnce();
        }
        Assert.Empty(store.Written);
        runner.SetHeld(false);
        for (int i = 0; i < 25; i++)
        {
            runner.StepOnce();
        }
        Assert.Equal(2, store.Written.Count);
        Assert.All(store.Written, s => Assert.Equal("custom-auto", s.Stem));
        Assert.True(runner.FlushSaves());
        Assert.Equal(2, notices.Count(n => n.Kind == MatchNoticeKind.Autosaved));

        runner.Quit();
        Assert.Equal(3, store.Written.Count);
        Assert.Equal(MatchPhase.Stopped, runner.Phase);
        Assert.Equal(StopReason.Quit, runner.StopInfo!.Reason);
        Assert.False(runner.Orders.CanSubmit);
        Assert.Equal(OrderSubmitResult.NotAPlayer, runner.Orders.Submit(Move(runner)));
        long tick = runner.Simulation.Tick;
        runner.Update(1);
        runner.StepOnce();
        Assert.Equal(tick, runner.Simulation.Tick);
        runner.Quit();
        Assert.Equal(3, store.Written.Count);
    }

    [Fact]
    public void SaveAndLoad_ContinueTheSameGame()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store);
        var script = new ScriptedOrders(5);
        for (int i = 0; i < 200; i++)
        {
            if (script.Next(runner.Simulation, 0) is { } order)
            {
                runner.Orders.Submit(order);
            }
            runner.StepOnce();
        }
        SaveSlot slot = runner.Save("2")!;
        Assert.Equal("custom-2", slot.Stem);
        Assert.Equal(SaveKind.SinglePlayer, slot.Header.Kind);
        Assert.Equal(200, slot.Header.Tick);
        Assert.Equal(8, slot.Header.Mission);
        Assert.Equal(TestContent.GameVersion, slot.Header.GameVersion);
        Assert.Throws<ArgumentException>(() => runner.Save("7"));

        using LocalMatchRunner loaded = LocalMatchRunner.Load(TestContent.Content, store.Files["custom-2"], TestContent.Runner());
        Assert.Equal(runner.Simulation.StateHash(), loaded.Simulation.StateHash());
        Assert.Equal(runner.Simulation.Tick, loaded.Simulation.Tick);
        Assert.Equal(MatchKind.Custom, loaded.Match);
        Assert.Equal(runner.Players[0], loaded.Players[0]);
        for (int i = 0; i < 50; i++)
        {
            runner.StepOnce();
            loaded.StepOnce();
        }
        Assert.Equal(runner.Simulation.StateHash(), loaded.Simulation.StateHash());
    }

    [Fact]
    public void Save_WithoutStore_ReturnsNull_AndAFailedWriteIsANotice()
    {
        using LocalMatchRunner none = Start();
        Assert.Null(none.Save("1"));
        var store = new MemorySaveStore { Fail = true };
        using LocalMatchRunner failing = Start(store);
        var notices = new List<MatchNotice>();
        failing.Notice += (_, e) => notices.Add(e.Notice);
        Assert.NotNull(failing.Save("1"));
        Assert.False(failing.FlushSaves());
        Assert.Equal(MatchNoticeKind.SaveFailed, Assert.Single(notices).Kind);
        Assert.Equal("disk full", notices[0].Detail);
    }

    [Fact]
    public void Load_RejectsHostedAndBrokenSaves()
    {
        byte[] hosted = MatchSaveFile.Write(new SaveHeader
        {
            Kind = SaveKind.HostedMatch, Match = MatchKind.Versus, GameVersion = "a", ContentVersion = "b",
        }, [1, 2]);
        Assert.Equal(SaveProblem.NotASave, Assert.Throws<SaveFileException>(() => LocalMatchRunner.Load(TestContent.Content, hosted, TestContent.Runner())).Problem);

        var broken = new SaveHeader { Kind = SaveKind.SinglePlayer, Match = MatchKind.Custom, GameVersion = TestContent.GameVersion, ContentVersion = TestContent.ContentVersion };
        Assert.Equal(SaveProblem.Damaged, Assert.Throws<SaveFileException>(
            () => LocalMatchRunner.Load(TestContent.Content, MatchSaveFile.Write(broken, [1, 2, 3]), TestContent.Runner())).Problem);
        Assert.Equal(SaveProblem.OtherVersion, Assert.Throws<SaveFileException>(
            () => LocalMatchRunner.Load(TestContent.Content, MatchSaveFile.Write(broken with { GameVersion = "0.1" }, [1, 2, 3]), TestContent.Runner())).Problem);
        Assert.Throws<ArgumentNullException>(() => LocalMatchRunner.Load(null!, hosted, TestContent.Runner()));
    }

    [Fact]
    public void LostMission_PutsThePlayerOut_AndEndsTheMatch()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store, autosave: 1);
        var over = new List<MatchOverEventArgs>();
        var ticks = new List<MatchTickEventArgs>();
        runner.MatchOver += (_, e) => over.Add(e);
        runner.TickRan += (_, e) => ticks.Add(e);
        runner.Simulation.World.EndCountdown = DefeatShown;
        runner.StepOnce();

        MatchOverEventArgs result = Assert.Single(over);
        Assert.False(result.LocalVictory);
        Assert.True(result.Outcome.Won(1));
        Assert.Equal(MatchPhase.Over, runner.Phase);
        Assert.Equal(LocalRole.Out, runner.Role);
        Assert.Equal(-1, runner.LocalTribe);
        Assert.Equal(0, runner.ViewTribe);
        Assert.Equal(PlayerChangeKind.Out, Assert.Single(ticks[^1].Changes).Kind);
        Assert.Empty(store.Written);
        runner.StepOnce();
        Assert.Single(over);
        runner.Quit();
        Assert.Empty(store.Written);

        byte[] saved = MatchSaveFile.Write(new SaveHeader
        {
            Kind = SaveKind.SinglePlayer, Match = MatchKind.Custom, GameVersion = "a", ContentVersion = "b", Players = runner.Players,
        }, runner.Simulation.Save());
        using LocalMatchRunner reloaded = LocalMatchRunner.Load(TestContent.Content, saved, TestContent.Runner());
        Assert.Equal(MatchPhase.Over, reloaded.Phase);
        Assert.NotNull(reloaded.Outcome);
    }

    [Fact]
    public void Dispose_Quits()
    {
        var store = new MemorySaveStore();
        LocalMatchRunner runner = Start(store);
        var stops = new List<MatchStop>();
        runner.Stopped += (_, e) => stops.Add(e.Stop);
        runner.Dispose();
        runner.Dispose();
        Assert.Single(stops);
        Assert.Single(store.Written);
    }

    [Fact]
    public void Content_IsLoaded() => Assert.IsType<GameContent>(TestContent.Content);
}
