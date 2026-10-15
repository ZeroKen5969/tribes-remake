using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Interruptions"/> with the original, plus edge cases.</summary>
public sealed class InterruptionsTests
{
    private static readonly int[] Actions = { -1, 79, 83, 84, 85, 59 };

    private readonly ITestOutputHelper output;

    public InterruptionsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void HoldsOn_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 4))
            {
                foreach (int action in SampleInputs.Pick(Actions, id, 3))
                {
                    int set = action < 0 ? LegacySamples.Field(game, UnitSampling.ActionField, id) : action;
                    foreach (bool byOrder in new[] { false, true })
                    {
                        int frame = byOrder ? -1 : 0;
                        run.Check(sample, $"HoldsOn unit {id} action {set} byOrder {byOrder}",
                            g =>
                            {
                                UnitSampling.Set(g, UnitSampling.ActionField, id, set);
                                return UnitSampling.Turn(g, id, 0).a_BZ((sbyte)frame, byOrder);
                            },
                            s =>
                            {
                                s.World.Unit(id).Action = set;
                                return s.Interruptions.HoldsOn(s.World.Unit(id), frame, byOrder);
                            });
                    }
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void HoldsOn_ARestingUnitResumesAndAnOrderedWorkerGetsUp()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit rester = UnitNeedsTests.Adult(systems.World, 20);
        rester.Action = 83;
        Assert.True(systems.Interruptions.HoldsOn(rester, 4, byOrder: false));
        Assert.Equal((85, 4), (rester.Action, rester.AnimationTick));

        Unit cook = UnitNeedsTests.Adult(systems.World, 21);
        (cook.AssignedTask, cook.PathLength) = (54, 5);
        Assert.False(systems.Interruptions.HoldsOn(cook, 0, byOrder: false));
        Assert.True(systems.Interruptions.HoldsOn(cook, 2, byOrder: true));
        Assert.Equal((UnitAnimations.ChangingWorkplace, 0, 2), (cook.AnimationNumber, cook.PathLength, cook.AnimationTick));
    }
}
