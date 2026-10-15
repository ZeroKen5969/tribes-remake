using System;
using System.Linq;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkSteps"/> with the original.</summary>
public sealed class WorkStepsTests
{
    private static readonly int[] Actions =
    {
        0, 2, 5, 10, 11, 12, 13, 14, 16, 17, 20, 23, 28, 37, 44, 60, 69, 79, 83, 84, 85, 87, 94, 95, 97, 99, 107, 108,
    };

    private readonly ITestOutputHelper output;

    public WorkStepsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Step_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                foreach (int action in SampleInputs.Pick(Actions, (id * 5) + sample.Frame, 4))
                {
                    int a = action, k = id + action + sample.Frame;
                    if (!Applicable(sample.Restore(), id, a))
                    {
                        continue;
                    }
                    Action<GameCanvas> prepare = g => Prepare(g, id, a, k);
                    run.Check(sample, $"Step unit {id} action {a} variant {k % 4}", prepare,
                        g => { BeginMeasuredTurn(g, id); return g.world_a.i_(); },
                        s => s.WorkSteps.Step(TargetCycleTests.Turn(s, id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }

    /// <summary>
    /// Whether <paramref name="action"/> can be set up for <paramref name="id"/>: a spell needs a
    /// sanctuary to work for, a capture an enemy child, a fetch of upgrade material a building being
    /// upgraded or repaired, an attack an enemy. Units inside a building being upgraded or repaired
    /// are left out (their next animation is the movement lane's).
    /// </summary>
    internal static bool Applicable(GameCanvas g, int id, int action)
    {
        int inside = Get(g, TargetIndex, id);
        if (g.bArr2_c[Get(g, Y, id)][Get(g, X, id)] != id && inside >= 1 && inside <= 60 && g.bArr_e[8383 + (4 * 61) + inside] != 0)
        {
            return false;
        }
        return action switch
        {
            >= 94 and <= 97 => WorkScenarios.FindBuilding(g, TribeOf(id), WorkScenarios.Sanctuary) != 0,
            107 => Live(g).Any(t => TribeOf(t) != TribeOf(id) && Get(g, Age, t) < 12),
            99 => BusyBuilding(g, TribeOf(id)) != 0,
            <= 9 => EnemyOf(g, id) != 0,
            _ => true,
        };
    }

    /// <summary>
    /// Puts <paramref name="id"/> into <paramref name="action"/> (as <see cref="Applicable"/> allows) with
    /// one of its own animations, its work progress at the cycle's end in half of the variants, and a
    /// load in some.
    /// </summary>
    internal static void Prepare(GameCanvas g, int id, int action, int variant)
    {
        PrepareTarget(g, id, action);
        g.world_a.i_v = id;
        g.world_a.a_IB(id, (sbyte)action);
        ActionDefinition definition = LegacySamples.Content.Rules.Actions.Actions[action];
        if (definition.AnimationChoices.Count > 0)
        {
            Set(g, Animation, id, definition.AnimationChoices[variant % definition.AnimationChoices.Count].Animation);
        }
        if (variant % 2 == 0)
        {
            Set(g, WorkProgress, id, Math.Min(definition.WorkCyclePoints, 127));
        }
        Set(g, CarriedAmount, id, variant % 4);
        Set(g, SavedAction, id, variant % 3 == 0 ? 69 : 30);
    }

    /// <summary>A standing building of <paramref name="tribe"/> being upgraded or repaired, or 0.</summary>
    internal static int BusyBuilding(GameCanvas g, int tribe) =>
        Enumerable.Range((tribe * 30) + 1, 30).FirstOrDefault(b =>
            WorkScenarios.BuildingField(g, WorkScenarios.BuildingHitPoints, b) > 0 && g.bArr_e[8383 + (4 * 61) + b] != 0);

    private static void PrepareTarget(GameCanvas g, int id, int action)
    {
        int target = action switch
        {
            >= 94 and <= 97 => Live(g).FirstOrDefault(t => t != id && TribeOf(t) == TribeOf(id)),
            107 => Live(g).FirstOrDefault(t => TribeOf(t) != TribeOf(id) && Get(g, Age, t) < 12),
            <= 9 => EnemyOf(g, id),
            _ => -1,
        };
        if (action is >= 94 and <= 97)
        {
            WorkScenarios.WitchDoctor(g, id, action - 94, target);
        }
        if (action == 99)
        {
            WorkScenarios.WorkAt(g, id, BusyBuilding(g, TribeOf(id)));
        }
        if (target >= 0)
        {
            Set(g, TargetIndex, id, target);
            Set(g, TargetCellCode, id, target);
        }
    }
}
