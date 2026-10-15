using System.Diagnostics;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// How fast the game loop runs on busy mission states (a player catching up after a snapshot needs
/// well over a thousand ticks a second), and where a frame's time goes.
/// </summary>
public sealed class PerformanceTests
{
    private const int WarmUpTicks = 4000, MeasuredTicks = 3000;

    /// <summary>
    /// A floor that still holds with coverage instrumentation on a busy machine, to catch a gross
    /// slowdown; the target of 1000 ticks a second is measured on an idle machine (see the output).
    /// </summary>
    private const double FloorTicksPerSecond = 250;

    private readonly ITestOutputHelper output;

    public PerformanceTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void BusyMissions_RunFastEnoughToCatchUp()
    {
        double slowest = double.MaxValue;
        for (int mission = 0; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            GameSimulation game = Busy(mission);
            if (game.IsOver)
            {
                continue;
            }
            byte[] start = game.Save();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredTicks; i++)
            {
                game.Step();
            }
            clock.Stop();
            double perSecond = MeasuredTicks / clock.Elapsed.TotalSeconds;
            slowest = System.Math.Min(slowest, perSecond);
            GameSimulation again = GameSimulation.Load(LegacySamples.Content, start);
            Profile profile = Measure(again, MeasuredTicks);
            clock.Restart();
            const int hashes = 200;
            for (int i = 0; i < hashes; i++)
            {
                game.StateHash();
            }
            double hashMicroseconds = clock.Elapsed.TotalMilliseconds * 1000 / hashes;
            clock.Restart();
            int saved = game.Save().Length;
            double saveMilliseconds = clock.Elapsed.TotalMilliseconds;
            output.WriteLine($"mission {mission}: {perSecond:F0} ticks/s, {Units(game)} units, {Animals(game)} animals; {profile}; "
                + $"hash {hashMicroseconds:F0} µs, save {saved} bytes in {saveMilliseconds:F1} ms");
        }
        output.WriteLine($"slowest: {slowest:F0} ticks/s");
        Assert.True(slowest >= FloorTicksPerSecond, $"slowest mission ran {slowest:F0} ticks/s");
    }

    [Fact]
    public void BusiestPlayedStates_RunFastEnoughToCatchUp()
    {
        var busiest = ScenarioSamples.Played(everyNth: 1)
            .Where(s => !s.Restore().z_s)
            .Select(s => (Sample: s, Units: LegacySamples.LiveUnits(s.Restore()).Count()))
            .OrderByDescending(s => s.Units).Take(5).ToList();
        double slowest = double.MaxValue;
        foreach ((LegacySample sample, int units) in busiest)
        {
            var world = Parity.LegacyRules.Import(sample.Restore(), LegacySamples.Content);
            GameSimulation game = GameSimulation.FromWorld(world);
            byte[] start = game.Save();
            var clock = Stopwatch.StartNew();
            int ticks = 0;
            for (; ticks < MeasuredTicks && !game.IsOver && !game.World.Script.Active; ticks++)
            {
                game.Step();
            }
            clock.Stop();
            double perSecond = ticks / clock.Elapsed.TotalSeconds;
            slowest = System.Math.Min(slowest, perSecond);
            Profile profile = Measure(GameSimulation.Load(LegacySamples.Content, start), ticks);
            output.WriteLine($"{sample} (mission {world.Mission}): {units} units, {ticks} ticks at {perSecond:F0} ticks/s; {profile}");
            output.WriteLine("  units' part: " + MeasureUnits(GameSimulation.Load(LegacySamples.Content, start), ticks));
        }
        output.WriteLine($"slowest: {slowest:F0} ticks/s");
        Assert.True(slowest >= FloorTicksPerSecond, $"slowest state ran {slowest:F0} ticks/s");
    }

    /// <summary>Mission <paramref name="mission"/> after some thousand ticks of play.</summary>
    private static GameSimulation Busy(int mission)
    {
        GameSimulation game = GameSimulation.StartMission(LegacySamples.Content, mission, 1, new MissionOptions { Seed = 5 + mission });
        for (int i = 0; i < WarmUpTicks && !game.IsOver; i++)
        {
            game.Step(ScriptedPlayer.InputFor(game, game.Tick, mission));
        }
        while (game.World.Script.Active)
        {
            game.Step(TickInput.WithOrders(new Tribes.Core.Commands.AdvanceScene(0)));
        }
        return game;
    }

    private static int Units(GameSimulation game) =>
        Enumerable.Range(1, game.World.UnitSlots).Count(id => game.World.Unit(id).HitPoints > 0);

    private static int Animals(GameSimulation game) =>
        Enumerable.Range(0, game.World.AnimalSlots).Count(i => game.World.Animal(i).HitPoints > 0);

    /// <summary>Runs <paramref name="ticks"/> world frames part by part, timing each part.</summary>
    private static Profile Measure(GameSimulation game, int ticks)
    {
        RuleSystems s = game.Rules;
        var parts = new Stopwatch[5];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new Stopwatch();
        }
        for (int tick = 0; tick < ticks && !game.IsOver && !game.World.Script.Active; tick++)
        {
            Time(parts[0], () => s.BuildingUpdate.RunFrame());
            Time(parts[1], () => s.UnitUpdate.RunFrame());
            Time(parts[2], () => s.WildlifeUpdate.RunFrame());
            Time(parts[3], () => s.ResourceUpdate.RunFrame(new TutorialInputs(false)));
            Time(parts[4], () =>
            {
                game.World.Frame++;
                s.SpottedTrapSweep.Advance();
                s.MissionOutcome.AdvanceCountdown(true);
                s.BuildingWorkLists.AdvanceDust(s.BuildingLifecycle.Free);
                s.Sight.RevealSeenBuildings();
            });
            s.TakeEvents();
        }
        return new Profile(parts.Select(p => p.Elapsed.TotalMilliseconds / ticks).ToArray());
    }

    /// <summary>
    /// The units' part of <paramref name="ticks"/> frames split into its steps (in the units' own order),
    /// with the rest of each frame run untimed.
    /// </summary>
    private static string MeasureUnits(GameSimulation game, int ticks)
    {
        RuleSystems s = game.Rules;
        var watches = new Stopwatch[5];
        for (int i = 0; i < watches.Length; i++)
        {
            watches[i] = new Stopwatch();
        }
        for (int tick = 0; tick < ticks && !game.IsOver && !game.World.Script.Active; tick++)
        {
            s.BuildingUpdate.RunFrame();
            Time(watches[0], () => s.FrameStart.Run());
            foreach (TribeState tribe in game.World.Tribes)
            {
                if (TribeRoles.IsComputer(game.World, tribe.Index))
                {
                    Time(watches[1], () => s.ComputerPlayer.Step(tribe.Index));
                }
                Time(watches[2], () => s.TargetCycle.HandOutOrders(tribe.Index));
                ListView<Unit> units = game.World.UnitsOf(tribe.Index);
                for (int i = 0; i < units.Count; i++)
                {
                    Unit unit = units[i];
                    if (unit.HitPoints == 0)
                    {
                        continue;
                    }
                    watches[3].Start();
                    bool gone = s.UnitHousekeeping.Update(unit);
                    watches[3].Stop();
                    if (!gone)
                    {
                        Time(watches[4], () => s.UnitUpdate.TakeTurn(s.UnitGoals.StartTurn(unit)));
                    }
                }
                s.HutBirths.Update(tribe.Index);
            }
            s.WildlifeUpdate.RunFrame();
            s.ResourceUpdate.RunFrame(new TutorialInputs(false));
            game.World.Frame++;
            s.TakeEvents();
        }
        string[] names = { "frame start", "computer player", "hand out orders", "housekeeping", "turns" };
        return string.Join(", ", names.Select((n, i) => $"{n} {watches[i].Elapsed.TotalMilliseconds * 1000 / ticks:F0} µs"));
    }

    private static void Time(Stopwatch watch, System.Action action)
    {
        watch.Start();
        action();
        watch.Stop();
    }

    private sealed record Profile(double[] MillisecondsPerFrame)
    {
        public override string ToString()
        {
            string[] names = { "buildings", "units", "wildlife", "nodes+story", "frame end" };
            return string.Join(", ", names.Select((n, i) => $"{n} {MillisecondsPerFrame[i] * 1000:F0} µs"));
        }
    }
}
