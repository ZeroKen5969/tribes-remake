using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ActionSteps"/> with the original.</summary>
public sealed class ActionStepsTests
{
    private static readonly int[] Actions = { 0, 5, 10, 13, 16, 20, 28, 60, 69, 79, 80, 84, 85, 87, 94, 97, 99, 107 };

    private readonly ITestOutputHelper output;

    public ActionStepsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void End_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                foreach (int action in SampleInputs.Pick(Actions, (id * 3) + sample.Frame, 4))
                {
                    int a = action, k = id + action + sample.Frame;
                    bool stepEnded = k % 3 == 0;
                    if (!WorkStepsTests.Applicable(sample.Restore(), id, a))
                    {
                        continue;
                    }
                    Action<GameCanvas> prepare = g =>
                    {
                        WorkStepsTests.Prepare(g, id, a, k);
                        int ticks = LegacySamples.Content.Rules.Actions.Actions[a].ActionTicks;
                        Set(g, StepTicks, id, (Get(g, StepTicks, id) & 0x0F) | (Math.Min(ticks, 15) << 4));
                    };
                    run.Check(sample, $"End unit {id} action {a} ended {stepEnded}", prepare,
                        g => { BeginMeasuredTurn(g, id); g.world_a.z_d = stepEnded; g.world_a.y_(); },
                        s =>
                        {
                            UnitTurn turn = TargetCycleTests.Turn(s, id);
                            turn.StepEnded = stepEnded;
                            s.ActionSteps.End(turn);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }
}
