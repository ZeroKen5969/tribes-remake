using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>What one tribe has explored and can see now.</summary>
public sealed class FogOfWar : IFogView
{
    private readonly bool[] explored;
    private readonly bool[] visible;
    private readonly GridSize size;
    private readonly ChunkFog? chunks;

    /// <summary>Creates fog covering a map of <paramref name="size"/>.</summary>
    public FogOfWar(GridSize size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "map size must be positive");
        }
        this.size = size;
        explored = new bool[size.Width * size.Height];
        visible = new bool[size.Width * size.Height];
    }

    private FogOfWar(ChunkFog chunks)
    {
        this.chunks = chunks;
        explored = visible = Array.Empty<bool>();
    }

    /// <summary>Fog over the chunks of the endless map <paramref name="map"/>, however many there are.</summary>
    internal static FogOfWar ForChunks(WorldMap map) => new(new ChunkFog(map));

    /// <summary>Size of the covered map (an endless map's fog: of nothing in particular, see <see cref="ExploredBounds"/>).</summary>
    public GridSize Size => chunks?.MapSize ?? size;

    /// <summary>The buildings the tribe has seen, each as it last saw it (kept in a match only).</summary>
    public BuildingMemory Buildings { get; } = new();

    /// <summary>
    /// Grows whenever a cell's flags may have changed (screen caches compare it; not part of the
    /// state, so it is not saved).
    /// </summary>
    internal long Revision { get; private set; }

    /// <summary>The chunks of an endless map's fog; null for a fixed map's.</summary>
    internal ChunkFog? Chunks => chunks;

    /// <summary>The explored flag of every cell of a fixed map, row by row (empty for an endless map's fog).</summary>
    internal ReadOnlySpan<bool> ExploredCells => explored;

    /// <summary>The visible flag of every cell of a fixed map, row by row (empty for an endless map's fog).</summary>
    internal ReadOnlySpan<bool> VisibleCells => visible;

    /// <summary>Whether the tribe has ever seen <paramref name="cell"/>.</summary>
    public bool IsExplored(GridPoint cell) => chunks?.IsExplored(cell) ?? (size.Contains(cell) && explored[IndexOf(cell)]);

    /// <summary>Whether the tribe sees <paramref name="cell"/> now.</summary>
    public bool IsVisible(GridPoint cell) => chunks?.IsVisible(cell) ?? (size.Contains(cell) && visible[IndexOf(cell)]);

    /// <summary>Whether the tribe sees any cell of <paramref name="area"/> now.</summary>
    public bool AnyVisible(GridRect area)
    {
        if (chunks != null)
        {
            return chunks.AnyVisible(area);
        }
        for (int y = Math.Max(0, area.Top); y < Math.Min(size.Height, area.Bottom); y++)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(size.Width, area.Right); x++)
            {
                if (visible[(y * size.Width) + x])
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The bounding box of the cells explored (empty when none is).</summary>
    public GridRect ExploredBounds
    {
        get
        {
            if (chunks != null)
            {
                return chunks.ExploredBounds();
            }
            GridRect bounds = default;
            for (int i = 0; i < explored.Length; i++)
            {
                if (explored[i])
                {
                    bounds = bounds.Union(new GridRect(i % size.Width, i / size.Width, 1, 1));
                }
            }
            return bounds;
        }
    }

    /// <summary>Marks <paramref name="cell"/> explored or not (cells beyond the fog are left alone).</summary>
    public void SetExplored(GridPoint cell, bool value)
    {
        Revision++;
        if (chunks != null)
        {
            chunks.SetExplored(cell, value);
        }
        else if (size.Contains(cell))
        {
            explored[IndexOf(cell)] = value;
        }
    }

    /// <summary>Marks <paramref name="cell"/> visible now or not (cells beyond the fog are left alone).</summary>
    public void SetVisible(GridPoint cell, bool value)
    {
        Revision++;
        if (chunks != null)
        {
            chunks.SetVisible(cell, value);
        }
        else if (size.Contains(cell))
        {
            visible[IndexOf(cell)] = value;
        }
    }

    /// <summary>Forgets what is visible now.</summary>
    public void ClearVisible()
    {
        Revision++;
        if (chunks != null)
        {
            chunks.ClearVisible();
            return;
        }
        Array.Clear(visible);
    }

    /// <summary>Forgets what was explored.</summary>
    public void ClearExplored()
    {
        Revision++;
        if (chunks != null)
        {
            chunks.ClearExplored();
            return;
        }
        Array.Clear(explored);
    }

    /// <summary>Adds what is visible now to what was explored.</summary>
    public void ExploreVisible()
    {
        Revision++;
        if (chunks != null)
        {
            chunks.ExploreVisible();
            return;
        }
        for (int i = 0; i < visible.Length; i++)
        {
            explored[i] |= visible[i];
        }
    }

    /// <summary>
    /// Adds what <paramref name="other"/> (another tribe's fog of the same map) has explored to
    /// what this fog has explored.
    /// </summary>
    /// <exception cref="ArgumentException">The two fogs cover different maps.</exception>
    internal void AddExplored(FogOfWar other)
    {
        ArgumentNullException.ThrowIfNull(other);
        bool changed;
        if (chunks != null)
        {
            changed = chunks.AddExplored(other.chunks ?? throw new ArgumentException("the fogs cover different maps", nameof(other)));
        }
        else
        {
            changed = other.explored.Length == explored.Length
                ? FogCells.Or(explored, other.explored)
                : throw new ArgumentException("the fogs cover different maps", nameof(other));
        }
        Revision += changed ? 1 : 0;
    }

    /// <summary>Copies what is visible now into <paramref name="copy"/> (see <see cref="AddVisible"/>).</summary>
    internal void CopyVisible(VisibleCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        if (chunks != null)
        {
            chunks.CopyVisible(copy);
        }
        else
        {
            copy.SetCells(visible);
        }
    }

    /// <summary>Makes visible now what <paramref name="copy"/> (of another tribe's fog of the same map) holds as visible.</summary>
    /// <exception cref="ArgumentException">The copy is of a fog of another map.</exception>
    internal void AddVisible(VisibleCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        Revision++;
        if (chunks != null)
        {
            chunks.AddVisible(copy);
        }
        else if (copy.Cells.Length == visible.Length)
        {
            FogCells.Or(visible, copy.Cells);
        }
        else
        {
            throw new ArgumentException("the copy is of another map's fog", nameof(copy));
        }
    }

    private int IndexOf(GridPoint cell) => (cell.Y * size.Width) + cell.X;

    /// <summary>
    /// Saves, loads or hashes the fog: cell by cell, or <paramref name="packed"/> with the two flags
    /// of <see cref="CellsPerValue"/> cells in each value (an endless map's fog by chunks); then
    /// (format 7 on) the buildings remembered.
    /// </summary>
    internal void Exchange(StateExchange s, bool packed)
    {
        Revision += s.IsReading ? 1 : 0;
        ExchangeLayers(s, packed);
        if (s.Format >= StateExchange.MemoryFormat)
        {
            Buildings.Exchange(s);
        }
    }

    private void ExchangeLayers(StateExchange s, bool packed)
    {
        if (chunks != null)
        {
            chunks.Exchange(s);
            return;
        }
        if (!packed)
        {
            for (int i = 0; i < explored.Length; i++)
            {
                explored[i] = s.Flag(explored[i]);
                visible[i] = s.Flag(visible[i]);
            }
            return;
        }
        for (int start = 0; start < explored.Length; start += CellsPerValue)
        {
            int end = Math.Min(start + CellsPerValue, explored.Length);
            int bits = 0;
            for (int i = start; i < end; i++)
            {
                bits |= ((explored[i] ? 1 : 0) | (visible[i] ? 2 : 0)) << (2 * (i - start));
            }
            bits = s.Value(bits);
            for (int i = start; i < end; i++)
            {
                int cell = bits >> (2 * (i - start));
                explored[i] = (cell & 1) != 0;
                visible[i] = (cell & 2) != 0;
            }
        }
    }

    /// <summary>Cells whose flags share one packed value (two bits each, kept below the sign bit).</summary>
    private const int CellsPerValue = 15;
}
