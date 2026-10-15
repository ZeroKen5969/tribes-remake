using System.Runtime.CompilerServices;

namespace Tribes.Core.Maps.Generation;

/// <summary>How often the original map designs put each tile next to each tile, by direction.</summary>
internal sealed class PairCounts
{
    private static readonly ConditionalWeakTable<TerrainModel, PairCounts> Counts = new();

    private readonly int[][] counts = new int[TerrainModel.Directions][];

    private PairCounts(TerrainModel model)
    {
        for (int d = 0; d < TerrainModel.Directions; d++)
        {
            counts[d] = new int[TileSet.Capacity * TileSet.Capacity];
        }
        foreach (SourceMap source in model.Sources)
        {
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    int tile = source.TileAt(x, y);
                    if (x + 1 < source.Width)
                    {
                        Add(tile, TerrainModel.Right, source.TileAt(x + 1, y));
                    }
                    if (y + 1 < source.Height)
                    {
                        Add(tile, TerrainModel.Down, source.TileAt(x, y + 1));
                    }
                }
            }
        }
    }

    /// <summary>The counts of <paramref name="model"/>'s designs (counted once per model).</summary>
    public static PairCounts For(TerrainModel model) => Counts.GetValue(model, m => new PairCounts(m));

    /// <summary>How often <paramref name="next"/> stands in <paramref name="direction"/> of <paramref name="tile"/>.</summary>
    public int Count(int tile, int direction, int next) => counts[direction][(tile * TileSet.Capacity) + next];

    private void Add(int tile, int direction, int next)
    {
        counts[direction][(tile * TileSet.Capacity) + next]++;
        counts[TerrainModel.Opposite(direction)][(next * TileSet.Capacity) + tile]++;
    }
}
