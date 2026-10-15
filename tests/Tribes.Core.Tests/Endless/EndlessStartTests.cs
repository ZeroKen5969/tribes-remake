using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

public sealed class EndlessStartTests
{
    [Fact]
    public void Start_GivesEveryTribeACampOnTheLandAndNeverDecides()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 2, computers: 6);
        GameWorld world = game.World;

        Assert.True(game.IsEndless);
        Assert.True(game.IsMatch);
        Assert.True(world.Map.IsChunked);
        Assert.Equal(8, world.Tribes.Count);
        Assert.False(world.Match.EndsWithLastSide);
        int minimum = EndlessWorlds.Content.Endless!.Camps.MinimumDistance;
        for (int a = 0; a < world.Tribes.Count; a++)
        {
            Assert.True(world.Map.InPlay(world.Tribes[a].Camp));
            Assert.True(world.Tribes[a][TribeCounter.Population] > 0);
            for (int b = a + 1; b < world.Tribes.Count; b++)
            {
                GridPoint p = world.Tribes[a].Camp, q = world.Tribes[b].Camp;
                long dx = p.X - q.X, dy = p.Y - q.Y;
                Assert.True((dx * dx) + (dy * dy) >= (long)minimum * minimum, $"camps {a} and {b} are too close");
            }
        }
        Assert.True(TribeRoles.IsPlayer(world, 0) && TribeRoles.IsPlayer(world, 1));
        Assert.True(TribeRoles.IsComputer(world, 7));
    }

    [Fact]
    public void Start_RaisesTribeAppearedAndChunksGeneratedWithTheFirstTick()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 2);

        TickResult first = game.Step();

        Assert.Equal(3, first.Events.OfType<TribeAppeared>().Count());
        Assert.Contains(first.Events, e => e is ChunksGenerated);
        Assert.Equal(new[] { false, true, true }, first.Events.OfType<TribeAppeared>().Select(e => e.Computer));
    }

    [Fact]
    public void Start_MakesLandAheadOfEveryUnit()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 3);
        GameWorld world = game.World;
        int reach = EndlessWorlds.Content.Endless!.Land.ReachCells;

        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            if (unit.InUse)
            {
                Assert.True(world.Map.InPlay(new GridPoint(unit.X + reach, unit.Y + reach)));
                Assert.True(world.Map.InPlay(new GridPoint(unit.X - reach, unit.Y - reach)));
            }
        }
    }

    [Fact]
    public void Start_IsTheSameForTheSameSetup()
    {
        GameSimulation a = EndlessWorlds.Start(humans: 1, computers: 4, seed: 9);
        GameSimulation b = EndlessWorlds.Start(humans: 1, computers: 4, seed: 9);
        GameSimulation other = EndlessWorlds.Start(humans: 1, computers: 4, seed: 10);

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.NotEqual(a.StateHash(), other.StateHash());
    }

    [Fact]
    public void Problem_RefusesSetupsThatCannotStart()
    {
        GameContent content = EndlessWorlds.Content;

        Assert.Null(EndlessStart.Problem(content, EndlessWorlds.Setup(1, 0)));
        Assert.NotNull(EndlessStart.Problem(content, EndlessWorlds.Setup(0, 3)));
        Assert.NotNull(EndlessStart.Problem(content, EndlessWorlds.Setup(1, -1)));
        Assert.NotNull(EndlessStart.Problem(content, EndlessWorlds.Setup(1, 0) with { NewTribes = 4 }));
        Assert.NotNull(EndlessStart.Problem(content, EndlessWorlds.Setup(1, 0) with { Difficulty = 2 }));
        Assert.Throws<ArgumentException>(() => GameSimulation.StartEndless(content, EndlessWorlds.Setup(0, 1)));
    }

    [Fact]
    public void Content_HasTheAllianceBuildingOnlyInEndlessWorlds()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0);

        Assert.True(EndlessContent.IsEndless(game.World.Content));
        Assert.False(EndlessContent.IsEndless(EndlessWorlds.Content));
        Assert.Equal(3, game.World.Content.Rules.Buildings.Kinds[AllianceBuilding.Kind].Width);
        Assert.Same(game.World.Content, EndlessContent.For(game.World.Content));
        Assert.Same(EndlessContent.For(EndlessWorlds.Content), EndlessContent.For(EndlessWorlds.Content));
    }

    [Fact]
    public void Start_ClearedCampSitesKeepTheTilesFittingTheirNeighbours()
    {
        GameSimulation game = GameSimulation.StartEndless(EndlessWorlds.Content, EndlessWorlds.Setup(humans: 1, computers: 8, seed: 7) with
        {
            StartAreaSize = 128,
        });
        WorldMap map = game.World.Map;
        var model = Maps.Generation.TerrainModel.For(game.World.Content);
        int misfits = 0;

        foreach (MapChunk chunk in map.Chunks)
        {
            for (int y = chunk.Cells.Top; y < chunk.Cells.Bottom; y++)
            {
                for (int x = chunk.Cells.Left; x < chunk.Cells.Right; x++)
                {
                    int tile = Design(game, new GridPoint(x, y));
                    var right = new GridPoint(x + 1, y);
                    var down = new GridPoint(x, y + 1);
                    misfits += map.InPlay(right) && !model.Fits(tile, Maps.Generation.TerrainModel.Right, Design(game, right)) ? 1 : 0;
                    misfits += map.InPlay(down) && !model.Fits(tile, Maps.Generation.TerrainModel.Down, Design(game, down)) ? 1 : 0;
                }
            }
        }

        Assert.Equal(0, misfits);
    }

    private static int Design(GameSimulation game, GridPoint cell) =>
        Maps.Generation.ChunkBuilder.DesignTile(game.World.Content.Terrain, game.World.Map.TileAt(cell));

    [Fact]
    public void Runs_SavesAndLoadsWithTheSameHash()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 3, seed: 21);
        for (int tick = 0; tick < 600; tick++)
        {
            game.Step();
        }

        GameSimulation loaded = GameSimulation.Load(EndlessWorlds.Content, game.Save());

        Assert.True(loaded.IsEndless);
        Assert.Equal(game.StateHash(), loaded.StateHash());
        for (int tick = 0; tick < 300; tick++)
        {
            game.Step();
            loaded.Step();
        }
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.Equal(game.Save(), loaded.Save());
    }

    [Fact]
    public void GenerateLand_MakesTheAreaAndOnlyInEndlessWorlds()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0);
        var far = new GridRect(2000, -3000, 10, 10);

        IReadOnlyList<ChunkCoord> made = game.GenerateLand(far);

        Assert.Single(made);
        Assert.True(game.World.Map.InPlay(new GridPoint(2005, -2995)));
        Assert.Empty(game.GenerateLand(far));
        GameSimulation campaign = GameSimulation.StartMission(EndlessWorlds.Content, 7, 0);
        Assert.Throws<InvalidOperationException>(() => campaign.GenerateLand(far));
    }
}
