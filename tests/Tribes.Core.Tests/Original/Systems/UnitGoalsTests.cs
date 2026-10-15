using Tribes.Core.Systems.Units.Work;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitGoals"/> with the original's goal measurement, plus edge cases.</summary>
public sealed class UnitGoalsTests
{
    /// <summary>The measurement's side effect (an attacker of a building aims at the nearest footprint cell) is not part of it.</summary>
    private static readonly string[] SideEffect = { "unit.DestinationX", "unit.DestinationY" };

    private static readonly int[] Actions = { -1, 0, 3, 8, 20, 69, 94 };

    private readonly ITestOutputHelper output;

    public UnitGoalsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Measure_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, 2))
                {
                    int a = action;
                    run.Check(sample, $"Measure unit {id} action {a}", g => { if (a >= 0) { Set(g, ActionField, id, a); } },
                        g => { BeginTurn(g, id); return g.world_a.a_III_I(Get(g, X, id), Get(g, Y, id), 0); },
                        s => s.UnitGoals.Measure(s.World.Unit(id)), SideEffect);
                }
                run.Check(sample, $"GoalDistance unit {id}", null,
                    g => GoalDistance(g, id),
                    s => s.UnitGoals.StartTurn(s.World.Unit(id)).GoalDistance);
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void Measure_PendingSearchIsAtItsGoal()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit unit = systems.World.Unit(1);
        unit.TargetIndex = -6;
        unit.DestinationX = 30;
        Assert.Equal(0, systems.UnitGoals.Measure(unit));
        unit.TargetIndex = 0;
        Assert.Equal(200, systems.UnitGoals.Measure(unit));
        UnitTurn turn = systems.UnitGoals.StartTurn(unit);
        Assert.Equal(20, turn.GoalCells);
        turn.Threat.Priority = 3;
        turn.ClearThreat();
        Assert.Equal(0, turn.Threat.Priority);
    }
}
