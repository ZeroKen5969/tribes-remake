using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="SpiralSearch"/> with the original's spiral search, plus its order and edges.</summary>
public sealed class SpiralSearchTests
{
    private const int UnitDestinationMode = 1, AnimalDestinationMode = 2;
    private static readonly int[] Rounds = { 4, 127 };

    private readonly ITestOutputHelper output;

    public SpiralSearchTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void FindEmptyCell_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All.Concat(WildlifeSamples.All.Where((_, i) => i % 6 == 0)))
        {
            var game = sample.Restore();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), 1))
            {
                var at = new GridPoint(AnimalField(game, AnimalX, animal), AnimalField(game, AnimalY, animal));
                foreach (int rounds in Rounds)
                {
                    run.Check(sample, $"FindEmptyCell animal {animal} {rounds}",
                        g => { g.world_a.i_v = animal; g.world_a.a_IIII_Z(AnimalDestinationMode, rounds, at.X, at.Y); },
                        s => SetAnimalDestination(s, animal, s.SpiralSearch.FindEmptyCell(at, rounds)));
                }
            }
            foreach (int unit in SampleInputs.Spread(LegacySamples.LiveUnits(game), 1))
            {
                GridPoint at = UnitCell(game, unit);
                run.Check(sample, $"FindEmptyCell unit {unit}",
                    g => { g.world_a.i_v = unit; g.world_a.a_IIII_Z(UnitDestinationMode, 21, at.X, at.Y); },
                    s => SetUnitDestination(s, unit, s.SpiralSearch.FindEmptyCell(at, 21)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Visit_GoesRoundTheCentreClockwiseFromTheEast()
    {
        GameWorld world = TestWorlds.Empty();
        var search = new SpiralSearch(world, new GridDistances(world.Content.Rules.Movement));
        var offsets = new List<(int, int)>();
        search.Visit(new GridPoint(10, 10), 3, cell =>
        {
            offsets.Add((cell.Dx, cell.Dy));
            return false;
        });
        Assert.Equal(new[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1), (2, -1), (2, 0), (2, 1), (2, 2) },
            offsets);
    }

    [Fact]
    public void Visit_SkipsCellsOutOfPlayAndStopsWhenAsked()
    {
        GameWorld world = TestWorlds.Empty(8, 8);
        var search = new SpiralSearch(world, new GridDistances(world.Content.Rules.Movement));
        var cells = new List<GridPoint>();
        bool stopped = search.Visit(new GridPoint(0, 0), 6, cell =>
        {
            cells.Add(cell.Cell);
            return cells.Count == 3;
        });
        Assert.True(stopped);
        Assert.All(cells, c => Assert.True(c.X >= 0 && c.Y >= 0));
        Assert.Equal(new GridPoint(1, 0), cells[0]);
        Assert.False(search.Visit(new GridPoint(0, 0), 0, _ => true));
    }

    [Fact]
    public void SpiralCell_MeasuresDistanceAndTheFacingAway()
    {
        GameWorld world = TestWorlds.Empty();
        var search = new SpiralSearch(world, new GridDistances(world.Content.Rules.Movement));
        SpiralCell? east = null;
        search.Visit(new GridPoint(10, 10), 1, cell =>
        {
            east = cell;
            return true;
        });
        Assert.Equal(1, east!.Value.Distance);
        Assert.Equal(4, east.Value.AwayFacing);
        Assert.Equal(0, Facings.Opposite(4));
    }

    [Fact]
    public void FindEmptyCell_IsNullWhenEverythingIsTaken()
    {
        GameWorld world = TestWorlds.Empty(3, 3);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                world.Map.SetCell(new GridPoint(x, y), MapCell.Of(CellContent.Impassable));
            }
        }
        var search = new SpiralSearch(world, new GridDistances(world.Content.Rules.Movement));
        Assert.Null(search.FindEmptyCell(new GridPoint(1, 1), 127));
    }

    private static void SetAnimalDestination(RuleSystems systems, int animal, GridPoint? cell)
    {
        if (cell is { } found)
        {
            systems.World.Animal(animal).DestinationX = found.X;
            systems.World.Animal(animal).DestinationY = found.Y;
        }
    }

    private static void SetUnitDestination(RuleSystems systems, int unit, GridPoint? cell)
    {
        if (cell is { } found)
        {
            systems.World.Unit(unit).DestinationX = found.X;
            systems.World.Unit(unit).DestinationY = found.Y;
        }
    }
}
