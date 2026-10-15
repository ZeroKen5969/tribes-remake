using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// New ground for cells of a camp site that lose their thicket, rock or node (or, as a last resort,
/// every cell of a site): each gets the open ground tile that fits its neighbours best by the rules
/// learnt from the original maps, so a cleared camp shows no hard-edged squares of grass.
/// </summary>
internal static class SiteTiles
{
    /// <summary>Passes over the cells: the first sees the cells not done yet as anything, the second sees them all.</summary>
    private const int Passes = 2;

    /// <summary>Gives each of <paramref name="cells"/> (in order) the open ground tile that fits its neighbours best.</summary>
    public static void Replant(WorldMap map, GameContent content, long seed, IReadOnlyList<GridPoint> cells)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cells);
        TerrainModel model = TerrainModel.For(content);
        PairCounts counts = PairCounts.For(model);
        List<int> ground = OpenGround(model);
        var waiting = new HashSet<GridPoint>(cells);
        for (int pass = 0; pass < Passes; pass++)
        {
            foreach (GridPoint cell in cells)
            {
                waiting.Remove(cell);
                int tile = Best(map, content.Terrain, model, counts, ground, cell, waiting);
                map.SetTile(cell, WithVariant(content.Terrain, tile, seed, cell));
            }
        }
    }

    /// <summary>The walkable tiles of the original maps that are no thicket, rock or node.</summary>
    private static List<int> OpenGround(TerrainModel model)
    {
        var tiles = new List<int>();
        for (int tile = 0; tile < TileSet.Capacity; tile++)
        {
            if (model.SeenWalkable.Contains(tile) && !model.Clearable[tile])
            {
                tiles.Add(tile);
            }
        }
        return tiles;
    }

    /// <summary>
    /// The tile of <paramref name="ground"/> for <paramref name="cell"/> that fits most of its
    /// neighbours (cells <paramref name="waiting"/> for new ground and land not made count as any),
    /// then the one seen most often beside them in the original maps, then the most common one.
    /// </summary>
    private static int Best(WorldMap map, TerrainCatalog terrain, TerrainModel model, PairCounts counts, List<int> ground,
        GridPoint cell, HashSet<GridPoint> waiting)
    {
        Span<int> around = stackalloc int[TerrainModel.Directions];
        for (int d = 0; d < TerrainModel.Directions; d++)
        {
            var next = new GridPoint(cell.X + TerrainModel.StepX(d), cell.Y + TerrainModel.StepY(d));
            around[d] = waiting.Contains(next) || !map.InPlay(next) ? -1 : ChunkBuilder.DesignTile(terrain, map.TileAt(next));
        }
        int best = model.Ground;
        (int Fits, long Seen, int Weight) bestScore = (-1, -1, -1);
        foreach (int tile in ground)
        {
            int fits = 0;
            long seen = 0;
            for (int d = 0; d < TerrainModel.Directions; d++)
            {
                if (around[d] >= 0)
                {
                    fits += model.Fits(tile, d, around[d]) ? 1 : 0;
                    seen += counts.Count(tile, d, around[d]);
                }
            }
            (int, long, int) score = (fits, seen, model.Weights[tile]);
            if (score.CompareTo(bestScore) > 0)
            {
                (best, bestScore) = (tile, score);
            }
        }
        return best;
    }

    /// <summary>A tile drawn in variants gets one picked by the cell (the same on every peer).</summary>
    private static int WithVariant(TerrainCatalog terrain, int tile, long seed, GridPoint cell) =>
        terrain.VariantTiles.Contains(tile)
            ? tile + (int)((ulong)TerrainGenerator.ChunkSeed(seed, cell.X, cell.Y) % (ulong)terrain.VariantCount)
            : tile;
}
