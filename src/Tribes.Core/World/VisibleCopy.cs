using System;
using System.Collections.Generic;

namespace Tribes.Core.World;

/// <summary>
/// A copy of what a tribe sees now (the visible layer of its fog), kept to be added to other
/// tribes' fog after the tribe's own fog has changed. A fixed map's copy is a flag per cell; an
/// endless map's holds only the chunks with anything visible. Reused from one copy to the next.
/// </summary>
internal sealed class VisibleCopy
{
    private readonly List<int> ordinals = new();
    private readonly List<ulong[]> layers = new();
    private int used;

    /// <summary>A fixed map's visible flags, row by row (empty for an endless map's copy).</summary>
    public bool[] Cells { get; private set; } = Array.Empty<bool>();

    /// <summary>The number of chunks copied from an endless map's fog.</summary>
    public int ChunkCount => used;

    /// <summary>The place in the map's order of chunks of copied chunk <paramref name="index"/>.</summary>
    public int ChunkOrdinal(int index) => ordinals[index];

    /// <summary>The visible words of copied chunk <paramref name="index"/>.</summary>
    public ReadOnlySpan<ulong> ChunkLayer(int index) => layers[index];

    /// <summary>Copies the visible flags of a fixed map's fog.</summary>
    public void SetCells(ReadOnlySpan<bool> visible)
    {
        if (Cells.Length != visible.Length)
        {
            Cells = new bool[visible.Length];
        }
        visible.CopyTo(Cells);
        used = 0;
    }

    /// <summary>Forgets the chunks copied before (the next copy is of an endless map's fog).</summary>
    public void ClearChunks()
    {
        Cells = Array.Empty<bool>();
        used = 0;
    }

    /// <summary>Adds the visible words of the chunk at <paramref name="ordinal"/>.</summary>
    public void AddChunk(int ordinal, ReadOnlySpan<ulong> visible)
    {
        if (used == layers.Count)
        {
            ordinals.Add(0);
            layers.Add(new ulong[visible.Length]);
        }
        ordinals[used] = ordinal;
        visible.CopyTo(layers[used]);
        used++;
    }
}
