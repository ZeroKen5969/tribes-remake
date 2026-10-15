using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.Tests.Endless;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>The land of camps: what a computer defends, each tribe's own side and its room to build.</summary>
public sealed class CampLandTests
{
    private readonly ITestOutputHelper output;

    public CampLandTests(ITestOutputHelper output) => this.output = output;

    private static GameWorld Match(int map, int tribes, long seed = 7) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, seed, MatchWorlds.Controllers(Math.Min(2, tribes), Math.Max(0, tribes - 2)))).World;

    /// <summary>The cells within <paramref name="reach"/> of <paramref name="centre"/>.</summary>
    private static IEnumerable<GridPoint> Around(GridPoint centre, int reach)
    {
        for (int y = centre.Y - reach; y <= centre.Y + reach; y++)
        {
            for (int x = centre.X - reach; x <= centre.X + reach; x++)
            {
                yield return new GridPoint(x, y);
            }
        }
    }

    /// <summary>Cells on a people's tribe's side (or of its camp) that some computer defends.</summary>
    private static int PeoplesLandDefended(GameWorld world)
    {
        int reach = CampRoom.BuildReach(world);
        int found = 0;
        foreach (TribeState person in world.Tribes.Where(t => t.Controller.IsHumanRuled))
        {
            foreach (GridPoint cell in Around(person.Camp, reach).Where(c => CampLand.IsOnOwnSide(world, person.Index, c) || c == person.Camp))
            {
                found += world.Tribes.Count(t => t.Controller.RunsAi && CampLand.Defends(world, t.Index, cell));
            }
        }
        return found;
    }

    private static double LeastDistance(GameWorld world)
    {
        GridPoint[] camps = world.Tribes.Select(t => t.Camp).ToArray();
        double least = double.MaxValue;
        for (int a = 0; a < camps.Length; a++)
        {
            for (int b = a + 1; b < camps.Length; b++)
            {
                least = Math.Min(least, Math.Sqrt(CampLand.Squared(camps[a], camps[b])));
            }
        }
        return least;
    }

    [Fact]
    public void Defends_OnlyLandWithinReach_ClearlyNearerItsOwnCamp()
    {
        GameWorld world = Match(0, 4);
        int reach = CampLand.Reach(world);
        foreach (TribeState computer in world.Tribes.Where(t => t.Controller.RunsAi))
        {
            foreach (GridPoint cell in Around(computer.Camp, reach + 2))
            {
                if (!CampLand.Defends(world, computer.Index, cell))
                {
                    continue;
                }
                double own = Math.Sqrt(CampLand.Squared(computer.Camp, cell));
                Assert.True(own <= reach);
                Assert.All(world.Tribes.Where(t => t.Index != computer.Index),
                    t => Assert.True(Math.Sqrt(CampLand.Squared(t.Camp, cell)) >= own + CampLand.SideMargin));
            }
        }
        GameWorld campaign = GameSimulation.StartMission(MatchWorlds.Content, 7, 0).World;
        Assert.True(CampLand.Defends(campaign, 1, new GridPoint(3, 3)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryCamp_HasRoomToBuild_AndNoPeoplesLandIsDefended_WithFourTribesAndAtCapacity(int map)
    {
        int capacity = MatchStart.CampCapacity(MatchWorlds.Content, map);
        Assert.True(capacity >= 4, $"map {map} has room for {capacity} camps");
        foreach (int tribes in new[] { 4, capacity })
        {
            GameWorld world = Match(map, tribes);
            int[] room = CampRoom.OfTribes(world);
            int defended = PeoplesLandDefended(world);
            output.WriteLine($"map {map}, {tribes} tribes: camps at least {LeastDistance(world):0} apart, room at least {room.Min()} footprints, people's land defended {defended}");
            Assert.All(room, r => Assert.True(r >= CampRoom.Needed, $"map {map}, {tribes} tribes: a camp has room for {r}"));
            Assert.Equal(0, defended);
        }
        Assert.NotNull(GameSimulation.MatchProblem(MatchWorlds.Content, MatchWorlds.Setup(map, 1, MatchWorlds.Controllers(4, capacity - 3))));
    }

    [Fact]
    public void Endless_CampsHaveRoom_AndNoPeoplesLandIsDefended()
    {
        GameWorld world = EndlessWorlds.Start(humans: 2, computers: 8, seed: 9).World;

        int[] room = CampRoom.OfTribes(world, CampRoom.Needed);
        int defended = PeoplesLandDefended(world);

        output.WriteLine($"endless 2+8: camps at least {LeastDistance(world):0} apart, room at least {room.Min()}, people's land defended {defended}");
        Assert.All(room, r => Assert.True(r >= CampRoom.Needed));
        Assert.Equal(0, defended);
    }

    [Fact]
    public void AJoiningTribe_GetsRoom_AndLeavesEveryCampItsRoom_OrNoCamp()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(4, 2, MatchWorlds.Controllers(2, 1)));
        int added = 0;
        while (game.AddTribe(TribeController.Computer()) >= 0)
        {
            added++;
            Assert.All(CampRoom.OfTribes(game.World, CampRoom.Needed), r => Assert.True(r >= CampRoom.Needed));
            Assert.True(added < 20, "the map never runs out of room");
        }
        output.WriteLine($"map 4: {added} tribes joined three");
        Assert.Equal(-1, game.AddTribe(TribeController.Human(3)));
    }

    [Fact]
    public void Intruders_AreOnlyEverFoundOnTheComputersSide()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 4, MatchWorlds.Controllers(2, 2)));
        GameWorld world = game.World;
        var before = world.Tribes.Select(t => t.Ai.Intruder).ToArray();
        int found = 0;
        for (int tick = 0; tick < 4000; tick++)
        {
            game.Step(BuildingPlayer.Orders(game, game.Tick));
            foreach (TribeState computer in world.Tribes.Where(t => t.Controller.RunsAi))
            {
                int intruder = computer.Ai.Intruder;
                if (intruder > 0 && intruder != before[computer.Index])
                {
                    Unit unit = world.Unit(intruder);
                    found++;
                    Assert.True(CampLand.Defends(world, computer.Index, new GridPoint(unit.X, unit.Y)) || UnitMovedOff(world, unit, computer.Index),
                        $"tribe {computer.Index} took unit {intruder} at ({unit.X},{unit.Y}) for an intruder");
                }
                before[computer.Index] = intruder;
            }
        }
        output.WriteLine($"{found} intruders found in 4000 frames");
    }

    /// <summary>Whether the unit could have stepped off the defended land in the frame since it was found (one cell).</summary>
    private static bool UnitMovedOff(GameWorld world, Unit unit, int computer) =>
        Around(new GridPoint(unit.X, unit.Y), 1).Any(cell => CampLand.Defends(world, computer, cell));
}
