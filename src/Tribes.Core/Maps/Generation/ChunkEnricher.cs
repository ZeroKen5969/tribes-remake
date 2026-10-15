using System;
using System.Collections.Generic;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Tops a chunk design up with resource nodes to the original designs' share of each kind: pieces
/// with many nodes fit their neighbours less often, so quilted land holds fewer than the maps it
/// copies. A node goes only on plain ground, unlocked, whose four neighbours may stand beside it by
/// the original maps' rules, and only where the chunk's connectors stay joined.
/// </summary>
internal sealed class ChunkEnricher
{
    /// <summary>Random cells tried per node still wanted.</summary>
    private const int TriesPerNode = 24;

    private static readonly (int Dx, int Dy, int Direction)[] Neighbours =
    {
        (1, 0, TerrainModel.Right), (0, 1, TerrainModel.Down), (-1, 0, TerrainModel.Left), (0, -1, TerrainModel.Up),
    };

    private readonly TerrainModel model;
    private readonly ChunkConnectivity connectivity;

    /// <summary>Each resource node tile of the designs and how many a chunk holds on average.</summary>
    private readonly List<(int Tile, int PerChunk)> shares = new();

    /// <summary>Tops up designs by <paramref name="model"/>, keeping them joined as <paramref name="connectivity"/> checks.</summary>
    public ChunkEnricher(TerrainModel model, ChunkConnectivity connectivity)
    {
        this.model = model;
        this.connectivity = connectivity;
        var counts = new long[TileSet.Capacity];
        long cells = 0;
        foreach (SourceMap source in model.Sources)
        {
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    counts[source.TileAt(x, y)]++;
                }
            }
            cells += (long)source.Width * source.Height;
        }
        for (int tile = 0; tile < TileSet.Capacity && cells > 0; tile++)
        {
            int perChunk = (int)Math.Round(counts[tile] * (double)(ChunkCanvas.Size * ChunkCanvas.Size) / cells);
            if (model.Resource[tile] && perChunk > 0)
            {
                shares.Add((tile, perChunk));
            }
        }
    }

    /// <summary>Adds resource nodes to <paramref name="canvas"/> until each kind reaches its share or no fitting cell is found.</summary>
    public void Enrich(ChunkCanvas canvas, GameRandom random)
    {
        foreach ((int tile, int perChunk) in shares)
        {
            int wanted = perChunk - Count(canvas, tile);
            for (int tries = 0; wanted > 0 && tries < wanted * TriesPerNode; tries++)
            {
                int x = random.NextMasked(int.MaxValue, ChunkCanvas.Size), y = random.NextMasked(int.MaxValue, ChunkCanvas.Size);
                if (!MayHold(canvas, x, y, tile))
                {
                    continue;
                }
                int ground = canvas[x, y];
                canvas[x, y] = tile;
                if (!connectivity.Connected(canvas))
                {
                    canvas[x, y] = ground;
                    continue;
                }
                wanted--;
            }
        }
    }

    private static int Count(ChunkCanvas canvas, int tile)
    {
        int count = 0;
        for (int y = 0; y < ChunkCanvas.Size; y++)
        {
            for (int x = 0; x < ChunkCanvas.Size; x++)
            {
                count += canvas[x, y] == tile ? 1 : 0;
            }
        }
        return count;
    }

    /// <summary>Whether the chunk cell is plain ground, free to change, and <paramref name="tile"/> fits beside each of its neighbours.</summary>
    private bool MayHold(ChunkCanvas canvas, int x, int y, int tile)
    {
        if (canvas[x, y] != model.Ground || canvas.IsLocked(x, y))
        {
            return false;
        }
        foreach ((int dx, int dy, int direction) in Neighbours)
        {
            int next = canvas[x + dx, y + dy];
            if (next >= 0 && (!model.Fits(tile, direction, next) || !model.Fits(next, TerrainModel.Opposite(direction), tile)))
            {
                return false;
            }
        }
        return true;
    }
}
