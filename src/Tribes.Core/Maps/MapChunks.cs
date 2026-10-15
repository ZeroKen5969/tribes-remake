using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tribes.Core.Content;

namespace Tribes.Core.Maps;

/// <summary>
/// A chunk of an endless map: <see cref="WorldMap.ChunkSize"/> by <see cref="WorldMap.ChunkSize"/>
/// cells; chunk (X, Y) holds the cells (64X .. 64X + 63, 64Y .. 64Y + 63). Coordinates may be negative.
/// </summary>
public readonly record struct ChunkCoord(int X, int Y)
{
    /// <summary>The chunk holding <paramref name="cell"/> (floor division, also for negative cells).</summary>
    public static ChunkCoord Of(GridPoint cell) => new(cell.X >> WorldMap.ChunkShift, cell.Y >> WorldMap.ChunkShift);

    /// <summary>The chunk's top-left cell.</summary>
    public GridPoint Origin => new(X << WorldMap.ChunkShift, Y << WorldMap.ChunkShift);

    /// <summary>The chunk's cells.</summary>
    public GridRect Cells => new(X << WorldMap.ChunkShift, Y << WorldMap.ChunkShift, WorldMap.ChunkSize, WorldMap.ChunkSize);
}

/// <summary>
/// The terrain and cell contents of one generated chunk of an endless map. Read it to draw the
/// chunk; the rules change it through <see cref="WorldMap"/>.
/// </summary>
public sealed class MapChunk
{
    private const int CellCount = WorldMap.ChunkSize * WorldMap.ChunkSize;

    internal MapChunk(ChunkCoord coord, int ordinal, int generatorVersion)
    {
        Coord = coord;
        Ordinal = ordinal;
        GeneratorVersion = generatorVersion;
    }

    /// <summary>Which chunk it is.</summary>
    public ChunkCoord Coord { get; }

    /// <summary>The chunk's cells.</summary>
    public GridRect Cells => Coord.Cells;

    /// <summary>The version of the terrain generator that made it (it never changes afterwards).</summary>
    public int GeneratorVersion { get; }

    /// <summary>
    /// Grows whenever a tile or cell of the chunk changes, so a view can tell when to redraw it. It is
    /// not part of the game's state (a loaded chunk starts again from 0).
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Its place in the order the chunks were made (0 first).</summary>
    internal int Ordinal { get; }

    internal byte[] Tiles { get; } = new byte[CellCount];

    internal MapCell[] CellArray { get; } = new MapCell[CellCount];

    /// <summary>The terrain tile at <paramref name="cell"/>, a cell of this chunk.</summary>
    public int TileAt(GridPoint cell) => Tiles[IndexOf(cell)];

    /// <summary>What fills <paramref name="cell"/>, a cell of this chunk.</summary>
    public MapCell CellAt(GridPoint cell) => CellArray[IndexOf(cell)];

    /// <summary>The index of cell (<paramref name="x"/>, <paramref name="y"/>) in its chunk's arrays (rows from the top).</summary>
    internal static int LocalIndex(int x, int y) => ((y & WorldMap.ChunkMask) << WorldMap.ChunkShift) | (x & WorldMap.ChunkMask);

    internal void SetTile(int index, byte tile)
    {
        Tiles[index] = tile;
        Revision++;
    }

    internal void SetCell(int index, MapCell cell)
    {
        CellArray[index] = cell;
        Revision++;
    }

    private int IndexOf(GridPoint cell) => Cells.Contains(cell)
        ? LocalIndex(cell.X, cell.Y)
        : throw new ArgumentOutOfRangeException(nameof(cell), cell, $"not in chunk {Coord}");
}

/// <summary>The generated chunks of an endless map and a quick index from a cell to its chunk.</summary>
internal sealed class ChunkGrid
{
    /// <summary>Chunks of room left around the indexed area when it grows, so it seldom grows.</summary>
    private const int GrowthMargin = 8;

    private const int None = -1;

    private readonly List<MapChunk> chunks = new();
    private int[] index = Array.Empty<int>();
    private int originX, originY, width, height;
    private GridRect chunkBounds;

    public ChunkGrid() => Chunks = new ReadOnlyCollection<MapChunk>(chunks);

    /// <summary>Every chunk, in the order they were made.</summary>
    public IReadOnlyList<MapChunk> Chunks { get; }

    /// <summary>The bounding box of the chunks, in cells.</summary>
    public GridRect Bounds => new(chunkBounds.X << WorldMap.ChunkShift, chunkBounds.Y << WorldMap.ChunkShift,
        chunkBounds.Width << WorldMap.ChunkShift, chunkBounds.Height << WorldMap.ChunkShift);

    /// <summary>The chunk (X, Y), or null when it has not been made.</summary>
    public MapChunk? Find(int chunkX, int chunkY)
    {
        int ix = chunkX - originX, iy = chunkY - originY;
        if ((uint)ix >= (uint)width || (uint)iy >= (uint)height)
        {
            return null;
        }
        int ordinal = index[(iy * width) + ix];
        return ordinal == None ? null : chunks[ordinal];
    }

    /// <summary>The chunk holding cell (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public MapChunk? FindCell(int x, int y) => Find(x >> WorldMap.ChunkShift, y >> WorldMap.ChunkShift);

    /// <summary>Makes chunk <paramref name="coord"/> (empty cells, tile 0).</summary>
    public MapChunk Add(ChunkCoord coord, int generatorVersion)
    {
        if (Find(coord.X, coord.Y) != null)
        {
            throw new InvalidOperationException($"chunk {coord} exists already");
        }
        EnsureIndexed(coord);
        var chunk = new MapChunk(coord, chunks.Count, generatorVersion);
        chunks.Add(chunk);
        index[((coord.Y - originY) * width) + coord.X - originX] = chunk.Ordinal;
        chunkBounds = chunkBounds.Union(new GridRect(coord.X, coord.Y, 1, 1));
        return chunk;
    }

    /// <summary>Forgets every chunk.</summary>
    public void Clear()
    {
        chunks.Clear();
        index = Array.Empty<int>();
        (originX, originY, width, height) = (0, 0, 0, 0);
        chunkBounds = default;
    }

    private void EnsureIndexed(ChunkCoord coord)
    {
        if (coord.X >= originX && coord.Y >= originY && coord.X < originX + width && coord.Y < originY + height)
        {
            return;
        }
        GridRect wanted = new GridRect(originX, originY, width, height)
            .Union(GridRect.FromEdges(coord.X - GrowthMargin, coord.Y - GrowthMargin, coord.X + GrowthMargin + 1, coord.Y + GrowthMargin + 1));
        int[] grown = new int[wanted.Width * wanted.Height];
        Array.Fill(grown, None);
        for (int y = 0; y < height; y++)
        {
            Array.Copy(index, y * width, grown, ((y + originY - wanted.Y) * wanted.Width) + originX - wanted.X, width);
        }
        (index, originX, originY, width, height) = (grown, wanted.X, wanted.Y, wanted.Width, wanted.Height);
    }
}
