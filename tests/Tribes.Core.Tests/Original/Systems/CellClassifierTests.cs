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

/// <summary>Parity of <see cref="CellClassifier"/> (cell probes) with the original, plus edge cases.</summary>
public sealed class CellClassifierTests
{
    private const int UnitsPerSample = 5;

    private readonly ITestOutputHelper output;

    public CellClassifierTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ProbeCell_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                GridPoint at = UnitCell(game, id);
                GridPoint[] cells = Around(game, at.X, at.Y, 8, 3).ToArray();
                foreach (int tribe in new[] { 0, 1 })
                {
                    run.Check(sample, $"ProbeCell around {id} tribe {tribe}",
                        g => string.Join(";", cells.Select(c =>
                        {
                            g.world_a.c_IIII(c.X, c.Y, tribe, 1);
                            return Original(g);
                        })),
                        s => string.Join(";", cells.Select(c => Text(s.CellClassifier.ProbeCell(c, tribe)))));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void ProbeTarget_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            int[] units = LegacySamples.LiveUnits(game).ToArray();
            run.Check(sample, "ProbeTarget",
                g => string.Join(";", units.Select(id =>
                {
                    GridPoint to = UnitDestination(g, id);
                    g.world_a.i_v = id;
                    g.world_a.c_IIII(to.X, to.Y, TribeOf(id), 0);
                    return Original(g);
                })),
                s => string.Join(";", units.Select(id =>
                {
                    Unit unit = s.World.Unit(id);
                    var to = new GridPoint(unit.DestinationX, unit.DestinationY);
                    return Text(s.CellClassifier.ProbeTarget(unit, to, unit.Tribe));
                })));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void Probe_DestroyedUnitsReadAsBlockedCells()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        var cell = new GridPoint(3, 3);
        world.Map.SetCell(cell, MapCell.UnitAt(55));

        CellProbe probe = systems.CellClassifier.ProbeCell(cell, 1);

        Assert.Equal(CellCategories.Other, probe.Category);
        world.Unit(55).HitPoints = 10;
        Assert.Equal(new CellProbe(CellCategories.Unit, 0, 55, false), systems.CellClassifier.ProbeCell(cell, 1));
        Assert.True(systems.CellClassifier.ProbeCell(cell, 0) is { Category: CellCategories.Empty });
        world.Fog(0).SetExplored(cell, true);
        world.Fog(0).SetVisible(cell, true);
        Assert.Equal(new CellProbe(CellCategories.Unit, 0, 55, true), systems.CellClassifier.ProbeCell(cell, 0));
    }

    [Fact]
    public void Probe_ReadsBuildingsAndNodes()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Building(31).HitPoints = 20;
        world.Building(31).Kind = 4;
        world.Map.SetCell(new GridPoint(2, 2), MapCell.BuildingAt(31));
        world.Map.PlaceResourceNode(5, new GridPoint(4, 4), 10);

        Assert.Equal(new CellProbe(CellCategories.Building, 4, 31, false), systems.CellClassifier.ProbeCell(new GridPoint(2, 2), 1));
        world.Fog(0).SetExplored(new GridPoint(2, 2), true);
        Assert.Equal(new CellProbe(CellCategories.Building, 4, 31, true), systems.CellClassifier.ProbeCell(new GridPoint(2, 2), 0));
        Assert.Equal(new CellProbe(CellCategories.ResourceNode, 5, 0, false), systems.CellClassifier.ProbeCell(new GridPoint(4, 4), 1));
    }

    private static string Original(GameCanvas g) =>
        $"{g.world_a.bArr_k[0]},{g.world_a.bArr_k[1]},{g.world_a.bArr_k[2]},{(g.world_a.bArr_k[3] != 0 ? 1 : 0)}";

    private static string Text(CellProbe probe) => $"{probe.Category},{probe.SubKind},{probe.Slot},{(probe.OtherSide ? 1 : 0)}";
}
