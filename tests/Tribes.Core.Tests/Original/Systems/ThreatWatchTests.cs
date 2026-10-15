using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ThreatWatch"/> with the original.</summary>
public sealed class ThreatWatchTests
{
    private static readonly int[] Actions = { -1, 69, 0, 5, 20, 59, 60, 92 };
    private static readonly int[] Weapons = { 9, 0, 1 };

    private readonly ITestOutputHelper output;

    public ThreatWatchTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Look_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                int enemy = EnemyOf(sample.Game, id);
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, 3))
                {
                    int a = action, k = id + action + sample.Frame;
                    bool free = k % 2 == 0, regular = k % 3 != 0, near = enemy > 0 && k % 4 != 1;
                    Action<GameCanvas> prepare = g =>
                    {
                        if (a >= 0)
                        {
                            Set(g, ActionField, id, a);
                        }
                        Set(g, Weapon, id, Weapons[k % Weapons.Length]);
                        if (near)
                        {
                            ActionTicksTests.PlaceNextTo(g, enemy, id);
                            Reveal(g, Get(g, X, enemy), Get(g, Y, enemy));
                        }
                        Situation(g, id, k % 4);
                    };
                    run.Check(sample, $"Look unit {id} action {a} free {free} regular {regular}", prepare,
                        g =>
                        {
                            BeginMeasuredTurn(g, id);
                            (g.world_a.z_a, g.world_a.z_b) = (free, !regular);
                            return g.world_a.p__Z();
                        },
                        s =>
                        {
                            UnitTurn turn = TargetCycleTests.Turn(s, id);
                            (turn.MayActFreely, turn.RegularDecision) = (free, regular);
                            return s.ThreatWatch.Look(turn);
                        });
                }
                bool settle = (id + sample.Frame) % 2 == 0;
                run.Check(sample, $"StopAtNextCell unit {id} settle {settle}", null,
                    g => { BeginTurn(g, id); g.world_a.a_IZ(id, settle); },
                    s => s.ThreatWatch.StopAtNextCell(s.World.Unit(id), settle));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    /// <summary>
    /// Variants of the situation: as prepared; an unarmed worker of the player (who runs for cover);
    /// the unit's tribe waits for the story (its units stand down); hidden (no calls).
    /// </summary>
    private static void Situation(GameCanvas g, int id, int variant)
    {
        switch (variant)
        {
            case 1:
                Set(g, Weapon, id, 9);
                Set(g, ActionField, id, 20);
                break;
            case 2:
                g.world_a.b_I = 3;
                g.world_a.b_f = 50;
                break;
            case 3:
                Set(g, 79, id, Get(g, 79, id) | 0x02);
                break;
        }
    }
}
