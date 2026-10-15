using System;
using System.Collections.Generic;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// The sector heads of the sector index on an endless map: for each chunk that held an indexed
/// object since the last rebuild, the head of every category in each of its 4 x 4 sectors, kept by
/// the chunk's place in the map's order of chunks. Sectors without a table read as empty.
/// </summary>
internal sealed class ChunkSectors
{
    /// <summary>Sectors along each side of a chunk.</summary>
    private const int SectorsPerChunkSide = WorldMap.ChunkSize / SectorIndex.SectorSize;

    private const int SectorsPerChunk = SectorsPerChunkSide * SectorsPerChunkSide;

    /// <summary>A sector coordinate shifted right by this gives its chunk coordinate.</summary>
    private const int ChunkShift = 2;

    private const int SectorMask = SectorsPerChunkSide - 1;

    private const int EmptyChain = -1;

    private readonly WorldMap map;
    private readonly List<int[]?> tables = new();
    private readonly List<int> touched = new();
    private readonly List<bool> listed = new();
    private readonly List<ulong[]?> present = new();

    public ChunkSectors(WorldMap map) => this.map = map;

    public int Head(int category, int sectorX, int sectorY)
    {
        MapChunk? chunk = map.Chunk(new ChunkCoord(sectorX >> ChunkShift, sectorY >> ChunkShift));
        if (chunk == null || chunk.Ordinal >= tables.Count || tables[chunk.Ordinal] is not { } table)
        {
            return EmptyChain;
        }
        int index = (category * SectorsPerChunk) + Local(sectorX, sectorY);
        return index < table.Length ? table[index] : EmptyChain;
    }

    public void SetHead(int category, int sectorX, int sectorY, int slot, int categoryCount)
    {
        MapChunk chunk = map.Chunk(new ChunkCoord(sectorX >> ChunkShift, sectorY >> ChunkShift))
            ?? throw new ArgumentOutOfRangeException(nameof(sectorX), (sectorX, sectorY), "no land at that sector");
        Table(chunk.Ordinal, categoryCount)[(category * SectorsPerChunk) + Local(sectorX, sectorY)] = slot;
        if (slot != EmptyChain)
        {
            ulong[] bits = present[chunk.Ordinal] ??= new ulong[(categoryCount + 63) >> 6];
            if (bits.Length <= category >> 6)
            {
                Array.Resize(ref bits, (categoryCount + 63) >> 6);
                present[chunk.Ordinal] = bits;
            }
            bits[category >> 6] |= 1UL << (category & 63);
        }
    }

    /// <summary>
    /// Whether any sector from (<paramref name="minX"/>, <paramref name="minY"/>) to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>) may head a chain of <paramref name="category"/>
    /// (false: every one of them is empty, so a search there can be skipped).
    /// </summary>
    public bool MayHold(int category, int minX, int maxX, int minY, int maxY)
    {
        for (int cy = minY >> ChunkShift; cy <= maxY >> ChunkShift; cy++)
        {
            for (int cx = minX >> ChunkShift; cx <= maxX >> ChunkShift; cx++)
            {
                MapChunk? chunk = map.Chunk(new ChunkCoord(cx, cy));
                if (chunk != null && chunk.Ordinal < present.Count && present[chunk.Ordinal] is { } bits
                    && category >> 6 < bits.Length && (bits[category >> 6] & (1UL << (category & 63))) != 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Empties every sector.</summary>
    public void Clear()
    {
        foreach (int ordinal in touched)
        {
            Array.Fill(tables[ordinal]!, EmptyChain);
            listed[ordinal] = false;
            if (present[ordinal] is { } bits)
            {
                Array.Clear(bits);
            }
        }
        touched.Clear();
    }

    /// <summary>
    /// Saves, loads or hashes the heads that are not empty: their number, then each one's chunk
    /// place, category, sector in the chunk and slot, in the map's order of chunks.
    /// </summary>
    public void Exchange(StateExchange s, int categoryCount)
    {
        int count = s.Value(CountUsed());
        if (s.IsReading)
        {
            tables.Clear();
            touched.Clear();
            listed.Clear();
            present.Clear();
            for (int i = 0; i < count; i++)
            {
                int ordinal = s.Value(0), category = s.Value(0), local = s.Value(0), slot = s.Value(0);
                if (ordinal < 0 || ordinal >= map.Chunks.Count || category < 0 || category >= categoryCount
                    || local < 0 || local >= SectorsPerChunk)
                {
                    throw new System.IO.InvalidDataException($"sector head {ordinal}/{category}/{local} is not on the map");
                }
                Table(ordinal, categoryCount)[(category * SectorsPerChunk) + local] = slot;
                MarkPresent(ordinal, category, categoryCount);
            }
            return;
        }
        for (int ordinal = 0; ordinal < tables.Count; ordinal++)
        {
            if (tables[ordinal] is not { } table)
            {
                continue;
            }
            for (int index = 0; index < table.Length; index++)
            {
                if (table[index] != EmptyChain)
                {
                    s.Value(ordinal);
                    s.Value(index / SectorsPerChunk);
                    s.Value(index % SectorsPerChunk);
                    s.Value(table[index]);
                }
            }
        }
    }

    private int CountUsed()
    {
        int count = 0;
        foreach (int[]? table in tables)
        {
            if (table != null)
            {
                foreach (int head in table)
                {
                    count += head != EmptyChain ? 1 : 0;
                }
            }
        }
        return count;
    }

    private void MarkPresent(int ordinal, int category, int categoryCount)
    {
        ulong[] bits = present[ordinal] ??= new ulong[(categoryCount + 63) >> 6];
        bits[category >> 6] |= 1UL << (category & 63);
    }

    private static int Local(int sectorX, int sectorY) => ((sectorY & SectorMask) * SectorsPerChunkSide) + (sectorX & SectorMask);

    private int[] Table(int ordinal, int categoryCount)
    {
        while (tables.Count <= ordinal)
        {
            tables.Add(null);
            listed.Add(false);
            present.Add(null);
        }
        int[]? table = tables[ordinal];
        int length = categoryCount * SectorsPerChunk;
        if (table == null || table.Length < length)
        {
            int[] grown = new int[length];
            Array.Fill(grown, EmptyChain);
            table?.CopyTo(grown, 0);
            tables[ordinal] = table = grown;
        }
        if (!listed[ordinal])
        {
            listed[ordinal] = true;
            touched.Add(ordinal);
        }
        return table;
    }
}
