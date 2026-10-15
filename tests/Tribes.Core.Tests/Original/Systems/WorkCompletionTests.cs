using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkCompletion"/> with the original.</summary>
public sealed class WorkCompletionTests
{
    private static readonly int[] Actions =
    {
        0, 3, 5, 2, 20, 21, 26, 50, 79, 60, 17, 83, 84, 85, 10, 16, 87, 110, 109,
    };

    private static readonly int[] SavedActions = { 69, 44, 30, 99, 10 };

    private readonly ITestOutputHelper output;

    public WorkCompletionTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void FinishAndTakeUpNext_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 4))
            {
                int enemy = EnemyOf(sample.Game, id);
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, 4))
                {
                    int a = action, saved = SavedActions[(id + action) % SavedActions.Length];
                    if (a is 109 or 110 && Get(sample.Game, 82, id) == 0)
                    {
                        continue;
                    }
                    Action<GameCanvas> prepare = g => Prepare(g, id, a, saved, enemy);
                    run.Check(sample, $"Finish unit {id} action {a}", prepare,
                        g => { BeginMeasuredTurn(g, id); return g.world_a.k_(); },
                        s => s.WorkCompletion.Finish(TargetCycleTests.Turn(s, id)));
                    run.Check(sample, $"TakeUpNext unit {id} action {a} saved {saved}", prepare,
                        g => { BeginMeasuredTurn(g, id); g.world_a.I_(); },
                        s => s.WorkCompletion.TakeUpNext(TargetCycleTests.Turn(s, id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    private static void Prepare(GameCanvas g, int id, int action, int saved, int enemy)
    {
        Set(g, ActionField, id, action);
        Set(g, SavedAction, id, saved);
        Set(g, OrderMode, id, (id + action) % 3 == 0 ? 2 : 0);
        if (action <= 9 && enemy > 0)
        {
            Set(g, TargetIndex, id, enemy);
            Set(g, TargetCellCode, id, enemy);
            Set(g, DestinationX, id, Get(g, X, enemy));
            Set(g, DestinationY, id, Get(g, Y, enemy));
        }
    }
}
