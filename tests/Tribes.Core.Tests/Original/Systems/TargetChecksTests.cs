using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="TargetChecks"/> (order refusals) with the original, plus edge cases.</summary>
public sealed class TargetChecksTests
{
    private const int UnitsPerSample = 8;
    private const int CellsPerSample = 16;

    private readonly ITestOutputHelper output;

    public TargetChecksTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CheckOrder_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            GridPoint[] cells = SampleInputs.Spread(TargetCells(game), CellsPerSample).ToArray();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                int tribe = TribeOf(id);
                run.Check(sample, $"CheckOrder unit {id}",
                    g => string.Join(";", cells.Select(c =>
                    {
                        g.world_a.i_v = id;
                        g.world_a.c_IIII(c.X, c.Y, tribe, 1);
                        g.world_a.b_k = g.world_a.bArr_k[0];
                        int refusal = g.world_a.a__B();
                        return $"{refusal},{g.world_a.b_k}";
                    })),
                    s => string.Join(";", cells.Select(c =>
                    {
                        OrderCheck check = s.TargetChecks.CheckOrder(s.World.Unit(id), s.CellClassifier.ProbeCell(c, tribe));
                        return $"{check.RefusalHint},{check.Category}";
                    })));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void CheckOrder_RefusesWhatTheUnitCannotDo()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(2);
        (unit.Age, unit.Weapon, unit.Action) = (20, 9, 69);
        Building building = world.Building(3);
        building.HitPoints = 10;

        Assert.Equal(new OrderCheck(6, CellCategories.Building), Check(systems, unit, building, kind: 1));
        unit.Weapon = 0;
        Assert.Equal(new OrderCheck(7, CellCategories.Building), Check(systems, unit, building, kind: 5));
        Assert.Equal(new OrderCheck(9, CellCategories.Building), Check(systems, unit, building, kind: 10));
        Assert.Equal(new OrderCheck(10, CellCategories.Building), Check(systems, unit, building, kind: 3));
        building.OperatorOrBeds = 7;
        Assert.Equal(new OrderCheck(TargetChecks.NoRefusal, CellCategories.OccupiedBuilding), Check(systems, unit, building, kind: 4));
        building.WorkMode = BuildingWorkMode.Upgrading;
        Assert.Equal(new OrderCheck(TargetChecks.NoRefusal, CellCategories.BusyBuilding), Check(systems, unit, building, kind: 4));
        building.WorkMode = BuildingWorkMode.Normal;
        building.Aux = 3;
        Assert.Equal(new OrderCheck(13, CellCategories.Building), Check(systems, unit, building, kind: 2));
        unit.Age = 5;
        Assert.Equal(new OrderCheck(11, CellCategories.Building), Check(systems, unit, building, kind: 4));
        Assert.Equal(new OrderCheck(TargetChecks.NoRefusal, CellCategories.Building), Check(systems, unit, world.Building(33), kind: 4, otherSide: true));
    }

    [Fact]
    public void CheckOrder_FishingNeedsASpear()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(2);
        var fishing = new CellProbe(CellCategories.ResourceNode, NodeKinds.FishingSpot, 0, false);

        unit.Weapon = 0;
        Assert.Equal(new OrderCheck(8, CellCategories.ResourceNode), systems.TargetChecks.CheckOrder(unit, fishing));
        unit.Weapon = 2;
        Assert.Equal(new OrderCheck(TargetChecks.NoRefusal, CellCategories.ResourceNode), systems.TargetChecks.CheckOrder(unit, fishing));
    }

    private static OrderCheck Check(RuleSystems systems, Unit unit, Building building, int kind, bool otherSide = false)
    {
        building.Kind = kind;
        return systems.TargetChecks.CheckOrder(unit, new CellProbe(CellCategories.Building, kind, building.Id, otherSide));
    }

    /// <summary>Cells with buildings, nodes and units on them.</summary>
    private static IEnumerable<GridPoint> TargetCells(GameCanvas game)
    {
        for (int y = 0; y < game.s_d; y++)
        {
            for (int x = 0; x < game.s_c; x++)
            {
                int code = CodeAt(game, x, y);
                if (code is < 0 and >= -110 || code is > 0 and <= 122)
                {
                    yield return new GridPoint(x, y);
                }
            }
        }
    }
}
