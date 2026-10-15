using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimationDriver"/> with the original, plus edge cases.</summary>
public sealed class AnimationDriverTests
{
    private const int AnimationTickField = 30;

    private readonly ITestOutputHelper output;

    public AnimationDriverTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6))
            {
                int goal = UnitSampling.GoalDistance(game, id);
                foreach (int tick in new[] { -1, 120 })
                {
                    int set = tick < 0 ? LegacySamples.Field(game, AnimationTickField, id) : tick;
                    run.Check(sample, $"Update unit {id} tick {set}",
                        g =>
                        {
                            UnitSampling.Set(g, AnimationTickField, id, set);
                            Legacy.World w = UnitSampling.Turn(g, id, goal);
                            w.v_();
                            return (w.z_d, w.i_z);
                        },
                        s =>
                        {
                            s.World.Unit(id).AnimationTick = set;
                            UnitTurn turn = s.UnitGoals.StartTurn(s.World.Unit(id));
                            s.AnimationDriver.Update(turn);
                            return (turn.StepEnded, turn.GoalDistance);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 700);
    }

    [Fact]
    public void Update_ATrapHurtsTheUnitWhenItBreaksFree()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 31);
        (unit.Animation, unit.AnimationTick, unit.NextOccupantOrCellObject) = (UnitAnimations.FirstTrapped, 100, -117);

        systems.AnimationDriver.Update(systems.UnitGoals.StartTurn(unit));

        Assert.Equal((10, 0), (unit.HitPoints, unit.NextOccupantOrCellObject));
    }
}
