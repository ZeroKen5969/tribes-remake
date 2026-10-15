using System;
using Tribes.Core.World;

namespace Tribes.Core.Maps;

/// <summary>
/// Saves, loads or hashes the chunks of an endless map: their number, then for each chunk (in the
/// order they were made) its coordinates, the generator version that made it, its tiles (a byte
/// each) and its cells as runs of equal cells (most of a chunk is empty ground; a run's slot and
/// owner are written only when they are not 0).
/// </summary>
internal static class ChunkExchange
{
    private const int CellCount = WorldMap.ChunkSize * WorldMap.ChunkSize;

    /// <summary>Marks a run's cell kind when its slot and owner follow (most runs are plain ground, water, thicket or rock).</summary>
    private const int WithSlot = 0x100;

    public static void Exchange(StateExchange s, ChunkGrid grid)
    {
        int count = s.Value(grid.Chunks.Count);
        if (s.IsReading)
        {
            grid.Clear();
            for (int i = 0; i < count; i++)
            {
                var coord = new ChunkCoord(s.Value(0), s.Value(0));
                MapChunk chunk = grid.Add(coord, s.Value(0));
                s.Bytes(chunk.Tiles);
                ReadCells(s, chunk.CellArray);
            }
            return;
        }
        foreach (MapChunk chunk in grid.Chunks)
        {
            s.Value(chunk.Coord.X);
            s.Value(chunk.Coord.Y);
            s.Value(chunk.GeneratorVersion);
            s.Bytes(chunk.Tiles);
            WriteCells(s, chunk.CellArray);
        }
    }

    /// <summary>Hashes which chunks there are and their generator versions (their contents are in the map's running hash).</summary>
    public static void HashList(StateExchange s, ChunkGrid grid)
    {
        s.Value(grid.Chunks.Count);
        foreach (MapChunk chunk in grid.Chunks)
        {
            s.Value(chunk.Coord.X);
            s.Value(chunk.Coord.Y);
            s.Value(chunk.GeneratorVersion);
        }
    }

    private static void WriteCells(StateExchange s, MapCell[] cells)
    {
        for (int start = 0; start < cells.Length;)
        {
            int end = start + 1;
            while (end < cells.Length && cells[end] == cells[start])
            {
                end++;
            }
            MapCell cell = cells[start];
            s.Value(end - start);
            bool plain = cell.Index == 0 && cell.Owner == 0;
            s.Value((int)cell.Content | (plain ? 0 : WithSlot));
            if (!plain)
            {
                s.Value(cell.Index);
                s.Value(cell.Owner);
            }
            start = end;
        }
    }

    private static void ReadCells(StateExchange s, MapCell[] cells)
    {
        for (int start = 0; start < cells.Length;)
        {
            int run = RunLength(s, start);
            int content = s.Value(0);
            var cell = (content & WithSlot) != 0
                ? new MapCell((CellContent)(content & ~WithSlot), s.Value(0), s.Value(0))
                : new MapCell((CellContent)content, 0, 0);
            cells.AsSpan(start, run).Fill(cell);
            start += run;
        }
    }

    /// <summary>A run length read back; a run that would not fit the chunk means the data is damaged.</summary>
    private static int RunLength(StateExchange s, int start)
    {
        int run = s.Value(0);
        return run >= 1 && run <= CellCount - start
            ? run
            : throw new System.IO.InvalidDataException($"chunk run of {run} cells at cell {start}");
    }
}
