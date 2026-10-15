using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// No order can stop a match: orders of every kind with any fields, from every tribe of three-
/// and four-tribe matches on every custom map, are carried out or turned down without throwing,
/// and the same orders give the same states.
/// </summary>
public sealed class OrderFuzzTests
{
    private const int Ticks = 6000, LongTicks = 30000, CheckEvery = 500;

    /// <summary>The tower (kind 11): one cell, entered from the cells to its left and below it, as is its site.</summary>
    private const int Tower = 11;

    private readonly ITestOutputHelper output;

    public OrderFuzzTests(ITestOutputHelper output) => this.output = output;

    public static TheoryData<int, int, int, int> Matches() => new()
    {
        { 0, 3, 1, 1 },
        { 0, 4, 0, 2 },
        { 1, 2, 2, 3 },
        { 2, 3, 1, 4 },
        { 3, 4, 0, 5 },
        { 4, 2, 1, 6 },
    };

    [Theory]
    [MemberData(nameof(Matches))]
    public void RandomOrders_FromEveryTribe_NeverThrow_AndAreDeterministic(int map, int humans, int computers, int seed)
    {
        MatchSetup setup = MatchWorlds.Setup(map, seed, MatchWorlds.Controllers(humans, computers)) with { EndsWithLastSide = false };
        (List<ulong> hashes, int[] outcomes) = Run(setup, seed, Ticks);
        (List<ulong> again, int[] againOutcomes) = Run(setup, seed, Ticks);

        Assert.Equal(hashes, again);
        Assert.Equal(outcomes, againOutcomes);
        output.WriteLine($"map {map}: outcomes {string.Join(", ", outcomes.Select((n, i) => $"{(OrderOutcome)i}={n}"))}");
        // The fuzzer must reach the rules, not only the checks.
        Assert.True(outcomes[(int)OrderOutcome.Applied] > Ticks / 10, "too few orders were carried out");
        Assert.True(outcomes[(int)OrderOutcome.OutOfMap] > 0);
        Assert.True(outcomes[(int)OrderOutcome.Malformed] > 0);
    }

    [Theory]
    [Trait("Speed", "Long")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomOrders_InLongFourTribeMatches_NeverThrow(int map)
    {
        MatchSetup setup = MatchWorlds.Setup(map, 40 + map, MatchWorlds.Controllers(3, 1)) with { EndsWithLastSide = false };
        (_, int[] outcomes) = Run(setup, 40 + map, LongTicks);
        Assert.True(outcomes[(int)OrderOutcome.Applied] > LongTicks / 10, "too few orders were carried out");
    }

    [Fact]
    public void BuildingAtTheMapEdge_WithItsOnlyEntranceOnTheMapBlocked_SendsUnitsToACellOnTheMap()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 15, MatchWorlds.Controllers(2, 0)));
        GameWorld world = game.World;
        // Two tower sites on top of each other at the left edge: the upper one's only entrance on the map is under it.
        int row = Enumerable.Range(0, world.Map.PlayableSize.Height - 1)
            .First(y => world.Map.CellAt(new GridPoint(0, y)).IsEmpty && world.Map.CellAt(new GridPoint(0, y + 1)).IsEmpty);
        RevealMap(world, 0);
        TickResult placed = game.Step(TickInput.WithOrders(
            new PlaceBuilding(0, Tower, new GridPoint(0, row)), new PlaceBuilding(0, Tower, new GridPoint(0, row + 1))));
        Assert.Equal([OrderOutcome.Applied, OrderOutcome.Applied], placed.Outcomes);
        Building building = world.BuildingsOf(0).Select(b => b.Id)
            .Select(world.Building).First(b => b.Stands && b.X == 0 && b.Y == row);
        Unit unit = world.Unit(MatchWorlds.LiveAdults(world, 0).First());

        game.Rules.UnitOrders.PickEntrance(unit, building.Kind, building.Id);

        Assert.Equal((0, row + 1), (unit.DestinationX, unit.DestinationY));
        for (int tick = 0; tick < 3000; tick++)
        {
            game.Step();
        }
    }

    [Fact]
    public void BuildingWithNoEntranceOnTheMap_SendsUnitsToItsCorner()
    {
        // The custom maps have no room for this at their edges (an endless world may): the chief's hut, whose
        // entrances lie below it, is taken as standing on the bottom rows.
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 3, MatchWorlds.Controllers(2, 0)));
        GameWorld world = game.World;
        Building hut = world.Building(world.FirstBuildingSlot(0));
        hut.Y = world.Map.Size.Height - world.Content.Rules.Buildings.Kinds[hut.Kind].Height;
        Unit unit = world.Unit(MatchWorlds.LiveAdults(world, 0).First());

        game.Rules.UnitOrders.PickEntrance(unit, hut.Kind, hut.Id);

        Assert.Equal(BuildingKindNumbers.ChiefsHut, hut.Kind);
        Assert.Equal((hut.X, hut.Y), (unit.DestinationX, unit.DestinationY));
    }

    private static (List<ulong> Hashes, int[] Outcomes) Run(MatchSetup setup, int seed, int ticks)
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        var fuzzer = new OrderFuzzer(seed * 7919);
        var hashes = new List<ulong>();
        var outcomes = new int[(int)Enum.GetValues<OrderOutcome>().Max() + 1];
        for (int tick = 1; tick <= ticks; tick++)
        {
            foreach (OrderOutcome outcome in game.Step(fuzzer.Tick(game)).Outcomes)
            {
                outcomes[(int)outcome]++;
            }
            if (tick % CheckEvery == 0)
            {
                hashes.Add(game.StateHash());
            }
        }
        return (hashes, outcomes);
    }

    private static void RevealMap(GameWorld world, int tribe)
    {
        FogOfWar fog = world.Fog(tribe);
        for (int y = 0; y < world.Map.PlayableSize.Height; y++)
        {
            for (int x = 0; x < world.Map.PlayableSize.Width; x++)
            {
                fog.SetExplored(new GridPoint(x, y), true);
                fog.SetVisible(new GridPoint(x, y), true);
            }
        }
    }
}
