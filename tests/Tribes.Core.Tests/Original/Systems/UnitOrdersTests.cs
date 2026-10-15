using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitOrders"/> with the original, plus edge cases.</summary>
public sealed class UnitOrdersTests
{
    private const int XField = 0, YField = 1, DestinationXField = 31, DestinationYField = 32;
    private const int BuildingXField = 0, BuildingYField = 1, KindField = 6;
    private const int UnitsPerSample = 3, CellsPerUnit = 3;

    private readonly ITestOutputHelper output;

    public UnitOrdersTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Cells worth sending units to: buildings, units of both tribes, nodes and empty ground.</summary>
    private static List<GridPoint> Targets(GameCanvas game)
    {
        var cells = new List<GridPoint>();
        foreach (int b in LegacySamples.UsedBuildings(game))
        {
            cells.Add(new GridPoint(LegacySamples.BuildingField(game, BuildingXField, b), LegacySamples.BuildingField(game, BuildingYField, b)));
        }
        foreach (int u in SampleInputs.Spread(LegacySamples.LiveUnits(game), 6))
        {
            cells.Add(new GridPoint(LegacySamples.Field(game, XField, u), LegacySamples.Field(game, YField, u)));
        }
        for (int i = 0; i < 50; i++)
        {
            if (game.bArr_e[Parity.EntityFields.NodeBase + (2 * Parity.EntityFields.NodeStride) + i] != 0)
            {
                cells.Add(new GridPoint(game.bArr_e[Parity.EntityFields.NodeBase + i], game.bArr_e[Parity.EntityFields.NodeBase + 50 + i]));
            }
        }
        cells.Add(new GridPoint(game.s_c / 2, game.s_d / 2));
        cells.Add(new GridPoint(1, 1));
        return cells;
    }

    [Fact]
    public void PickAndTakeOrders_MatchTheOriginal()
    {
        var run = new ParityRun();
        int n = 0;
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            List<GridPoint> targets = Targets(game);
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                foreach (GridPoint cell in Enumerable.Range(0, CellsPerUnit).Select(i => targets[(n * 7 + i * 13) % targets.Count]))
                {
                    bool retarget = n++ % 2 == 0;
                    int mode = n % 3;
                    int tribe = (id - 1) / 50;
                    run.Check(sample, $"PickAction unit {id} cell {cell} retarget {retarget}",
                        g => { g.world_a.i_v = id; g.world_a.i_u = tribe; g.world_a.a_IIZ(cell.X, cell.Y, retarget); return (g.world_a.b_l, g.world_a.b_k, g.world_a.b_m); },
                        s => { OrderChoice c = s.UnitOrders.PickAction(s.World.Unit(id), cell, retarget); return ((sbyte)c.RefusalHint, (sbyte)c.Category, (sbyte)c.Action); });
                    run.Check(sample, $"TakeOrder unit {id} cell {cell} retarget {retarget} mode {mode}",
                        g => { g.world_a.i_v = id; g.world_a.i_u = tribe; g.world_a.a_IIZ(cell.X, cell.Y, retarget); g.world_a.h_I(mode); },
                        s =>
                        {
                            Tribes.Core.World.Unit unit = s.World.Unit(id);
                            s.UnitOrders.TakeOrder(unit, s.UnitOrders.PickAction(unit, cell, retarget), mode);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void FinishOrder_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            var insiders = LegacySamples.UsedBuildings(game).SelectMany(b => OccupancyTests.Occupants(game, b));
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample).Concat(SampleInputs.Spread(insiders, 2)).Distinct())
            {
                foreach (int mode in SampleInputs.Pick(new[] { 0, 1, 2, 3 }, id + sample.Frame, 2))
                {
                    int tribe = (id - 1) / 50;
                    run.Check(sample, $"FinishOrder unit {id} mode {mode}",
                        g => { g.world_a.i_v = id; g.world_a.i_u = tribe; g.world_a.g_I(mode); },
                        s => s.UnitOrders.FinishOrder(s.World.Unit(id), mode));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 250);
    }

    [Fact]
    public void EntrancesAndSettlingInside_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            var live = LegacySamples.LiveUnits(game).ToList();
            foreach (int b in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 4))
            {
                int kind = LegacySamples.BuildingField(game, KindField, b);
                if (kind >= 20)
                {
                    continue;
                }
                foreach (int id in SampleInputs.Spread(live, 2))
                {
                    int tribe = (id - 1) / 50;
                    run.Check(sample, $"PickEntrance unit {id} building {b}",
                        g => { g.world_a.i_v = id; g.world_a.i_u = tribe; g.world_a.b_II(kind, b); },
                        s => s.UnitOrders.PickEntrance(s.World.Unit(id), kind, b));
                }
            }
            foreach (int b in LegacySamples.UsedBuildings(game))
            {
                foreach (int id in OccupancyTests.Occupants(game, b))
                {
                    int tribe = (id - 1) / 50;
                    run.Check(sample, $"SettleInside unit {id} building {b}",
                        g => { g.world_a.i_v = id; g.world_a.i_u = tribe; g.world_a.U_(); },
                        s => s.UnitOrders.SettleInside(s.World.Unit(id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void FinishOrder_RefusedOrderStopsTheUnit()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit unit = systems.World.Unit(2);
        systems.World.FramesSinceCombat = 50;
        (unit.HitPoints, unit.Age, unit.X, unit.Y, unit.OrderMode, unit.Action) = (10, 5, 3, 3, OrderModes.Walk, ActionNumbers.ChildIdle);
        systems.World.Map.SetCell(new GridPoint(3, 3), Tribes.Core.Maps.MapCell.UnitAt(2));
        int hut = systems.BuildingPlacement.Place(new PlacementRequest(0, BuildingKindNumbers.Hut, new GridPoint(8, 8), 1, true)).Slot;
        Assert.True(hut > 0);
        (unit.DestinationX, unit.DestinationY, unit.TargetCellCode) = (8, 8, -hut);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                systems.World.Fog(0).SetExplored(new GridPoint(x, y), true);
            }
        }

        systems.UnitOrders.FinishOrder(unit, 1);

        Assert.Equal((3, 3, OrderModes.Keep), (unit.DestinationX, unit.DestinationY, unit.OrderMode));
        Assert.Equal(ActionNumbers.ChildIdle, unit.Action);
        Assert.Contains(systems.TakeEvents(), e => e is Tribes.Core.Systems.Notices.HintEvent);
    }
}
