using System;
using System.Collections.Generic;

namespace Tribes.Client.World.Scene;

/// <summary>
/// How the terrain is cut into blocks of cells, each rasterised into one picture. Blocks are
/// counted from cell (0, 0) in both directions (block -1 holds cells -16..-1), so negative cells of
/// an endless map tile like the others; four blocks side by side make one map chunk, so a chunk
/// never splits a block. A block is drawn with <see cref="TerrainLayer.Margin"/> cells around it,
/// so its edges show the parts that reach in from the neighbouring cells and no seam appears.
/// </summary>
public static class TerrainTiling
{
    /// <summary>Cells per side of a terrain block.</summary>
    public const int BlockCells = 16;

    /// <summary>Cells per side of an endless map chunk.</summary>
    public const int ChunkCells = 64;

    /// <summary>Floor division (rounds towards minus infinity, also for negative cells).</summary>
    public static int FloorDiv(int value, int divisor)
    {
        if (divisor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "must be positive");
        }
        int quotient = value / divisor;
        return (value % divisor != 0 && value < 0) ? quotient - 1 : quotient;
    }

    /// <summary>The block holding cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static (int X, int Y) BlockOf(int x, int y) => (FloorDiv(x, BlockCells), FloorDiv(y, BlockCells));

    /// <summary>The cells of block (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
    public static CellRect CellsOf(int blockX, int blockY) => new(blockX * BlockCells, blockY * BlockCells, BlockCells, BlockCells);

    /// <summary>
    /// The blocks to draw for <paramref name="view"/>: those under it and the cell around it (the
    /// object layer draws one cell beyond the view), as an inclusive block range.
    /// </summary>
    public static (int X0, int Y0, int X1, int Y1) BlocksFor(ViewRect view) =>
        (FloorDiv(view.Column - 1, BlockCells), FloorDiv(view.Row - 1, BlockCells),
            FloorDiv(view.Column + view.Columns, BlockCells), FloorDiv(view.Row + view.Rows, BlockCells));

    /// <summary>
    /// The blocks just outside <paramref name="view"/>'s blocks, <paramref name="rings"/> deep,
    /// nearest to the view's middle first: drawn ahead while the frame has time to spare, so
    /// panning finds them ready.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> AheadOf(ViewRect view, int rings)
    {
        (int x0, int y0, int x1, int y1) = BlocksFor(view);
        var ahead = new List<(int X, int Y)>();
        for (int y = y0 - rings; y <= y1 + rings; y++)
        {
            for (int x = x0 - rings; x <= x1 + rings; x++)
            {
                if (x < x0 || x > x1 || y < y0 || y > y1)
                {
                    ahead.Add((x, y));
                }
            }
        }
        // Distances in doubled block units from the middle of the view's blocks, so they stay whole numbers.
        int midX = x0 + x1, midY = y0 + y1;
        ahead.Sort((a, b) =>
        {
            int da = Math.Abs((2 * a.X) - midX) + Math.Abs((2 * a.Y) - midY);
            int db = Math.Abs((2 * b.X) - midX) + Math.Abs((2 * b.Y) - midY);
            return da != db ? da.CompareTo(db) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X);
        });
        return ahead;
    }

    /// <summary>
    /// Terrain blocks kept in memory while <paramref name="drawn"/> blocks are on screen or drawn
    /// ahead: twice as many, so panning back and forth finds them; the least recently used beyond
    /// that are freed, so panning over a large map never adds up.
    /// </summary>
    public static int BlocksKept(int drawn) => Math.Max(32, 2 * drawn);
}
