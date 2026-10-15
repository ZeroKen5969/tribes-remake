using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

public sealed class LandWorksTests
{
    private const int LegTicks = 300, LegCells = 40;

    /// <summary>A world with one explorer of the people's tribe (land ahead of it is asked for as it walks).</summary>
    private static (GameSimulation Game, int Explorer) Exploring(long seed)
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 1, seed: seed);
        GameWorld world = game.World;
        game.Step();
        int explorer = world.UnitsOf(0).Select(u => u.Id).First(id => world.Unit(id).InUse);
        return (game, explorer);
    }

    /// <summary>One tick; every few ticks the explorer is sent a leg further east, as a player would.</summary>
    private static TickResult Walk(GameSimulation game, int explorer)
    {
        GameWorld world = game.World;
        if (game.Tick % LegTicks != 0)
        {
            return game.Step();
        }
        var goal = new GridPoint(world.Tribes[0].Camp.X + ((int)(game.Tick / LegTicks + 1) * LegCells), world.Tribes[0].Camp.Y);
        return game.Step(TickInput.WithOrders(new MoveUnits(0, new UnitSet(new[] { explorer }), goal, 0, true)));
    }

    [Fact]
    public void MissingLand_JoinsTheWorldLeadFramesLater_OneChunkAFrame()
    {
        (GameSimulation game, int explorer) = Exploring(seed: 3);
        GameWorld world = game.World;
        int lead = world.Content.Endless!.Land.LeadFrames;
        var askedAt = new Dictionary<ChunkCoord, int>();
        int joined = 0;

        for (int tick = 0; tick < 3000; tick++)
        {
            TickResult result = Walk(game, explorer);
            foreach (LandOrder order in world.Endless!.LandOrders)
            {
                askedAt.TryAdd(order.Coord, world.Frame);
                Assert.Null(world.Map.Chunk(order.Coord));
            }
            List<ChunkCoord> made = result.Events.OfType<ChunksGenerated>().SelectMany(e => e.Chunks).ToList();
            Assert.True(made.Count <= 1, $"{made.Count} chunks joined on frame {world.Frame}");
            foreach (ChunkCoord chunk in made)
            {
                Assert.True(world.Frame - askedAt[chunk] >= lead - 1, $"chunk {chunk} joined {world.Frame - askedAt[chunk]} frames after it was asked for");
                joined++;
            }
        }

        Assert.True(joined >= 2, $"{joined} chunks joined");
    }

    [Fact]
    public void LandBeingMade_IsSavedAndComesOutTheSameAfterALoad()
    {
        (GameSimulation game, int explorer) = Exploring(seed: 5);
        (GameSimulation twin, _) = Exploring(seed: 5);
        GameWorld world = game.World;
        for (int tick = 0; tick < 4000 && world.Endless!.LandOrders.Count < 2; tick++)
        {
            Walk(game, explorer);
            Walk(twin, explorer);
        }
        Assert.True(world.Endless!.LandOrders.Count >= 2);

        GameSimulation loaded = GameSimulation.Load(EndlessWorlds.Content, game.Save());

        Assert.Equal(game.StateHash(), loaded.StateHash());
        for (int tick = 0; tick < 400; tick++)
        {
            Walk(game, explorer);
            Walk(twin, explorer);
            Walk(loaded, explorer);
            Assert.Equal(game.StateHash(), loaded.StateHash());
        }
        Assert.Equal(game.StateHash(), twin.StateHash());
        Assert.Equal(game.Save(), loaded.Save());
    }

    [Fact]
    public void GenerateLand_LetsTheLandBeingMadeJoinFirst()
    {
        (GameSimulation game, int explorer) = Exploring(seed: 7);
        GameWorld world = game.World;
        for (int tick = 0; tick < 4000 && world.Endless!.LandOrders.Count == 0; tick++)
        {
            Walk(game, explorer);
        }
        ChunkCoord ordered = world.Endless!.LandOrders[0].Coord;

        game.GenerateLand(new GridRect(ordered.Origin.X, ordered.Origin.Y, 1, 1));

        Assert.Empty(world.Endless.LandOrders);
        Assert.NotNull(world.Map.Chunk(ordered));
    }
}
