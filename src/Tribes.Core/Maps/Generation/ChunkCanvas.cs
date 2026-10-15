using System;
using System.Collections.Generic;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// The design of one chunk while it is made: <see cref="WorldMap.ChunkSize"/> cells square plus a
/// ring of one cell around it holding the edge of the neighbouring chunks that exist already
/// (cells of chunks not made yet are <see cref="Open"/>: they ask nothing). Coordinates are the
/// chunk's own, from (-1, -1) to (64, 64).
/// </summary>
internal sealed class ChunkCanvas
{
    /// <summary>Cells along a side of the chunk.</summary>
    public const int Size = WorldMap.ChunkSize;

    /// <summary>A cell with no tile yet.</summary>
    public const int Unset = -1;

    /// <summary>A ring cell of land not made yet.</summary>
    public const int Open = -2;

    private const int Stride = Size + 2;

    /// <summary>The cells of the ring, in a fixed order: the row above, the row below, then the column left and the column right.</summary>
    public static IReadOnlyList<(int X, int Y)> Ring { get; } = RingCells();

    private readonly int[] tiles = new int[Stride * Stride];
    private readonly bool[] locked = new bool[Stride * Stride];
    private readonly bool[] walkableOnly = new bool[Stride * Stride];

    /// <summary>A canvas with every chunk cell unset and the ring open.</summary>
    public ChunkCanvas()
    {
        Array.Fill(tiles, Open);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                tiles[IndexOf(x, y)] = Unset;
            }
        }
    }

    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is a cell of the chunk (not the ring).</summary>
    public static bool InChunk(int x, int y) => (uint)x < Size && (uint)y < Size;

    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is on the canvas (chunk or ring).</summary>
    public static bool OnCanvas(int x, int y) => x >= -1 && y >= -1 && x <= Size && y <= Size;

    /// <summary>The tile at a cell (a tile, <see cref="Unset"/> or <see cref="Open"/>).</summary>
    public int this[int x, int y]
    {
        get => tiles[IndexOf(x, y)];
        set => tiles[IndexOf(x, y)] = value;
    }

    /// <summary>Whether the chunk cell must keep its tile (a connector or a corner).</summary>
    public bool IsLocked(int x, int y) => locked[IndexOf(x, y)];

    /// <summary>Whether the chunk cell must keep a tile units can walk on (a carved path); its tile may still change.</summary>
    public bool MustStayWalkable(int x, int y) => walkableOnly[IndexOf(x, y)];

    /// <summary>Gives a chunk cell <paramref name="tile"/> (walkable) and lets it only ever take walkable tiles.</summary>
    public void KeepWalkable(int x, int y, int tile)
    {
        tiles[IndexOf(x, y)] = tile;
        walkableOnly[IndexOf(x, y)] = true;
    }

    /// <summary>Sets the tile of a chunk cell and keeps it from then on.</summary>
    public void Lock(int x, int y, int tile)
    {
        tiles[IndexOf(x, y)] = tile;
        locked[IndexOf(x, y)] = true;
    }

    private static int IndexOf(int x, int y) => ((y + 1) * Stride) + x + 1;

    private static (int X, int Y)[] RingCells()
    {
        var cells = new List<(int X, int Y)>((4 * Size) + 4);
        for (int x = -1; x <= Size; x++)
        {
            cells.Add((x, -1));
        }
        for (int x = -1; x <= Size; x++)
        {
            cells.Add((x, Size));
        }
        for (int y = 0; y < Size; y++)
        {
            cells.Add((-1, y));
        }
        for (int y = 0; y < Size; y++)
        {
            cells.Add((Size, y));
        }
        return [.. cells];
    }
}
