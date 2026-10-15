using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// The fog of one tribe on an endless map: two bits per cell (explored, visible) for each chunk the
/// tribe has seen anything of, kept by the chunk's place in the map's order of chunks. Cells of
/// land not made yet are never explored nor visible. For state hashes each chunk keeps a digest of
/// its layers, made again only after they changed, so a hash costs little however much is explored.
/// </summary>
internal sealed class ChunkFog
{
    /// <summary>Words of 64 cells per chunk layer.</summary>
    public const int WordsPerLayer = WorldMap.ChunkSize * WorldMap.ChunkSize / 64;

    /// <summary>Where a chunk's visible layer starts in its words (the explored layer comes first).</summary>
    public const int VisibleLayer = WordsPerLayer;

    private readonly WorldMap map;
    private readonly List<ulong[]?> bits = new();
    private readonly List<int> seenNow = new();
    private readonly List<bool> listed = new();
    private readonly List<ulong> digests = new();
    private readonly List<bool> stale = new();
    private readonly List<long> revisions = new();
    private long clock;

    public ChunkFog(WorldMap map) => this.map = map;

    /// <summary>The size of the map's bounding box.</summary>
    public GridSize MapSize => map.Size;

    public bool IsExplored(GridPoint cell) => Get(cell, 0);

    public bool IsVisible(GridPoint cell) => Get(cell, VisibleLayer);

    public void SetExplored(GridPoint cell, bool value) => Set(cell, 0, value);

    public void SetVisible(GridPoint cell, bool value)
    {
        if (Set(cell, VisibleLayer, value) is int ordinal && value && !listed[ordinal])
        {
            listed[ordinal] = true;
            seenNow.Add(ordinal);
        }
    }

    /// <summary>
    /// A number that grows whenever the layers of the chunk at <paramref name="ordinal"/> may have
    /// changed (never smaller than before, also across a load); 0 while it has none.
    /// </summary>
    public long ChunkRevision(int ordinal) => ordinal >= 0 && ordinal < revisions.Count ? revisions[ordinal] : 0;

    /// <summary>
    /// The layers of the chunk at <paramref name="ordinal"/> (<see cref="WordsPerLayer"/> words of the
    /// explored layer, then as many of the visible layer), or null while nothing of it was seen.
    /// </summary>
    public ReadOnlySpan<ulong> Layers(int ordinal) =>
        ordinal >= 0 && ordinal < bits.Count && bits[ordinal] is { } words ? words : ReadOnlySpan<ulong>.Empty;

    /// <summary>Forgets what is visible now (only the chunks that had anything visible are touched).</summary>
    public void ClearVisible()
    {
        foreach (int ordinal in seenNow)
        {
            Array.Clear(bits[ordinal]!, VisibleLayer, WordsPerLayer);
            listed[ordinal] = false;
            Touch(ordinal);
        }
        seenNow.Clear();
    }

    /// <summary>Adds what is visible now to what was explored.</summary>
    public void ExploreVisible()
    {
        foreach (int ordinal in seenNow)
        {
            ulong[] words = bits[ordinal]!;
            for (int i = 0; i < WordsPerLayer; i++)
            {
                words[i] |= words[VisibleLayer + i];
            }
            Touch(ordinal);
        }
    }

    /// <summary>Forgets what was explored.</summary>
    public void ClearExplored()
    {
        for (int ordinal = 0; ordinal < bits.Count; ordinal++)
        {
            if (bits[ordinal] is { } words)
            {
                Array.Clear(words, 0, WordsPerLayer);
                Touch(ordinal);
            }
        }
    }

