using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

public sealed class LockstepMatchTests
{
    // The end countdown values of an announced result (MissionOutcome, internal to the rules).
    private const int VictoryShown = 41, DefeatShown = 90;

    private static readonly MatchPlan Plan = new() { Mission = 8, Difficulty = 0, Seed = 7 };

    private static LockstepMatch Create(MatchStart start, ITribeControl? control = null) =>
        LockstepMatch.Create(TestContent.Content, start, control);

    private static PlayerCommand Game(int slot, GameOrder order) => new(slot, CommandKind.Game, GameOrderCodec.Encode(order));

    private static PlayerCommand System(int slot, CommandKind kind) => new(slot, kind, ReadOnlyMemory<byte>.Empty);

    private static GameOrder MoveOrder(LockstepMatch match, int tribe)
    {
        GameWorld world = match.Simulation.World;
        int unit = world.UnitsOf(tribe).Select(u => u.Id).First(u => world.Unit(u).InUse);
        Unit u = world.Unit(unit);
        return new MoveUnits(tribe, UnitSet.Of(unit), new GridPoint(u.X + 2, u.Y + 1), 0, false);
    }

    [Fact]
    public void Create_MapsFirstHumanAndComputerSlots_OthersHaveNoTribe()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), cpu = TestContent.Computer(1), other = TestContent.Human(2, "Other"), watcher = TestContent.Spectator("Eve");
        LockstepMatch match = Create(TestContent.Start(Plan, host, cpu, other, watcher), OriginalTribeControl.Instance);

        Assert.Equal(0, match.Players.BySlot(0)!.Tribe);
        Assert.Equal(1, match.Players.BySlot(1)!.Tribe);
        Assert.True(match.Players.BySlot(1)!.IsComputer);
        Assert.Equal(-1, match.Players.BySlot(2)!.Tribe);
        Assert.Equal(PlayerState.Spectator, match.Players.ById(watcher.Id)!.State);
        Assert.Equal(0, match.Tick);
        Assert.Equal(8, match.Simulation.World.Mission);
        Assert.Null(match.Outcome);
        Assert.False(match.Simulation.Options.HintsPauseGame);
    }

    [Fact]
    public void Create_BadSetup_ThrowsInvalidData()
    {
        PlayerInfo host = TestContent.Human(0, "Host");
        MatchStart start = TestContent.Start(Plan, host);
        Assert.Throws<InvalidDataException>(() => Create(start with { GameSetup = "{not json"u8.ToArray() }));
        Assert.Throws<InvalidDataException>(() => Create(start with { GameSetup = ReadOnlyMemory<byte>.Empty }));
        Assert.Throws<InvalidDataException>(() => Create(start with { GameSetup = new MatchPlan { Mission = 99 }.ToBytes() }));
        Assert.Throws<InvalidDataException>(() => Create(start with { GameSetup = new MatchPlan { Mission = 8, Difficulty = 3 }.ToBytes() }));
        Assert.Throws<InvalidDataException>(() => Create(start with { GameSetup = "null"u8.ToArray() }));
    }

    [Fact]
    public void Step_AppliesOrdersOfThePlayingSlot_IgnoresSlotsWithoutTribe()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), other = TestContent.Human(2, "Other");
        LockstepMatch match = Create(TestContent.Start(Plan, host, TestContent.Computer(1), other), OriginalTribeControl.Instance);
        var ticks = new List<LockstepTickEventArgs>();
        match.Stepped += (_, e) => ticks.Add(e);

        match.Step([Game(0, MoveOrder(match, 0)), Game(2, MoveOrder(match, 0)), new PlayerCommand(0, CommandKind.Game, new byte[] { 9, 9 })]);

        LockstepTickEventArgs tick = Assert.Single(ticks);
        Assert.Equal(2, tick.Result.Outcomes.Count);
        Assert.Equal(OrderOutcome.Applied, tick.Result.Outcomes[0]);
        Assert.Equal(OrderOutcome.Malformed, tick.Result.Outcomes[1]);
        Assert.Equal(1, tick.IgnoredCommands);
        Assert.All(tick.Sources, s => Assert.Equal(0, s.Slot));
        Assert.Equal(1, match.Tick);
    }

    [Fact]
    public void BadPayloads_AreIgnoredIdentically_OnEveryPeer()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), cpu = TestContent.Computer(1);
        MatchStart start = TestContent.Start(Plan, host, cpu);
        LockstepMatch a = Create(start), b = Create(start), clean = Create(start);
        var script = new ScriptedOrders(3);
        for (int t = 0; t < 300; t++)
        {
            var commands = new List<PlayerCommand>();
            if (t % 3 == 0)
            {
                commands.Add(new PlayerCommand(0, CommandKind.Game, script.Garbage()));
                commands.Add(new PlayerCommand(1, CommandKind.Game, script.Garbage()));
                commands.Add(new PlayerCommand(3, CommandKind.Game, GameOrderCodec.Encode(new SetHints(0, true))));
                commands.Add(new PlayerCommand(0, CommandKind.Game, ReadOnlyMemory<byte>.Empty));
                commands.Add(new PlayerCommand(1, CommandKind.PlayerJoined, new byte[] { 1, 2, 3 }));
            }
            a.Step(commands);
            b.Step(commands);
            clean.Step([]);
        }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(clean.Simulation.StateHash(), a.Simulation.StateHash());
        Assert.Equal(clean.Players.Count, a.Players.Count);
    }

    [Fact]
    public void AbsentAndFinishedSlots_GetNoOrders_ReturnRestoresThem()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest");
        var control = new ScriptedControl();
        LockstepMatch match = Create(TestContent.Start(Plan, host, guest), control);
        var ticks = new List<LockstepTickEventArgs>();
        match.Stepped += (_, e) => ticks.Add(e);

        match.Step([System(1, CommandKind.PlayerAbsent), Game(1, MoveOrder(match, 1))]);
        Assert.Equal(PlayerState.Absent, match.Players.BySlot(1)!.State);
        Assert.Equal(1, ticks[^1].IgnoredCommands);
        Assert.Equal(PlayerChangeKind.Absent, Assert.Single(ticks[^1].Changes).Kind);

        match.Step([System(1, CommandKind.PlayerReturned), Game(1, MoveOrder(match, 1))]);
        Assert.Equal(PlayerState.Playing, match.Players.BySlot(1)!.State);
        Assert.Equal(0, ticks[^1].IgnoredCommands);
        Assert.Single(ticks[^1].Result.Outcomes);

        match.Step([System(1, CommandKind.PlayerForfeited), System(1, CommandKind.PlayerReturned), Game(1, MoveOrder(match, 1))]);
        Assert.Equal(PlayerState.Forfeited, match.Players.BySlot(1)!.State);
        Assert.Equal(1, ticks[^1].IgnoredCommands);
        Assert.Single(ticks[^1].Changes);

        match.Step([System(1, CommandKind.PlayerAbsent), System(1, CommandKind.PlayerReturned), System(3, CommandKind.PlayerAbsent)]);
        Assert.Equal(PlayerState.Forfeited, match.Players.BySlot(1)!.State);
        Assert.Empty(ticks[^1].Changes);

        Assert.Equal(
            [(1, 1, PlayerState.Absent), (1, 1, PlayerState.Playing), (1, 1, PlayerState.Forfeited)],
            control.States);
    }

    [Fact]
    public void ResumedAbsentPlayers_AreReportedToTheRulesAtStart()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest", presence: PlayerPresence.Absent);
        var control = new ScriptedControl();
        LockstepMatch match = Create(TestContent.Start(Plan, host, guest), control);
        Assert.Equal(PlayerState.Absent, match.Players.BySlot(1)!.State);
        Assert.Equal([(1, 1, PlayerState.Absent)], control.States);
    }

    [Fact]
    public void DefeatedTribes_GoOut_AndTheOutcomeIsReportedOnce()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest");
        var control = new ScriptedControl();
        LockstepMatch match = Create(TestContent.Start(Plan, host, guest), control);
        var ticks = new List<LockstepTickEventArgs>();
        match.Stepped += (_, e) => ticks.Add(e);

        control.Defeated.Add(1);
        match.Step([]);
        Assert.Equal(PlayerState.Out, match.Players.BySlot(1)!.State);
        Assert.Equal(PlayerChangeKind.Out, Assert.Single(ticks[^1].Changes).Kind);
        Assert.Contains((1, 1, PlayerState.Out), control.States);

        match.Step([System(1, CommandKind.PlayerForfeited), Game(1, MoveOrder(match, 1))]);
        Assert.Equal(PlayerState.Out, match.Players.BySlot(1)!.State);
        Assert.Equal(1, ticks[^1].IgnoredCommands);

        control.Result = new MatchOutcome([0]);
        match.Step([]);
        Assert.Same(control.Result, ticks[^1].Decided);
        match.Step([]);
        Assert.Null(ticks[^1].Decided);
        Assert.True(match.Outcome!.Won(0));
        Assert.False(match.Outcome.Won(1));
        Assert.False(match.Outcome.Won(-1));
    }

    [Fact]
    public void OriginalControl_ReportsTheMissionResult()
    {
        LockstepMatch match = Create(TestContent.Start(Plan, TestContent.Human(0, "Host"), TestContent.Computer(1)));
        var control = OriginalTribeControl.Instance;
        Assert.False(control.IsDefeated(match.Simulation, 0));
        Assert.Null(control.Outcome(match.Simulation));
        Assert.Equal(-1, control.AddTribe(match.Simulation, 2, 2));
        control.SetPlayerState(match.Simulation, 0, 0, PlayerState.Absent);

        match.Simulation.World.EndCountdown = VictoryShown;
        Assert.True(control.IsDefeated(match.Simulation, 1));
        Assert.False(control.IsDefeated(match.Simulation, 0));
        Assert.True(control.Outcome(match.Simulation)!.Won(0));

        match.Simulation.World.EndCountdown = DefeatShown;
        Assert.True(control.IsDefeated(match.Simulation, 0));
        Assert.True(control.Outcome(match.Simulation)!.Won(1));
        Assert.Throws<ArgumentException>(() => control.Start(TestContent.Content, new MatchPlan { Mission = 77 }, []));
    }

    [Fact]
    public void Snapshot_RoundTrips_AndBothCopiesStayEqual()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest");
        MatchStart start = TestContent.Start(Plan, host, guest);
        LockstepMatch original = Create(start, new TwoHumansControl());
        var script = new ScriptedOrders(11);
        for (int t = 0; t < 120; t++)
        {
            GameOrder? order = script.Next(original.Simulation, 0);
            original.Step(order is null ? [] : [Game(0, order), System(1, CommandKind.PlayerAbsent)]);
        }

        LockstepMatch copy = Create(start, new TwoHumansControl());
        int reloads = 0;
        copy.Reloaded += (_, _) => reloads++;
        copy.LoadSnapshot(original.SaveSnapshot());

        Assert.Equal(1, reloads);
        Assert.Equal(original.Tick, copy.Tick);
        Assert.Equal(original.StateHash(), copy.StateHash());
        Assert.Equal(PlayerState.Absent, copy.Players.BySlot(1)!.State);
        Assert.Equal(original.Plan, copy.Plan);
        for (int t = 0; t < 100; t++)
        {
            GameOrder? order = script.Next(original.Simulation, 0);
            PlayerCommand[] commands = order is null ? [] : [Game(0, order)];
            original.Step(commands);
            copy.Step(commands);
        }
        Assert.Equal(original.StateHash(), copy.StateHash());
        Assert.Equal(original.Simulation.StateHash(), copy.Simulation.StateHash());
    }

    [Fact]
    public void StateHash_CoversThePlayerTable()
    {
        MatchStart start = TestContent.Start(Plan, TestContent.Human(0, "Host"), TestContent.Human(1, "Guest"));
        LockstepMatch a = Create(start, OriginalTribeControl.Instance), b = Create(start, OriginalTribeControl.Instance);
        a.Step([]);
        b.Step([System(1, CommandKind.PlayerAbsent)]);
        Assert.Equal(a.Simulation.StateHash(), b.Simulation.StateHash());
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void LoadSnapshot_RejectsDamagedData()
    {
        MatchStart start = TestContent.Start(Plan, TestContent.Human(0, "Host"), TestContent.Computer(1));
        LockstepMatch match = Create(start);
        match.Step([]);
        byte[] good = match.SaveSnapshot();
        long tick = match.Tick;

        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(good[..(good.Length / 2)]));
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot([.. good, 0]));
        byte[] version = (byte[])good.Clone();
        version[4] = 9;
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(version));
        byte[] magic = (byte[])good.Clone();
        magic[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(magic));
        byte[] wrongTick = (byte[])good.Clone();
        wrongTick[6] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(wrongTick));
        byte[] hugeBlock = (byte[])good.Clone();
        hugeBlock[14] = 0xFF;
        hugeBlock[17] = 0x7F;
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(hugeBlock));

        Assert.Equal(tick, match.Tick);
        match.LoadSnapshot(good);
        Assert.Equal(tick, match.Tick);
    }

    [Fact]
    public void Factory_BuildsMatches()
    {
        Func<MatchStart, ILockstepSimulation> factory = LockstepMatch.Factory(TestContent.Content);
        ILockstepSimulation simulation = factory(TestContent.Start(Plan, TestContent.Human(0, "Host")));
        Assert.IsType<LockstepMatch>(simulation);
        Assert.Throws<ArgumentNullException>(() => LockstepMatch.Factory(null!));
        Assert.Throws<ArgumentNullException>(() => ((LockstepMatch)simulation).Step(null!));
        Assert.Throws<ArgumentNullException>(() => ((LockstepMatch)simulation).LoadSnapshot(null!));
    }

    [Fact]
    public void EndlessPlan_UsesTheFirstCustomMap()
    {
        var plan = new MatchPlan { Mission = -1, Seed = 3, Endless = new EndlessPlan(3, 32, 2, 1) };
        MatchPlan read = MatchPlan.Read(plan.ToBytes());
        Assert.Equal(plan, read);
        Assert.Equal(Tribes.Core.Systems.Missions.MissionStart.FirstCustomMission, read.MapMission);
        LockstepMatch match = Create(TestContent.Start(read, TestContent.Human(0, "Host")));
        Assert.Equal(read.MapMission, match.Simulation.World.Mission);
    }

    [Fact]
    public void MatchPlan_ReadsTheClientsJson()
    {
        MatchPlan plan = MatchPlan.Read("""{"mission": 9, "difficulty": 1, "seed": 1234, "endless": null, "extra": true}"""u8);
        Assert.Equal(new MatchPlan { Mission = 9, Difficulty = 1, Seed = 1234 }, plan);
        Assert.Contains("\"mission\":9", global::System.Text.Encoding.UTF8.GetString(plan.ToBytes()), StringComparison.Ordinal);
    }

    [Fact]
    public void GameContent_IsShared() => Assert.IsType<GameContent>(TestContent.Content);
}
