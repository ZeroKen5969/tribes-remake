using System;
using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Core.Tests.Systems.WorkScenarios;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkMaterials"/> with the original.</summary>
public sealed class WorkMaterialsTests
{
    private static readonly int[] FetchActions = Enumerable.Range(28, 22).Append(99).ToArray();

    private readonly ITestOutputHelper output;

    public WorkMaterialsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void InStockFetchAndTakeWeapon_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 4))
            {
                foreach (int action in SampleInputs.Pick(FetchActions, (id * 3) + sample.Frame, 4))
                {
                    int a = action, variant = id + action + sample.Frame;
                    if (a == 99 && BusyBuilding(sample.Game, TribeOf(id)) == 0)
                    {
                        continue;
                    }
                    Action<GameCanvas> prepare = g => Prepare(g, id, a, variant);
                    run.Check(sample, $"InStock unit {id} action {a}", prepare,
                        g => { BeginTurn(g, id); return g.world_a.b_IZI(a - 28, true, id); },
                        s => s.WorkMaterials.InStock(s.World.Unit(id), a - 28));
                    run.Check(sample, $"Fetch unit {id} action {a}", prepare,
                        g => { BeginTurn(g, id); return g.world_a.a_IZI(a, true, id); },
                        s => Fetch(s, id));
                }
                if (BusyBuilding(sample.Game, TribeOf(id)) != 0)
                {
                    foreach (int stock in new[] { 0, 500 })
                    {
                        int st = stock;
                        Action<GameCanvas> upgrade = g => { Prepare(g, id, 99, 1); g.sArr2_a[TribeOf(id)][2] = g.sArr2_a[TribeOf(id)][3] = g.sArr2_a[TribeOf(id)][4] = (short)st; };
                        run.Check(sample, $"InStock unit {id} upgrade stock {st}", upgrade,
                            g => { BeginTurn(g, id); return g.world_a.b_IZI(71, true, id); },
                            s => s.WorkMaterials.InStock(s.World.Unit(id), 71));
                        run.Check(sample, $"Fetch unit {id} upgrade stock {st}", upgrade,
                            g => { BeginTurn(g, id); return g.world_a.a_IZI(99, true, id); },
                            s => Fetch(s, id));
                    }
                }
                int melee = id % 10, ranged = (id * 3) % 10;
                run.Check(sample, $"TakeWeapon unit {id}", g => { Set(g, 17, id, melee); Set(g, 16, id, ranged); Set(g, Weapon, id, 9); },
                    g => { BeginTurn(g, id); g.world_a.L_(); },
                    s => s.WorkMaterials.TakeWeapon(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    /// <summary>A fetch that fails makes the unit pause and look for its next target, as the original's fetch does.</summary>
    private static bool Fetch(Simulation.RuleSystems s, int id)
    {
        Tribes.Core.World.Unit unit = s.World.Unit(id);
        if (s.WorkMaterials.Fetch(unit))
        {
            return true;
        }
        s.ActionPauses.Pause(unit);
        s.TargetCycle.NextTarget(TargetCycleTests.Turn(s, id));
        return false;
    }

    /// <summary>The unit fetches <paramref name="action"/>; some variants empty a stock or work for a building being upgraded or repaired.</summary>
    private static void Prepare(GameCanvas g, int id, int action, int variant)
    {
        Set(g, ActionField, id, action);
        int tribe = TribeOf(id);
        if (variant % 3 == 0)
        {
            g.sArr2_a[tribe][2 + (variant % 3)] = (short)(variant % 5);
        }
        if (action == 99)
        {
            WorkAt(g, id, BusyBuilding(g, tribe));
        }
    }

    /// <summary>A standing building of <paramref name="tribe"/> being upgraded or repaired, or 0.</summary>
    private static int BusyBuilding(GameCanvas g, int tribe) =>
        Enumerable.Range((tribe * 30) + 1, 30).FirstOrDefault(b =>
            BuildingField(g, BuildingHitPoints, b) > 0 && g.bArr_e[8383 + (4 * 61) + b] != 0);
}
