using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimationChooser"/> with the original, plus edge cases.</summary>
public sealed class AnimationChooserTests
{
    private static readonly int[] Rolls = { 0, 21, 50, 86, 99 };

    private readonly ITestOutputHelper output;

    public AnimationChooserTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Choose_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 5))
            {
                int goal = UnitSampling.GoalDistance(game, id);
                foreach (int roll in SampleInputs.Pick(Rolls, id, 3))
                {
                    run.Check(sample, $"Choose unit {id} goal {goal} roll {roll}",
                        g =>
                        {
                            Legacy.World w = UnitSampling.Turn(g, id, goal);
                            w.i_x = roll;
                            bool ends = w.b_I_m(goal);
                            return (ends, w.i_H);
                        },
                        s =>
                        {
                            AnimationPick pick = s.AnimationChooser.Choose(s.World.Unit(id), goal, roll);
                            return (pick.TurnEnds, pick.Action);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Choose_AFisherTurnsToTheWater()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 17);
        (unit.Action, unit.TargetIndex, unit.Facing) = (ActionNumbers.Fishing, 0, 3);
        systems.World.Map.SetCell(new GridPoint(unit.X + 1, unit.Y), MapCell.Node(4));

        AnimationPick pick = systems.AnimationChooser.Choose(unit, 0, 40);

        Assert.Equal(new AnimationPick(true, ActionNumbers.Fishing), pick);
        Assert.Equal((UnitAnimations.Fishing, 0), (unit.AnimationNumber, unit.Facing));
        Assert.True(systems.AnimationChooser.Choose(unit, 0, 40) is { TurnEnds: false });
        unit.Animation = UnitAnimations.CastingLine;
        Assert.True(systems.AnimationChooser.Choose(unit, 0, 40).TurnEnds);
    }

    [Fact]
    public void Choose_AnIdleUnitWithALongRollMayStartChatting()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 18);
        Unit mate = UnitNeedsTests.Adult(systems.World, 19);
        systems.World.Map.SetCell(new GridPoint(mate.X, mate.Y), MapCell.Empty);
        (mate.X, mate.Y) = (unit.X + 1, unit.Y + 1);
        systems.World.Map.SetCell(new GridPoint(mate.X, mate.Y), MapCell.UnitAt(19));

        Assert.True(systems.AnimationChooser.Choose(unit, 0, 99).TurnEnds);

        Assert.Equal((UnitAnimations.Chatting, 1, 5), (unit.AnimationNumber, unit.Facing, mate.Facing));
        Assert.True(unit.BuildingSlot < -1);
    }
}
