using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Tests.MultiTribe;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

public sealed class TerrainGeneratorTests
{
    private readonly ITestOutputHelper output;

    public TerrainGeneratorTests(ITestOutputHelper output) => this.output = output;

    private static GameContent Content => MatchWorlds.Content;

    /// <summary>Chunks in rings around (0, 0) up to <paramref name="radius"/>, ring by ring.</summary>
    internal static List<ChunkCoord> Rings(int radius)
    {
        var chunks = new List<ChunkCoord>();
        for (int ring = 0; ring <= radius; ring++)
        {
            for (int y = -ring; y <= ring; y++)
            {
                for (int x = -ring; x <= ring; x++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == ring)
                    {
                        chunks.Add(new ChunkCoord(x, y));
                    }
                }
            }
        }
        return chunks;
    }

    private static WorldMap Generate(IEnumerable<ChunkCoord> chunks, long seed)
    {
        var generator = new TerrainGenerator(Content);
        WorldMap map = WorldMap.Chunked();
        foreach (ChunkCoord chunk in chunks)
        {
            generator.Generate(map, chunk, seed);
        }
        return map;
    }

    [Fact]
    public void Model_LearnsTheOriginalMaps()
    {
        TerrainModel model = TerrainModel.For(Content);

        Assert.Equal(10, model.Sources.Count);
        Assert.True(model.Seen.Count > 200);
        Assert.True(model.Walkable[model.Ground]);
        Assert.True(model.Fits(model.Ground, TerrainModel.Right, model.Ground));
        Assert.Same(model, TerrainModel.For(Content));
    }

    [Fact]
    public void ChunkSeed_DependsOnSeedAndPlace()
    {
        long a = TerrainGenerator.ChunkSeed(5, 1, 2);

        Assert.Equal(a, TerrainGenerator.ChunkSeed(5, 1, 2));
        Assert.NotEqual(a, TerrainGenerator.ChunkSeed(5, 2, 1));
        Assert.NotEqual(a, TerrainGenerator.ChunkSeed(6, 1, 2));
        Assert.NotEqual(TerrainGenerator.ChunkSeed(5, -1, 0), TerrainGenerator.ChunkSeed(5, 0, -1));
    }

    [Fact]
    public void Generate_IsTheSameForTheSameSeedAndOrder()
    {
        List<ChunkCoord> chunks = Rings(1);

        WorldMap a = Generate(chunks, 42);
        WorldMap b = Generate(chunks, 42);
        WorldMap other = Generate(chunks, 43);

        Assert.True(SameLand(a, b));
        Assert.False(SameLand(a, other));
    }

    [Fact]
    public void Generate_SeamsFitTheOriginalNeighbourRules()
    {
        WorldMap map = Generate(Rings(2), 7);
        TerrainModel model = TerrainModel.For(Content);
        int pairs = 0, misfits = 0;

        foreach (MapChunk chunk in map.Chunks)
        {
            foreach ((GridPoint a, GridPoint b, int direction) in Pairs(chunk.Cells))
            {
                if (!map.InPlay(b))
                {
                    continue;
                }
                pairs++;
                int ta = ChunkBuilder.DesignTile(Content.Terrain, map.TileAt(a));
                int tb = ChunkBuilder.DesignTile(Content.Terrain, map.TileAt(b));
                misfits += model.Fits(ta, direction, tb) ? 0 : 1;
            }
        }

        output.WriteLine($"pairs {pairs}, misfits {misfits}");
        Assert.Equal(0, misfits);
    }

    [Fact]
    public void Generate_AllConnectorsOfAllChunksAreJoined()
    {
        WorldMap map = Generate(Rings(2), 11);
        var connectors = new List<GridPoint>();
        foreach (MapChunk chunk in map.Chunks)
        {
            foreach ((int x, int y) in ChunkConnectivity.ConnectorCells())
            {
                connectors.Add(new GridPoint(chunk.Cells.X + x, chunk.Cells.Y + y));
            }
        }

        HashSet<GridPoint> reached = Flood(map, connectors[0]);

        Assert.All(connectors, c => Assert.Contains(c, reached));
    }

    [Fact]
    public void Generate_JoinsChunksMadeInAnyOrder()
    {
        List<ChunkCoord> chunks = Rings(1);
        chunks.Reverse();

        WorldMap map = Generate(chunks, 3);
        var start = new GridPoint(ChunkConnectivity.ConnectorStart, 0);

        HashSet<GridPoint> reached = Flood(map, start);
        foreach (MapChunk chunk in map.Chunks)
        {
            Assert.Contains(new GridPoint(chunk.Cells.X, chunk.Cells.Y + ChunkConnectivity.ConnectorStart), reached);
        }
    }

    [Fact]
    public void Generate_FollowsTheSavedEdgeOfANeighbourMadeAnotherWay()
    {
        // The neighbour was made from another seed (as a chunk saved by an older generator would be).
        var generator = new TerrainGenerator(Content);
        WorldMap map = WorldMap.Chunked();
        generator.Generate(map, new ChunkCoord(0, 0), 1234);
        MapChunk first = map.Chunks[0];
        byte[] before = first.Tiles.ToArray();

        generator.Generate(map, new ChunkCoord(1, 0), 1);

        TerrainModel model = generator.Model;
        for (int y = 0; y < WorldMap.ChunkSize; y++)
        {
            int left = ChunkBuilder.DesignTile(Content.Terrain, map.TileAt(new GridPoint(63, y)));
            int right = ChunkBuilder.DesignTile(Content.Terrain, map.TileAt(new GridPoint(64, y)));
            Assert.True(model.Fits(left, TerrainModel.Right, right), $"row {y}: {left} | {right}");
        }
        Assert.Equal(before, first.Tiles);
        Assert.All(map.Chunks, c => Assert.Equal(TerrainGenerator.Version, c.GeneratorVersion));
    }

    [Fact]
    [Trait("Speed", "Long")]
    public void Generate_Speed()
    {
        List<ChunkCoord> chunks = Rings(3);
        var watch = Stopwatch.StartNew();

        WorldMap map = Generate(chunks, 99);

        double perChunk = watch.Elapsed.TotalMilliseconds / chunks.Count;
        output.WriteLine($"{chunks.Count} chunks, {perChunk:0.0} ms per chunk, {map.ResourceNodes.Count} nodes");
        Dump(map);
        Assert.True(perChunk < 250, $"{perChunk} ms per chunk");
    }

    private static void Dump(WorldMap map)
    {
        string? path = Environment.GetEnvironmentVariable("TRIBES_DUMP_LAND");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        GridRect bounds = map.Bounds;
        var rows = new List<string>();
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            var row = new StringBuilder();
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                row.Append(map.TileAt(new GridPoint(x, y)).ToString("x2", CultureInfo.InvariantCulture));
            }
            rows.Add(row.ToString());
        }
        File.WriteAllText(path, "{\"id\":99,\"width\":" + bounds.Width + ",\"height\":" + bounds.Height + ",\"rows\":["
            + string.Join(",", rows.Select(r => "\"" + r + "\"")) + "]}");
    }

    private static IEnumerable<(GridPoint A, GridPoint B, int Direction)> Pairs(GridRect cells)
    {
        for (int y = cells.Top; y < cells.Bottom; y++)
        {
            for (int x = cells.Left; x < cells.Right; x++)
            {
                yield return (new GridPoint(x, y), new GridPoint(x + 1, y), TerrainModel.Right);
                yield return (new GridPoint(x, y), new GridPoint(x, y + 1), TerrainModel.Down);
            }
        }
    }

    private static HashSet<GridPoint> Flood(WorldMap map, GridPoint start)
    {
        var reached = new HashSet<GridPoint> { start };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            GridPoint cell = queue.Dequeue();
            foreach (GridPoint next in new[]
            {
                new GridPoint(cell.X + 1, cell.Y), new GridPoint(cell.X - 1, cell.Y),
                new GridPoint(cell.X, cell.Y + 1), new GridPoint(cell.X, cell.Y - 1),
            })
            {
                if (map.InPlay(next) && map.CellAt(next).IsEmpty && reached.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return reached;
    }

    private static bool SameLand(WorldMap a, WorldMap b)
    {
        if (a.Bounds != b.Bounds)
        {
            return false;
        }
        for (int y = a.Bounds.Top; y < a.Bounds.Bottom; y++)
        {
            for (int x = a.Bounds.Left; x < a.Bounds.Right; x++)
            {
                var cell = new GridPoint(x, y);
                if (a.TileAt(cell) != b.TileAt(cell) || a.CellAt(cell) != b.CellAt(cell))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