    /// <summary>
    /// Adds what <paramref name="other"/> (another tribe's fog of the same map) has explored to what
    /// this fog has explored; true when anything was added.
    /// </summary>
    public bool AddExplored(ChunkFog other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.map != map)
        {
            throw new ArgumentException("the fogs cover different maps", nameof(other));
        }
        bool changed = false;
        for (int ordinal = 0; ordinal < other.bits.Count; ordinal++)
        {
            if (other.bits[ordinal] is not { } source || source.AsSpan(0, WordsPerLayer).IndexOfAnyExcept(0UL) < 0)
            {
                continue;
            }
            ulong[] target = Words(ordinal)!;
            ulong added = 0;
            for (int i = 0; i < WordsPerLayer; i++)
            {
                added |= source[i] & ~target[i];
                target[i] |= source[i];
            }
            if (added != 0)
            {
                Touch(ordinal);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Whether any cell of <paramref name="area"/> is visible (only the chunks with anything visible are looked at).</summary>
    public bool AnyVisible(GridRect area)
    {
        if (area.IsEmpty)
        {
            return false;
        }
        for (int cy = area.Top >> WorldMap.ChunkShift; cy <= (area.Bottom - 1) >> WorldMap.ChunkShift; cy++)
        {
            for (int cx = area.Left >> WorldMap.ChunkShift; cx <= (area.Right - 1) >> WorldMap.ChunkShift; cx++)
            {
                MapChunk? chunk = map.Chunk(new ChunkCoord(cx, cy));
                if (chunk == null || chunk.Ordinal >= listed.Count || !listed[chunk.Ordinal])
                {
                    continue;
                }
                ulong[] words = bits[chunk.Ordinal]!;
                GridRect cells = chunk.Cells;
                for (int y = Math.Max(area.Top, cells.Top); y < Math.Min(area.Bottom, cells.Bottom); y++)
                {
                    for (int x = Math.Max(area.Left, cells.Left); x < Math.Min(area.Right, cells.Right); x++)
                    {
                        int index = MapChunk.LocalIndex(x, y);
                        if ((words[VisibleLayer + (index >> 6)] & (1UL << (index & 63))) != 0)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    /// <summary>Copies the visible layer of every chunk with anything visible into <paramref name="copy"/>.</summary>
    public void CopyVisible(VisibleCopy copy)
    {
        copy.ClearChunks();
        foreach (int ordinal in seenNow)
        {
            copy.AddChunk(ordinal, bits[ordinal]!.AsSpan(VisibleLayer, WordsPerLayer));
        }
    }

    /// <summary>Makes visible now what <paramref name="copy"/> (of another tribe's fog of the same map) holds as visible.</summary>
    public void AddVisible(VisibleCopy copy)
    {
        for (int i = 0; i < copy.ChunkCount; i++)
        {
            int ordinal = copy.ChunkOrdinal(i);
            ulong[] words = Words(ordinal) ?? throw new ArgumentException($"no chunk {ordinal} on this map", nameof(copy));
            ReadOnlySpan<ulong> visible = copy.ChunkLayer(i);
            for (int w = 0; w < WordsPerLayer; w++)
            {
                words[VisibleLayer + w] |= visible[w];
            }
            if (!listed[ordinal])
            {
                listed[ordinal] = true;
                seenNow.Add(ordinal);
            }
            Touch(ordinal);
        }
    }

    /// <summary>The bounding box of the explored cells (empty when nothing is explored).</summary>
    public GridRect ExploredBounds()
    {
        GridRect bounds = default;
        for (int ordinal = 0; ordinal < bits.Count; ordinal++)
        {
            if (bits[ordinal] is not { } words)
            {
                continue;
            }
            GridPoint origin = map.Chunks[ordinal].Coord.Origin;
            for (int word = 0; word < WordsPerLayer; word++)
            {
                if (words[word] == 0)
                {
                    continue;
                }
                int first = System.Numerics.BitOperations.TrailingZeroCount(words[word]);
                int last = 63 - System.Numerics.BitOperations.LeadingZeroCount(words[word]);
                int row = word * 64 / WorldMap.ChunkSize, column = (word * 64) % WorldMap.ChunkSize;
                bounds = bounds.Union(GridRect.FromEdges(origin.X + column + first, origin.Y + row,
                    origin.X + column + last + 1, origin.Y + row + 1));
            }
        }
        return bounds;
    }

    /// <summary>
    /// Saves, loads or hashes the fog: the number of chunks with anything explored or visible, then
    /// for each (in the map's order) its place and its two layers (a hash takes each chunk's digest).
    /// </summary>
    public void Exchange(StateExchange s)
    {
        if (s is StateHasher hasher)
        {
            Hash(hasher);
            return;
        }
        int count = s.Value(CountUsed());
        if (s.IsReading)
        {
            Read(s, count);
            return;
        }
        for (int ordinal = 0; ordinal < bits.Count; ordinal++)
        {
            if (bits[ordinal] is { } words && Used(words))
            {
                s.Value(ordinal);
                foreach (ulong word in words)
                {
                    s.Value64((long)word);
                }
            }
        }
    }

    private void Read(StateExchange s, int count)
    {
        bits.Clear();
        seenNow.Clear();
        listed.Clear();
        digests.Clear();
        stale.Clear();
        for (int i = 0; i < count; i++)
        {
            int ordinal = s.Value(0);
            ulong[] words = Words(ordinal) ?? throw new System.IO.InvalidDataException($"fog of chunk {ordinal}: no such chunk");
            for (int w = 0; w < words.Length; w++)
            {
                words[w] = (ulong)s.Value64(0);
            }
            MarkSeen(ordinal, words);
        }
        // Every chunk may have changed, and no revision may go back.
        for (int ordinal = 0; ordinal < revisions.Count; ordinal++)
        {
            revisions[ordinal] = ++clock;
        }
    }

    /// <summary>Mixes the number of chunks with anything explored or visible, then each one's place and digest.</summary>
    private void Hash(StateHasher hasher)
    {
        int count = 0;
        for (int ordinal = 0; ordinal < bits.Count; ordinal++)
        {
            count += Digest(ordinal) != 0 ? 1 : 0;
        }
        hasher.Value(count);
        for (int ordinal = 0; ordinal < bits.Count; ordinal++)
        {
            ulong digest = Digest(ordinal);
            if (digest != 0)
            {
                hasher.Value(ordinal);
                hasher.Value64((long)digest);
            }
        }
    }

    /// <summary>The digest of the layers of the chunk at <paramref name="ordinal"/>; 0 when nothing of it is explored or visible.</summary>
    private ulong Digest(int ordinal)
    {
        if (stale[ordinal])
        {
            ulong digest = 0;
            if (bits[ordinal] is { } words && Used(words))
            {
                var local = new StateHasher();
                local.MixAll(words);
                digest = local.Hash;
            }
            digests[ordinal] = digest;
            stale[ordinal] = false;
        }
        return digests[ordinal];
    }

    /// <summary>Notes that the layers of the chunk at <paramref name="ordinal"/> changed.</summary>
    private void Touch(int ordinal)
    {
        stale[ordinal] = true;
        revisions[ordinal] = ++clock;
    }

    private void MarkSeen(int ordinal, ulong[] words)
    {
        for (int w = VisibleLayer; w < words.Length; w++)
        {
            if (words[w] != 0)
            {
                listed[ordinal] = true;
                seenNow.Add(ordinal);
                return;
            }
        }
    }

    private int CountUsed()
    {
        int count = 0;
        foreach (ulong[]? words in bits)
        {
            count += words != null && Used(words) ? 1 : 0;
        }
        return count;
    }

    private static bool Used(ulong[] words) => words.AsSpan().IndexOfAnyExcept(0UL) >= 0;

    private bool Get(GridPoint cell, int layer)
    {
        MapChunk? chunk = map.ChunkAtCell(cell);
        if (chunk == null || chunk.Ordinal >= bits.Count || bits[chunk.Ordinal] is not { } words)
        {
            return false;
        }
        int index = MapChunk.LocalIndex(cell.X, cell.Y);
        return (words[layer + (index >> 6)] & (1UL << (index & 63))) != 0;
    }

    /// <summary>Sets a cell's bit of <paramref name="layer"/>; the chunk's place, or null when the land is not made.</summary>
    private int? Set(GridPoint cell, int layer, bool value)
    {
        MapChunk? chunk = map.ChunkAtCell(cell);
        if (chunk == null || (!value && (chunk.Ordinal >= bits.Count || bits[chunk.Ordinal] == null)))
        {
            return null;
        }
        ulong[] words = Words(chunk.Ordinal)!;
        int index = MapChunk.LocalIndex(cell.X, cell.Y);
        ulong bit = 1UL << (index & 63);
        words[layer + (index >> 6)] = value ? words[layer + (index >> 6)] | bit : words[layer + (index >> 6)] & ~bit;
        Touch(chunk.Ordinal);
        return chunk.Ordinal;
    }

    /// <summary>The layers of the chunk at <paramref name="ordinal"/> (made when first needed), or null when there is no such chunk.</summary>
    private ulong[]? Words(int ordinal)
    {
        if (ordinal < 0 || ordinal >= map.Chunks.Count)
        {
            return null;
        }
        while (bits.Count <= ordinal)
        {
            bits.Add(null);
            listed.Add(false);
            digests.Add(0);
            stale.Add(true);
        }
        while (revisions.Count <= ordinal)
        {
            revisions.Add(0);
        }
        return bits[ordinal] ??= new ulong[2 * WordsPerLayer];
    }
}
