using System;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Core.Tests.Systems.WorkScenarios;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="TargetCycle"/> with the original.</summary>
public sealed class TargetCycleTests
{
    private static readonly int[] ApproachActions = { -1, 69, 20, 10, 0, 59, 5 };
    private static readonly int[] NextActions = { -1, 17, 60, 83, 11, 14, 27, 65, 21, 69, 85 };

    private readonly ITestOutputHelper output;

    public TargetCycleTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ApproachNextTargetFallBack_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 4))
            {
                foreach (int action in SampleInputs.Pick(ApproachActions, id + sample.Frame, 3))
                {
                    Action<GameCanvas>? prepare = action < 0 ? null : g => Set(g, ActionField, id, action);
                    run.Check(sample, $"Approach unit {id} action {action}", prepare,
                        g => { BeginTurn(g, id); g.world_a.p_(); },
                        s => s.TargetCycle.Approach(Turn(s, id)));
                }
                foreach (int action in SampleInputs.Pick(NextActions, id + sample.Frame, 4))
                {
                    if (action == 65 && Get(sample.Game, 34, id) <= 0)
                    {
                        continue;
                    }
                    Action<GameCanvas>? prepare = action < 0 ? null : g => { Set(g, ActionField, id, action); Set(g, CarriedAmount, id, 2); };
                    run.Check(sample, $"NextTarget unit {id} action {action}", prepare,
                        g => { BeginTurn(g, id); g.world_a.N_(); },
                        s => s.TargetCycle.NextTarget(Turn(s, id)));
                    run.Check(sample, $"FallBack unit {id} action {action}", prepare,
                        g => { BeginTurn(g, id); g.world_a.O_(); },
                        s => s.TargetCycle.FallBack(Turn(s, id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 900);
    }

    [Fact]
    public void SpellsAndArmouryOrders_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            int[] live = Live(sample.Restore()).ToArray();
            foreach (int id in Some(sample, 3))
            {
                int target = live.FirstOrDefault(t => t != id);
                if (FindBuilding(sample.Game, TribeOf(id), Sanctuary) == 0)
                {
                    continue;
                }
                for (int spell = 0; spell < 4; spell++)
                {
                    int s0 = spell;
                    Action<GameCanvas> doctor = g => WitchDoctor(g, id, s0, target);
                    run.Check(sample, $"HandOut doctor {id} spell {spell}", doctor,
                        g => { g.world_a.i_u = TribeOf(id); g.world_a.a_I_I(0); },
                        s => s.TargetCycle.HandOutOrders(TribeOf(id)));
                    run.Check(sample, $"NextSpell doctor {id} spell {spell}", g => { doctor(g); Set(g, ActionField, id, 94 + s0); },
                        g => { BeginTurn(g, id); g.world_a.ap_(); },
                        s => s.TargetCycle.NextSpell(Turn(s, id)));
                    run.Check(sample, $"FallBackSpell doctor {id} spell {spell}", g => { doctor(g); Set(g, ActionField, id, 94 + s0); },
                        g => { BeginTurn(g, id); g.world_a.O_(); },
                        s => s.TargetCycle.FallBack(Turn(s, id)));
                    run.Check(sample, $"CloseOrder doctor {id} spell {spell}", g => { doctor(g); Set(g, ActionField, id, 94 + s0); },
                        g => { BeginTurn(g, id); g.world_a.ao_(); },
                        s => s.TargetCycle.CloseSpellOrder(s.World.Unit(id)));
                }
                int product = id % 9;
                run.Check(sample, $"HandOut armoury {id} product {product}", g => ArmouryWorker(g, id, product, 2),
                    g => { g.world_a.i_u = TribeOf(id); g.world_a.a_I_I(0); },
                    s => s.TargetCycle.HandOutOrders(TribeOf(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void NextTarget_HandsTheHutPositionOn()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Building hut = systems.World.Building(1);
        (hut.HitPoints, hut.Kind, hut.Level, hut.FirstOccupant) = (10, 2, 1, 1);
        Tribes.Core.World.Unit leaving = systems.World.Unit(1), staying = systems.World.Unit(2);
        (leaving.TargetIndex, leaving.BuildingSlot, leaving.NextOccupantOrCellObject, leaving.HitPoints) = (1, 0, 2, 10);
        (staying.BuildingSlot, staying.HitPoints) = (3, 10);
        systems.TargetCycle.NextTarget(Turn(systems, 1));
        Assert.Equal(3, leaving.BuildingSlot);
        Assert.Equal(0, staying.BuildingSlot);
    }

    [Fact]
    public void FallBack_WithoutAStoreLooksForATarget()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit unit = systems.World.Unit(1);
        (unit.X, unit.Y, unit.HitPoints, unit.Action) = (3, 3, 10, 20);
        systems.World.Map.SetCell(new Tribes.Core.Content.GridPoint(3, 3), Tribes.Core.Maps.MapCell.UnitAt(1));
        systems.TargetCycle.FallBack(Turn(systems, 1));
        Assert.Equal(-6, unit.TargetIndex);
        Assert.Equal((3, 3), (unit.DestinationX, unit.DestinationY));
    }

    /// <summary>Starts the turn of unit <paramref name="id"/> as the units' update does.</summary>
    internal static UnitTurn Turn(RuleSystems s, int id) => s.UnitGoals.StartTurn(s.World.Unit(id));
}
