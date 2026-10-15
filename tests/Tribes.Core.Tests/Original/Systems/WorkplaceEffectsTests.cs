using System;
using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Core.Tests.Systems.WorkScenarios;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkplaceEffects"/> with the original.</summary>
public sealed class WorkplaceEffectsTests
{
    private const int NextOccupant = 45, BuildingKinds = 20, Levels = 3;

    private readonly ITestOutputHelper output;

    public WorkplaceEffectsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TrainEntertainHeal_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            int[] occupied = Enumerable.Range(1, 60).Where(b => BuildingField(sample.Restore(), BuildingFirstOccupant, b) > 0).ToArray();
            foreach (int id in Some(sample, 6))
            {
                int kind = (id + sample.Frame) % BuildingKinds, level = id % Levels;
                run.Check(sample, $"Train unit {id} kind {kind}", null,
                    g => { BeginTurn(g, id); return g.world_a.b_II_Z(kind, level); },
                    s => s.WorkplaceEffects.Train(s.World.Unit(id), kind, level));
                if (occupied.Length == 0)
                {
                    continue;
                }
                int building = occupied[id % occupied.Length];
                Action<GameCanvas> join = g => Join(g, id, building);
                run.Check(sample, $"Entertain unit {id}", join,
                    g => { BeginTurn(g, id); g.world_a.j_I(0); },
                    s => s.WorkplaceEffects.Entertain(s.World.Unit(id)));
                run.Check(sample, $"Heal unit {id}", join,
                    g => { BeginTurn(g, id); g.world_a.j_I(1); },
                    s => s.WorkplaceEffects.Heal(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    /// <summary>Makes <paramref name="id"/> one more occupant of <paramref name="building"/> (the first of its list).</summary>
    private static void Join(GameCanvas g, int id, int building)
    {
        int first = BuildingField(g, BuildingFirstOccupant, building);
        for (int u = first; u > 0; u = Get(g, NextOccupant, u))
        {
            if (u == id)
            {
                return;
            }
        }
        Set(g, NextOccupant, id, first);
        SetBuilding(g, BuildingFirstOccupant, building, id);
        Set(g, TargetIndex, id, building);
        foreach (int other in Enumerable.Range(1, 100).Where(u => u != id && Get(g, HitPoints, u) > 0).Take(3))
        {
            Set(g, HitPoints, other, Math.Max(1, Get(g, 67, other) - 5));
        }
    }
}
