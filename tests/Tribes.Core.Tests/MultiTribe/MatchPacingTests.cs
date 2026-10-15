using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.Tests.Endless;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>Matches: the opening grace, the land a computer defends, wild animals and the start stock of the settings, and camps spread apart.</summary>
public sealed class MatchPacingTests
{
    private const int Grace = 3000;

    private readonly ITestOutputHelper output;

    public MatchPacingTests(ITestOutputHelper output) => this.output = output;

    private static GameSimulation Match(int map, long seed, WorldSettings settings, int humans = 2, int computers = 2) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, seed, MatchWorlds.Controllers(humans, computers)) with { Settings = settings });

    /// <summary>The frame a human-ruled tribe is first hit by a computer tribe within <paramref name="frames"/>, or -1.</summary>
    private static int FirstComputerBlowOnPeople(GameSimulation game, int frames)
    {
        GameWorld world = game.World;
        for (int i = 0; i < frames; i++)
        {
            TickResult result = game.Step(BuildingPlayer.Orders(game, game.Tick));
            foreach (BlowLanded blow in result.Events.OfType<BlowLanded>())
            {
                if (blow.StrikerTribe < 0 || blow.Category is not (CellCategories.Unit or CellCategories.Building)
                    || !world.Tribes[blow.StrikerTribe].Controller.RunsAi)
                {
                    continue;
                }
                int victim = blow.Category == CellCategories.Unit ? world.TribeOfUnit(blow.Slot) : world.TribeOfBuilding(blow.Slot);
                if (world.Tribes[victim].Controller.IsHumanRuled)
                {
                    return world.Frame;
                }
            }
        }
        return -1;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(4, 1)]
    public void OpeningGrace_NoComputerAttacksAPeoplesTribe(int map, long seed)
    {
        GameSimulation game = Match(map, seed, new WorldSettings { OpeningGraceFrames = Grace });

        Assert.Equal(-1, FirstComputerBlowOnPeople(game, Grace - 1));
        Assert.All(game.World.Tribes.Where(t => t.Controller.RunsAi), t => Assert.True(t.Ai.Intruder < 0 && t.Ai.WaveTarget < 0));
    }

    [Fact]
    public void OpeningGrace_StopsWavesAndDefenceUntilItEnds()
    {
        GameWorld world = Match(0, 1, new WorldSettings { OpeningGraceFrames = Grace }).World;

        Assert.False(EndlessPacing.MaySendWaves(world, 2));
        Assert.False(EndlessPacing.MayDefend(world, 2));
        Assert.False(EndlessPacing.MayTarget(world, 2, 0, 100));
        Assert.Equal(0, EndlessPacing.ComputerFrames(world, 2));
        world.Frame = Grace + 10;
        Assert.True(EndlessPacing.MaySendWaves(world, 2));
        Assert.True(EndlessPacing.MayTarget(world, 2, 0, 100));
        Assert.Equal(10, EndlessPacing.ComputerFrames(world, 2));
    }

    [Fact]
    public void DefendedLand_IsTheLandAroundTheComputersOwnCamp()
    {
        GameWorld world = Match(0, 1, WorldSettings.Original).World;
        int reach = CampPlacer.RoomyDistance / 2;
        GridPoint camp = world.Tribes[2].Camp;

        CellRect land = EndlessPacing.DefendedLand(world, 2, new CellRect(-1000, -1000, 1000, 1000));

        Assert.Equal(new CellRect(camp.X - reach, camp.Y - reach, camp.X + reach, camp.Y + reach), land);
        GameWorld campaign = GameSimulation.StartMission(MatchWorlds.Content, 7, 0).World;
        Assert.Equal(new CellRect(1, 2, 3, 4), EndlessPacing.DefendedLand(campaign, 1, new CellRect(1, 2, 3, 4)));
    }

    [Fact]
    public void WildAnimals_AreNeverBornOnACampsLand_AndNoneWhenSwitchedOff()
    {
        GameSimulation with = Match(1, 3, WorldSettings.Original);
        GameSimulation without = Match(1, 3, new WorldSettings { WildAnimals = false });
        var born = new List<GridPoint>();

        var living = new HashSet<int>();
        for (int tick = 0; tick < 3000; tick++)
        {
            with.Step();
            without.Step();
            for (int slot = 0; slot < with.World.AnimalSlots; slot++)
            {
                Animal animal = with.World.Animal(slot);
                if (animal.InUse && living.Add(slot))
                {
                    born.Add(new GridPoint(animal.X, animal.Y));
                }
                else if (!animal.InUse)
                {
                    living.Remove(slot);
                }
            }
        }

        Assert.NotEmpty(born);
        Assert.DoesNotContain(born, cell => CampLand.IsNearACamp(with.World, cell));
        Assert.DoesNotContain(Animals(without.World), a => a.InUse);
    }

    private static IEnumerable<Animal> Animals(GameWorld world) => Enumerable.Range(0, world.AnimalSlots).Select(world.Animal);

    [Fact]
    public void StartStock_FollowsTheSettings()
    {
        GameWorld normal = Match(1, 4, WorldSettings.Original).World;
        GameWorld rich = Match(1, 4, new WorldSettings { StartStockPercent = 300 }).World;

        foreach (TribeCounter counter in new[] { TribeCounter.Wood, TribeCounter.Stones, TribeCounter.Food })
        {
            Assert.All(Enumerable.Range(0, 4), t => Assert.Equal(normal.Tribes[t][counter] * 3, rich.Tribes[t][counter]));
        }
        Assert.Contains(Enumerable.Range(0, 4), t => normal.Tribes[t][TribeCounter.Wood] > 0);
    }

    [Fact]
    public void Settings_AreSavedWithTheWorld_AndRefusedOutOfRange()
    {
        var settings = new WorldSettings { OpeningGraceFrames = 1234, StartStockPercent = 200, WildAnimals = false };
        GameSimulation game = Match(2, 5, settings);

        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());

        Assert.Equal(settings, loaded.World.Settings);
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.NotNull(GameSimulation.MatchProblem(MatchWorlds.Content, MatchWorlds.Setup(0, 1, MatchWorlds.Controllers(1, 1)) with
        {
            Settings = new WorldSettings { StartStockPercent = -1 },
        }));
        Assert.Equal(WorldSettings.Original, GameSimulation.StartMission(MatchWorlds.Content, 7, 0).World.Settings);
    }

    [Fact]
    public void Camps_KeepTwiceTheLeastSpacingWhereTheMapHasRoom_AndFourPeopleFitEveryMap()
    {
        for (int map = 0; map < MatchStart.MapCount(MatchWorlds.Content); map++)
        {
            int capacity = MatchStart.CampCapacity(MatchWorlds.Content, map);
            int roomy = MatchStart.RoomyCampCapacity(MatchWorlds.Content, map);
            var line = new System.Text.StringBuilder($"map {map}: capacity {capacity}, room at {CampPlacer.RoomyDistance} cells for {roomy};");
            foreach (int count in new[] { 2, 4, roomy, capacity }.Distinct().Where(c => c >= 2))
            {
                GameSimulation game = Match(map, 7, WorldSettings.Original, humans: Math.Min(4, count), computers: Math.Max(0, count - 4));
                double least = LeastDistance(game.World);
                line.Append($" {count} tribes {least:0}");
                if (count <= roomy)
                {
                    Assert.True(least >= CampPlacer.RoomyDistance, $"map {map}: {count} camps only {least:0} apart");
                }
            }
            output.WriteLine(line.ToString());
            Assert.True(LeastDistance(Match(map, 7, WorldSettings.Original, humans: 4, computers: 0).World) >= CampPlacer.RoomyDistance,
                $"map {map}: four camps closer than {CampPlacer.RoomyDistance}");
        }
    }

    private static double LeastDistance(GameWorld world)
    {
        GridPoint[] camps = world.Tribes.Select(t => t.Camp).ToArray();
        double least = double.MaxValue;
        for (int a = 0; a < camps.Length; a++)
        {
            for (int b = a + 1; b < camps.Length; b++)
            {
                least = Math.Min(least, Math.Sqrt(Math.Pow(camps[a].X - camps[b].X, 2) + Math.Pow(camps[a].Y - camps[b].Y, 2)));
            }
        }
        return least;
    }
}
