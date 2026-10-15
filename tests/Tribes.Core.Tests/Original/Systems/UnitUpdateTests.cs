using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitUpdate"/> with the original's unit loop: one unit's turn, and the whole units' part of a frame.</summary>
public sealed class UnitUpdateTests
{
    private const int Hidden = 2, KeepsTarget = 4, TowerGuardCategory = 16;

    private readonly ITestOutputHelper output;

    public UnitUpdateTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TakeTurn_MatchesTheOriginalUnitLoop()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 10))
            {
                run.Check(sample, $"Turn unit {id}", null,
                    g => OriginalTurn(g, id),
                    s =>
                    {
                        Tribes.Core.World.Unit unit = s.World.Unit(id);
                        if (unit.HitPoints != 0 && !s.UnitHousekeeping.Update(unit))
                        {
                            s.UnitUpdate.TakeTurn(s.UnitGoals.StartTurn(unit));
                        }
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void RunFrame_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            run.Check(sample, "Frame", null,
                g => g.world_a.e_(),
                s => { s.UnitUpdate.RunFrame(); });
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    /// <summary>
    /// The body of the original's unit loop for unit <paramref name="id"/>, with the registers its turn
    /// starts with set as the rewrite has them (the goal distance measured).
    /// </summary>
    private static void OriginalTurn(GameCanvas g, int id)
    {
        Tribes.Legacy.World w = g.world_a;
        BeginMeasuredTurn(g, id);
        if (Get(g, HitPoints, id) == 0 || w.a__Z())
        {
            return;
        }
        int trap = Get(g, 45, id);
        if (trap > -111 || trap < -122)
        {
            w.t_();
            if (Get(g, 75, id) > 3 && w.h_())
            {
                return;
            }
            if (g.i_c % 15 == (id & 7) && TribeOf(id) == 0 && Get(g, Age, id) >= 12)
            {
                w.B_();
            }
            if (w.b__Z())
            {
                return;
            }
        }
        w.v_();
        w.z_b = true;
        if (Get(g, 50, id) == 0 && Get(g, Age, id) >= 12 && Get(g, ActionField, id) != 50 && Get(g, ActionField, id) != 105)
        {
            w.z_a = FreeChoice(g, id);
            w.z_b = false;
            w.p__Z();
        }
        w.u_();
        if (Get(g, ProjectileFlightSteps, id) > 0)
        {
            w.R_();
        }
    }

    /// <summary>The original unit loop's "may act freely" condition, as it reads the registers.</summary>
    private static bool FreeChoice(GameCanvas g, int id)
    {
        Tribes.Legacy.World w = g.world_a;
        int action = Get(g, ActionField, id), mode = Get(g, OrderMode, id);
        bool stuck = mode == 1 && Get(g, PathLength, id) <= Get(g, 7, id) && w.i_z / 10 > w.i_B && (Get(g, Animation, id) & 255) > 7;
        bool free = mode == 0 || (w.b_I & (TribeOf(id) + 1)) != 0 || stuck;
        bool placed = g.bArr2_c[Get(g, Y, id)][Get(g, X, id)] == id
            || (w.bArr_b[w.sArr_a[41] + action] == TowerGuardCategory && w.bArr_b[w.sArr_a[18] + Get(g, Weapon, id)] > 0);
        return free && placed && (Get(g, 79, id) & Hidden) == 0 && (action > 9 || (w.bArr_b[w.sArr_a[27] + action] & KeepsTarget) == 0);
    }
}
