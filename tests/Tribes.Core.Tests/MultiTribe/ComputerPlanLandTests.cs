using System.Linq;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Tests.Endless;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// A computer tribe's plan in a world whose land grows (endless): it follows from the saved world
/// alone, so a loaded copy plays on exactly as the original, and it is made again only when land
/// within its reach of the camp is made.
/// </summary>
public sealed class ComputerPlanLandTests
{
    private const int Tribes = 30, AddedPerStep = 25, StepTicks = 20, TicksAfterLoad = 60;

    /// <summary>Chunks this far from the camp's lie beyond any plan's reach.</summary>
    private const int FarChunks = 12;

    /// <summary>The rounds of the search for room round a planned place (ComputerPlans).</summary>
    private const int SearchRounds = 25;

    private static TribeController Newcomer(int n) => n % 10 == 3 ? TribeController.Absent(4 + n) : TribeController.Computer(n % 2);

    [Fact]
    public void ManyComputerTribes_InAGrowingWorld_PlayOnAlikeAfterSaveAndLoad()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 77);
        for (int n = 0; n < Tribes; n++)
        {
            game.AddTribe(Newcomer(n));
            if (n % AddedPerStep == AddedPerStep - 1)
            {
                for (int tick = 0; tick < StepTicks; tick++)
                {
                    game.Step();
                }
            }
        }
        GameSimulation loaded = GameSimulation.Load(EndlessWorlds.Content, game.Save());

        for (int tick = 1; tick <= TicksAfterLoad; tick++)
        {
            game.Step();
            loaded.Step();
            Assert.True(game.StateHash() == loaded.StateHash(), $"apart {tick} ticks after loading");
        }
    }

    [Fact]
    public void Plan_IsKeptWhenFarLandIsMade_AndMadeAgainWhenLandWithinItsReachIs()
    {
        // A world where land within the plan's reach of the camp is not made yet.
        (GameSimulation game, int tribe, ChunkCoord near) = Enumerable.Range(1, 40)
            .Select(seed => EndlessWorlds.Start(humans: 1, computers: 1, seed: seed))
            .Select(g => (g, Computer(g.World)))
            .Select(p => (p.g, p.Item2, UnmadeChunkWithinReach(p.g, p.Item2)))
            .First(p => p.Item3 is not null) is var found ? (found.g, found.Item2, found.Item3!.Value) : default;
        GameWorld world = game.World;
        ChunkCoord camp = ChunkCoord.Of(world.Tribes[tribe].Camp);
        MissionAi plan = game.Rules.ComputerPlans.For(tribe);

        game.Rules.Land.Make(new ChunkCoord(camp.X + FarChunks, camp.Y + FarChunks));
        Assert.Same(plan, game.Rules.ComputerPlans.For(tribe));

        game.Rules.Land.Make(near);
        Assert.NotSame(plan, game.Rules.ComputerPlans.For(tribe));
    }

    [Fact]
    public void Reach_HoldsTheCornersOfTheAlarmAndTerritoryAreas_WithRoomToClampThem()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 1, MatchWorlds.Controllers(1, 1)));
        foreach (MissionRules mission in MatchWorlds.Content.Rules.Missions.Missions)
        {
            MissionAi template = mission.Ai;
            var camp = new Content.GridPoint(400, -300);
            Content.GridRect reach = game.Rules.ComputerPlans.Reach(template, camp);
            (int dx, int dy) = (camp.X - template.Home.X, camp.Y - template.Home.Y);
            foreach (CellRect rect in new[] { template.AlarmRect, template.TerritoryRect })
            {
                foreach (var corner in new[] { new Content.GridPoint(rect.Left + dx, rect.Top + dy), new Content.GridPoint(rect.Right + dx, rect.Bottom + dy) })
                {
                    Assert.True(Content.GridRect.FromEdges(reach.Left + 5, reach.Top + 5, reach.Right - 5, reach.Bottom - 5).Contains(corner),
                        $"mission {mission.Id}: corner {corner} outside {reach}");
                }
            }
        }
    }

    private static int Computer(GameWorld world) => world.Tribes.Select((t, i) => (t, i)).First(p => p.t.Controller.RunsAi).i;

    /// <summary>
    /// A chunk not made yet within the search for room round the mission's plan moved to the tribe's
    /// camp (the places' box widened by the search's rounds), or null.
    /// </summary>
    private static ChunkCoord? UnmadeChunkWithinReach(GameSimulation game, int tribe)
    {
        GameWorld world = game.World;
        MissionAi template = game.Rules.ComputerPlans.Template;
        Content.GridPoint camp = world.Tribes[tribe].Camp;
        (int dx, int dy) = (camp.X - template.Home.X, camp.Y - template.Home.Y);
        var places = template.BuildPlan.Select(e => e.Cell).Concat(template.Posts.Select(p => p.Cell))
            .Select(c => new Content.GridPoint(c.X + dx, c.Y + dy)).Append(camp).ToList();
        ChunkCoord first = ChunkCoord.Of(new Content.GridPoint(places.Min(c => c.X) - SearchRounds, places.Min(c => c.Y) - SearchRounds));
        ChunkCoord last = ChunkCoord.Of(new Content.GridPoint(places.Max(c => c.X) + SearchRounds, places.Max(c => c.Y) + SearchRounds));
        return Enumerable.Range(first.Y, last.Y - first.Y + 1)
            .SelectMany(y => Enumerable.Range(first.X, last.X - first.X + 1).Select(x => new ChunkCoord(x, y)))
            .Where(c => world.Map.Chunk(c) == null)
            .Select(c => (ChunkCoord?)c)
            .FirstOrDefault();
    }
}
