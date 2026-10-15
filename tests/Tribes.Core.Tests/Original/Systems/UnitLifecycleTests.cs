using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitLifecycle"/> and <see cref="UnitSpawning"/> with the original, plus edge cases.</summary>
public sealed class UnitLifecycleTests
{
    private const int AgeField = 14, XField = 0, YField = 1;
    private const int UnitsPerSample = 3;
    private static readonly int[] Deltas = { -5, 0, 2, 10 };
    private static readonly (int Stat, int Chance, int Cap, int Step)[] Trainings =
    {
        (1, 50, 9, 1), (4, 100, 90, 5), (0, 30, 5, -1), (6, 15, 6, 1), (11, 100, 10, 1), (5, 40, 9, 1),
    };

    private readonly ITestOutputHelper output;

    public UnitLifecycleTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void HitPointsAndPractice_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), UnitsPerSample))
            {
                foreach (int delta in SampleInputs.Pick(Deltas, id, 2))
                {
                    run.Check(sample, $"ChangeHitPoints unit {id} delta {delta}",
                        g => { g.world_a.i_u = (id - 1) / 50; return g.world_a.a_II_Z(id, delta); },
                        s => s.UnitLifecycle.ChangeHitPoints(s.World.Unit(id), delta));
                }
                foreach (var t in Enumerable.Range(0, 2).Select(i => Trainings[(id + sample.Frame / 1000 + i) % Trainings.Length]))
                {
                    run.Check(sample, $"Practise unit {id} stat {t.Stat}",
                        g => { g.world_a.i_v = id; return g.world_a.a_BBBB((sbyte)t.Stat, (sbyte)t.Chance, (sbyte)t.Cap, (sbyte)t.Step); },
                        s => s.UnitLifecycle.Practise(s.World.Unit(id), t.Stat, t.Chance, t.Cap, t.Step));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void DeathAndFreeSlot_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var dying = Enumerable.Range(1, 100).Where(id => LegacySamples.Field(game, LegacySamples.UnitHitPointsField, id) < 0);
            var live = SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample).ToList();
            foreach (int id in live)
            {
                run.Check(sample, $"Kill unit {id}", g => g.a_B((sbyte)id), s => s.UnitLifecycle.Kill(s.World.Unit(id)));
            }
            foreach (int id in dying.Concat(live.Take(1)))
            {
                run.Check(sample, $"FreeSlot unit {id}",
                    g => g.world_a.m_I(id),
                    s => s.UnitLifecycle.FreeSlot(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void ChangeTribe_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var live = LegacySamples.LiveUnits(game).ToList();
            var children = live.Where(id => LegacySamples.Field(game, AgeField, id) < 12);
            foreach (int id in SampleInputs.Spread(children, 2).Concat(SampleInputs.Spread(live, 2)).Distinct())
            {
                int tribe = 1 - ((id - 1) / 50);
                run.Check(sample, $"ChangeTribe unit {id} to {tribe}",
                    g => { g.world_a.i_u = tribe; g.world_a.c_B_V((sbyte)id); },
                    s => s.UnitLifecycle.ChangeTribe(s.World.Unit(id), tribe));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void SpawnPortraitAndName_MatchTheOriginal()
    {
        var run = new ParityRun();
        int[] ages = { 3, 20, 60 };
        int[] traits = { 0, 0x2A, 0x45, 0x7F };
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var live = LegacySamples.LiveUnits(game).ToList();
            foreach (int tribe in new[] { 0, 1 })
            {
                int near = live.FirstOrDefault(id => (id - 1) / 50 == tribe, live[0]);
                int x = System.Math.Min(LegacySamples.Field(game, XField, near) + 1, game.s_c - 1), y = LegacySamples.Field(game, YField, near);
                int age = ages[(sample.Frame / 1000 + tribe) % ages.Length];
                int bits = traits[(sample.Frame / 1000 + tribe) % traits.Length];
                run.Check(sample, $"Spawn tribe {tribe} at {x},{y} age {age} traits {bits}",
                    g => (int)g.a_BBBBB((sbyte)tribe, (sbyte)x, (sbyte)y, (sbyte)age, (sbyte)bits),
                    s => s.UnitSpawning.Spawn(tribe, new GridPoint(x, y), age, new UnitTraits(bits))?.Id ?? -1);
            }
            foreach (int id in SampleInputs.Spread(live, UnitsPerSample))
            {
                run.Check(sample, $"PickPortrait unit {id}", g => g.b_B_m((sbyte)id), s => s.UnitSpawning.PickPortrait(s.World.Unit(id)));
                run.Check(sample, $"PickName unit {id}", g => g.g_I(id), s => s.UnitSpawning.PickName(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Spawn_FailsWhenTheTribeIsFull()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        for (int id = 1; id <= 50; id++)
        {
            systems.World.Unit(id).HitPoints = 1;
        }

        Assert.Null(systems.UnitSpawning.Spawn(0, new GridPoint(3, 3), 20, UnitTraits.None));
        Unit? enemy = systems.UnitSpawning.Spawn(1, new GridPoint(3, 3), 2, new UnitTraits(0x40));
        Assert.NotNull(enemy);
        Assert.Equal((51, ActionNumbers.ChildIdle, -2), (enemy!.Id, enemy.Action, enemy.NameIndex));
        Assert.Equal(MapCell.UnitAt(51), systems.World.Map.CellAt(new GridPoint(3, 3)));
    }

    [Fact]
    public void ChangeHitPoints_StaysWithinTheLimitsAndCuresWithAStrongHeal()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = systems.World.Unit(2);
        (unit.HitPoints, unit.MaxHitPoints, unit.Status) = (5, 20, UnitStatus.Diseased);

        Assert.False(systems.UnitLifecycle.ChangeHitPoints(unit, -9));
        Assert.Equal(0, unit.HitPoints);
        Assert.True(systems.UnitLifecycle.ChangeHitPoints(unit, 30));
        Assert.Equal((20, UnitStatus.None, -100), (unit.HitPoints, unit.Status, unit.DiseaseExposure));
        Assert.True(systems.UnitLifecycle.ChangeHitPoints(unit, 1));
        Assert.Contains(systems.PendingEvents, e => e is HitPointsChanged { Unit: 2, Delta: -9 });
    }

    [Fact]
    public void FreeSlot_PutsBackTheTrapTheUnitStoodOn()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = systems.World.Unit(4);
        (unit.X, unit.Y, unit.HitPoints, unit.NextOccupantOrCellObject) = (6, 7, -1, CellCode.Trap(1, 21));
        systems.World.Tribes[0][TribeCounter.Population] = 3;

        systems.UnitLifecycle.FreeSlot(unit);

        Assert.Equal(new MapCell(CellContent.Trap, 21, 1), systems.World.Map.CellAt(new GridPoint(6, 7)));
        Assert.Equal((0, 2), (unit.HitPoints, systems.World.Tribes[0][TribeCounter.Population]));
    }
}
