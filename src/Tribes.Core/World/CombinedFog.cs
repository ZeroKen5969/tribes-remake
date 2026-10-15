using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// The fog of several tribes seen as one (what a spectator of their players is shown): a cell is
/// explored when any of them has explored it and visible when any of them sees it now. Only for
/// the screen: it reads the tribes' fog and keeps a combined copy, made again only where their fog
/// changed since (a fixed map's whole grid; an endless map's chunks one by one when first read).
/// One tribe's fog is read as it is.
/// </summary>
public sealed class CombinedFog : IFogView
{
    private readonly GameWorld world;
    private readonly int[] tribes;
    private readonly FogOfWar[] sources;
    private readonly long[] seenRevisions;
    private bool[] explored = Array.Empty<bool>();
    private bool[] visible = Array.Empty<bool>();
    private GridSize size;
    private readonly List<ulong[]?> chunkLayers = new();
    private readonly List<long> chunkStamps = new();
    private readonly List<long> chunkEpochs = new();
    private long epoch = 1;
    private long boundsEpoch;
    private GridRect bounds;
    private BuildingMemory? buildings;
    private long buildingsStamp = -1;

    /// <summary>The fog of <paramref name="tribes"/> of <paramref name="world"/> seen as one (repeats are dropped).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A tribe is not one of the world's.</exception>
    public CombinedFog(GameWorld world, IEnumerable<int> tribes)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(tribes);
        this.tribes = tribes.Distinct().Order().ToArray();
        foreach (int tribe in this.tribes)
        {
            if (tribe < 0 || tribe >= world.Tribes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(tribes), tribe, $"the world has tribes 0..{world.Tribes.Count - 1}");
            }
        }
        sources = this.tribes.Select(world.Fog).ToArray();
        seenRevisions = new long[sources.Length];
        Array.Fill(seenRevisions, -1);
    }

    /// <summary>The tribes whose fog is combined, in tribe order.</summary>
    public IReadOnlyList<int> Tribes => tribes;

    /// <summary>Whether <paramref name="tribe"/>'s fog is part of this one.</summary>
    public bool Includes(int tribe) => Array.BinarySearch(tribes, tribe) >= 0;

    /// <inheritdoc/>
    public bool IsExplored(GridPoint cell) => Read(cell, visibleLayer: false);

    /// <inheritdoc/>
    public bool IsVisible(GridPoint cell) => Read(cell, visibleLayer: true);

    /// <inheritdoc/>
    public bool AnyVisible(GridRect area)
    {
        foreach (FogOfWar fog in sources)
        {
            if (fog.AnyVisible(area))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The buildings any of the tribes remembers, each as the one that saw it last remembers it
    /// (made again when a tribe's memory changed).
    /// </summary>
    public BuildingMemory Buildings
    {
        get
        {
            if (sources.Length == 1)
            {
                return sources[0].Buildings;
            }
            long stamp = 0;
            foreach (FogOfWar fog in sources)
            {
                stamp += fog.Buildings.Revision;
            }
            if (buildings == null || stamp != buildingsStamp)
            {
                buildings = new BuildingMemory();
                foreach (FogOfWar fog in sources)
                {
                    buildings.AddFrom(fog.Buildings);
                }
                buildingsStamp = stamp;
            }
            return buildings;
        }
    }

    /// <inheritdoc/>
    public GridRect ExploredBounds
    {
        get
        {
            if (sources.Length == 1)
            {
                return sources[0].ExploredBounds;
            }
            CatchUp();
            if (boundsEpoch != epoch)
            {
                bounds = default;
                foreach (FogOfWar fog in sources)
                {
                    bounds = bounds.Union(fog.ExploredBounds);
                }
                boundsEpoch = epoch;
            }
            return bounds;
        }
    }

    private bool Read(GridPoint cell, bool visibleLayer)
    {
        if (sources.Length <= 1)
        {
            return sources.Length == 1 && (visibleLayer ? sources[0].IsVisible(cell) : sources[0].IsExplored(cell));
        }
        CatchUp();
        if (!world.Map.IsChunked)
        {
            return size.Contains(cell) && (visibleLayer ? visible : explored)[(cell.Y * size.Width) + cell.X];
        }
        MapChunk? chunk = world.Map.ChunkAtCell(cell);
        if (chunk == null || ChunkLayers(chunk.Ordinal) is not { } words)
        {
            return false;
        }
        int index = MapChunk.LocalIndex(cell.X, cell.Y) + (visibleLayer ? ChunkFog.VisibleLayer * 64 : 0);
        return (words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    /// <summary>
    /// Notices whether any tribe's fog changed since the last look: a fixed map's grid is combined
    /// again at once, an endless map's chunks each when next read.
    /// </summary>
    private void CatchUp()
    {
        bool changed = false;
        for (int i = 0; i < sources.Length; i++)
        {
            long revision = sources[i].Revision;
            if (revision != seenRevisions[i])
            {
                seenRevisions[i] = revision;
                changed = true;
            }
        }
        if (!changed)
        {
            return;
        }
        epoch++;
        if (!world.Map.IsChunked)
        {
            CombineGrid();
        }
    }

    private void CombineGrid()
    {
        size = sources[0].Size;
        int cells = sources[0].ExploredCells.Length;
        if (explored.Length != cells)
        {
            explored = new bool[cells];
            visible = new bool[cells];
        }
        sources[0].ExploredCells.CopyTo(explored);
        sources[0].VisibleCells.CopyTo(visible);
        for (int i = 1; i < sources.Length; i++)
        {
            FogCells.Or(explored, sources[i].ExploredCells);
            FogCells.Or(visible, sources[i].VisibleCells);
        }
    }

    /// <summary>The combined layers of the chunk at <paramref name="ordinal"/>, made again when a tribe's changed; null when nobody saw any of it.</summary>
    private ulong[]? ChunkLayers(int ordinal)
    {
        while (chunkLayers.Count <= ordinal)
        {
            chunkLayers.Add(null);
            chunkStamps.Add(0);
            chunkEpochs.Add(0);
        }
        if (chunkEpochs[ordinal] == epoch)
        {
            return chunkLayers[ordinal];
        }
        chunkEpochs[ordinal] = epoch;
        long stamp = 0;
        foreach (FogOfWar fog in sources)
        {
            stamp += fog.Chunks!.ChunkRevision(ordinal);
        }
        if (stamp == chunkStamps[ordinal])
        {
            return chunkLayers[ordinal];
        }
        chunkStamps[ordinal] = stamp;
        ulong[]? words = chunkLayers[ordinal];
        if (words != null)
        {
            Array.Clear(words);
        }
        foreach (FogOfWar fog in sources)
        {
            ReadOnlySpan<ulong> layers = fog.Chunks!.Layers(ordinal);
            if (layers.IsEmpty)
            {
                continue;
            }
            words ??= new ulong[layers.Length];
            for (int w = 0; w < layers.Length; w++)
            {
                words[w] |= layers[w];
            }
        }
        chunkLayers[ordinal] = words;
        return words;
    }
}
