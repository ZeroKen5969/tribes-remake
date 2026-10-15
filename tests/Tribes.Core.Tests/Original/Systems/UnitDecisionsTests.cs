using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitDecisions"/> with the original.</summary>
public sealed class UnitDecisionsTests
{
    private const int IdleTicks = 50, RunBonus = 77, Status = 79, Variants = 14;

    private readonly ITestOutputHelper output;

    public UnitDecisionsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Decide_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                int enemy = EnemyOf(sample.Game, id);
                for (int v = 0; v < 4; v++)
                {
                    int variant = (id + sample.Frame + (v * 3)) % Variants;
                    run.Check(sample, $"Decide unit {id} variant {variant}", g => Prepare(g, id, variant, enemy),
                        g => { BeginMeasuredTurn(g, id); return g.world_a.b__Z(); },
                        s => { return s.UnitDecisions.Decide(TargetCycleTests.Turn(s, id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }

    /// <summary>
    /// The situations decided: as sampled, reconsidering idle, called to the rally point, everyone
    /// called out, gathering on, attacking, following an enemy, an attacker or a hunter whose
    /// target died, a tower guard whose target is out of range, a computer unit sent after an intruder,
    /// an army member without a weapon, a unit inside called out, a target out of sight.
    /// </summary>
    private static void Prepare(GameCanvas g, int id, int variant, int enemy)
    {
        if (variant == 0)
        {
            return;
        }
        Set(g, IdleTicks, id, 20);
        Set(g, RunBonus, id, 0);
        Set(g, OrderMode, id, 0);
        Set(g, OffsetX, id, 0);
        Set(g, OffsetY, id, 0);
        if (variant >= 5 && variant != 9 && enemy > 0)
        {
            Set(g, TargetIndex, id, enemy);
            Set(g, TargetCellCode, id, enemy);
        }
        switch (variant)
        {
            case 1:
                Set(g, ActionField, id, 69);
                break;
            case 2:
                Set(g, Status, id, Get(g, Status, id) | 0x20);
                break;
            case 3:
                g.z_N = true;
                break;
            case 4:
                Set(g, ActionField, id, 10);
                Set(g, TargetIndex, id, -1);
                Set(g, CarriedAmount, id, 2);
                break;
            default:
                PrepareTarget(g, id, variant, enemy);
                break;
        }
    }

    private static void PrepareTarget(GameCanvas g, int id, int variant, int enemy)
    {
        int[] actions = { 0, 1, 0, 2, 92, 69, 50, 69, 5 };
        bool inTower = g.bArr2_c[Get(g, Y, id)][Get(g, X, id)] != id && Get(g, TargetIndex, id) is >= 1 and <= 60
            && WorkScenarios.BuildingField(g, WorkScenarios.BuildingKind, Get(g, TargetIndex, id)) == WorkScenarios.Tower;
        if (variant == 9 && !inTower)
        {
            return;
        }
        Set(g, ActionField, id, actions[variant - 5]);
        switch (variant)
        {
            case 7 when enemy > 0:
                Set(g, HitPoints, enemy, 0);
                break;
            case 8:
                Set(g, Status, id, Get(g, Status, id) | (id % 2 == 0 ? 0x80 : 0));
                Set(g, TargetIndex, id, 0);
                break;
            case 9:
                Set(g, TargetIndex, id, enemy);
                Set(g, 18, id, 0);
                break;
            case 10:
                Set(g, Status, id, Get(g, Status, id) | 0x20);
                g.world_a.b_Q = 1;
                break;
            case 11:
                g.z_N = true;
                Set(g, Status, id, Get(g, Status, id) | 0x40);
                Set(g, Weapon, id, 9);
                break;
            case 12:
                Set(g, Status, id, Get(g, Status, id) | 0x20);
                Set(g, SubState, id, 1);
                break;
            case 13 when enemy > 0:
                Set(g, X, enemy, (Get(g, X, id) + 20) % g.s_c);
                Set(g, PathLength, id, 0);
                break;
        }
    }
}
