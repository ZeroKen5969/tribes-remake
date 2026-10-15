using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WildlifeAndNodes"/> (node placement, animal birth and removal) with the original, plus edge cases.</summary>
public sealed class WildlifeAndNodesTests
{
    private const int CarcassKind = 6;
    private const int NodeKindField = 2;
    private const int CellsPerSample = 3;

    private readonly ITestOutputHelper output;

    public WildlifeAndNodesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void PlaceNode_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            if (Enumerable.Range(0, 50).All(i => NodeField(game, NodeKindField, i) != 0))
            {
                continue;
            }
            foreach (GridPoint cell in SampleInputs.Spread(EmptyCells(game), CellsPerSample))
            {
                int amount = 10 + (cell.X % 15);
                run.Check(sample, $"PlaceNode at {cell}",
                    g => g.a_IIIB(CarcassKind, cell.X, cell.Y, (sbyte)amount),
                    s => s.WildlifeAndNodes.PlaceNode(CarcassKind, cell, amount));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 90);
    }

    [Fact]
    public void SpawnAnimal_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            if (LiveAnimals(game).Count() >= 22)
            {
                continue;
            }
            foreach (GridPoint cell in SampleInputs.Spread(EmptyCells(game), CellsPerSample))
            {
                int kind = cell.X % 3, zone = cell.Y % 3;
                run.Check(sample, $"SpawnAnimal kind {kind} zone {zone} at {cell}",
                    g => g.b_IIII(kind, cell.X, cell.Y, zone),
                    s => s.WildlifeAndNodes.SpawnAnimal(kind, cell, zone));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 90);
    }

    [Fact]
    public void RemoveAnimal_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (int index in LiveAnimals(sample.Restore()))
            {
                run.Check(sample, $"RemoveAnimal {index}",
                    g => g.world_a.l_I(index),
                    s => s.WildlifeAndNodes.RemoveAnimal(s.World.Animal(index)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 80);
    }

    [Fact]
    public void PlaceNode_WithEverySlotTaken_ReusesTheFirstCarcass()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int i = 0; i < world.Map.ResourceNodes.Count; i++)
        {
            world.Map.PlaceResourceNode(i == 7 ? CarcassKind : 1, new GridPoint(i % 30, 1 + (i / 30)), 5);
        }

        ResourceNode? node = systems.WildlifeAndNodes.PlaceNode(CarcassKind, new GridPoint(10, 20), 25);

        Assert.NotNull(node);
        Assert.Equal(7, node.Index);
        Assert.Equal(MapCell.Empty, world.Map.CellAt(new GridPoint(7, 1)));
        Assert.Equal(MapCell.Node(7), world.Map.CellAt(new GridPoint(10, 20)));
        Assert.Equal(25, node.Amount);
    }

    [Fact]
    public void PlaceNode_WithEverySlotTakenByMapNodes_PlacesNothing()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int i = 0; i < world.Map.ResourceNodes.Count; i++)
        {
            world.Map.PlaceResourceNode(1, new GridPoint(i % 30, 1 + (i / 30)), 5);
        }

        Assert.Null(systems.WildlifeAndNodes.PlaceNode(CarcassKind, new GridPoint(10, 20), 25));
        Assert.Equal(MapCell.Empty, world.Map.CellAt(new GridPoint(10, 20)));
    }

    [Fact]
    public void SpawnAnimal_WithEverySlotTaken_SpawnsNothing()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            world.Animal(i).HitPoints = 5;
        }
        long state = world.Random.State;

        Assert.Null(systems.WildlifeAndNodes.SpawnAnimal(1, new GridPoint(3, 3), 0));
        Assert.Equal(state, world.Random.State);
        Assert.Equal(0, world.AnimalZoneCount(0));
    }

    private static IEnumerable<GridPoint> EmptyCells(GameCanvas game)
    {
        for (int y = 2; y < game.s_d - 2; y += 3)
        {
            for (int x = 2; x < game.s_c - 2; x += 3)
            {
                if (CodeAt(game, x, y) == 0)
                {
                    yield return new GridPoint(x, y);
                }
            }
        }
    }
}
