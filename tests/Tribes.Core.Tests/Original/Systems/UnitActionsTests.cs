using System.Linq;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitActions"/> with the original, plus edge cases.</summary>
public sealed class UnitActionsTests
{
    private const int UnitsPerSample = 4;
    private const int ActionsPerUnit = 3;
    private const int NodeCodeBase = -61;
    private static readonly int[] Actions = { 0, 13, 17, 56, 69, 78, 84, 87, 99 };

    private readonly ITestOutputHelper output;

    public UnitActionsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SetActionAndCarriedItem_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), UnitsPerSample))
            {
                int current = LegacySamples.Field(sample.Game, 28, id);
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, ActionsPerUnit).Append(current))
                {
                    run.Check(sample, $"SetAction unit {id} action {action}",
                        g => { g.world_a.i_v = id; g.world_a.a_IB(id, (sbyte)action); },
                        s => s.UnitActions.SetAction(s.World.Unit(id), action));
                }
                run.Check(sample, $"UpdateCarriedItem unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.C_(); },
                    s => s.UnitActions.UpdateCarriedItem(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void WalkStandStop_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), UnitsPerSample))
            {
                run.Check(sample, $"WalkingAnimation unit {id}",
                    g => (int)g.world_a.b_I_B(id),
                    s => UnitActions.WalkingAnimation(s.World.Unit(id)));
                run.Check(sample, $"Stand unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.P_(); },
                    s => UnitActions.Stand(s.World.Unit(id)));
                run.Check(sample, $"Stop unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.Q_(); },
                    s => s.UnitActions.Stop(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void MatchesTarget_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            int[] nodeCodes = Enumerable.Range(0, 50)
                .Where(i => game.bArr_e[Parity.EntityFields.NodeBase + (2 * Parity.EntityFields.NodeStride) + i] != 0)
                .Select(i => NodeCodeBase - i).ToArray();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                int target = LegacySamples.Field(game, 47, id);
                foreach (int code in SampleInputs.Pick(nodeCodes, id, 2).Append(target).Append(-1))
                {
                    run.Check(sample, $"MatchesTarget unit {id} code {code}",
                        g => { g.world_a.i_v = id; return g.world_a.c_B((sbyte)code); },
                        s => s.UnitActions.MatchesTarget(s.World.Unit(id), code));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Theory]
    [InlineData(5, 0, 0, 0, AnimationNumbers.ChildWalk)]
    [InlineData(20, 0, 0, 3, AnimationNumbers.Run)]
    [InlineData(20, 0, 0, 0, AnimationNumbers.Walk)]
    [InlineData(20, 2, 0, 0, AnimationNumbers.CarryWalk)]
    [InlineData(20, 0, UnitActions.StoneWeapon, 3, AnimationNumbers.CarryWalk)]
    public void WalkingAnimation_DependsOnAgeLoadAndRunning(int age, int carried, int weapon, int run, int expected)
    {
        Unit unit = TestWorlds.Empty().Unit(1);
        (unit.Age, unit.CarriedItem, unit.Weapon, unit.RunBonus) = (age, carried, weapon, run);

        Assert.Equal(expected, UnitActions.WalkingAnimation(unit));
        Assert.Equal(expected == AnimationNumbers.Run ? run : 0, unit.RunBonus);
    }

    [Fact]
    public void WalkingAnimation_HiddenUnitsCrawlUnlessRunningFree()
    {
        Unit unit = TestWorlds.Empty().Unit(1);
        (unit.Age, unit.Weapon, unit.Status) = (30, 9, UnitStatus.Hidden);

        Assert.Equal(AnimationNumbers.Crawl, UnitActions.WalkingAnimation(unit));
        unit.RunBonus = 3;
        Assert.Equal(AnimationNumbers.Run, UnitActions.WalkingAnimation(unit));
        unit.CarriedItem = 1;
        Assert.Equal(AnimationNumbers.Crawl, UnitActions.WalkingAnimation(unit));
        Assert.Equal(0, unit.RunBonus);
    }

    [Fact]
    public void SetAction_KeepsProgressWhenTheActionDoesNotChange()
    {
        var world = TestWorlds.Empty();
        var actions = new UnitActions(world);
        Unit unit = world.Unit(1);
        (unit.Action, unit.WorkProgress, unit.StepAndActionTicks) = (ActionNumbers.Idle, 7, 0x35);

        actions.SetAction(unit, ActionNumbers.Idle);
        Assert.Equal(7, unit.WorkProgress);
        actions.SetAction(unit, ActionNumbers.Fishing);
        Assert.Equal((0, 0x05, ActionNumbers.Fishing, 1), (unit.WorkProgress, unit.StepAndActionTicks, unit.Action, unit.EffectRecipient));
    }

    [Fact]
    public void UpdateCarriedItem_BuilderWaitingForMaterialKeepsItsLoad()
    {
        var world = TestWorlds.Empty();
        var actions = new UnitActions(world);
        Unit unit = world.Unit(3);
        (unit.Action, unit.Animation, unit.CarriedItem, unit.CarriedAmount) =
            (ActionNumbers.WaitForMaterial, AnimationNumbers.FirstMaterialWait, 2, 0);

        actions.UpdateCarriedItem(unit);
        Assert.Equal(2, unit.CarriedItem);
        unit.Animation = AnimationNumbers.Walk;
        actions.UpdateCarriedItem(unit);
        Assert.Equal(0, unit.CarriedItem);
    }
}
