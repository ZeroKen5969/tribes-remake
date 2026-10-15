using System;
using System.Collections.Generic;

namespace Tribes.Core.World;

/// <summary>An entry of one of the world's shared tables, saved, loaded and hashed in table order.</summary>
internal interface ITableEntry
{
    /// <summary>Saves, loads or hashes the entry's values (see <see cref="StateExchange"/>).</summary>
    void Exchange(StateExchange s);
}

/// <summary>
/// A dust cloud over a building. When it settles the change it covers is finished: a demolished
/// building's slot is freed, or a completed building is shown.
/// </summary>
public sealed class DustCloud : ITableEntry
{
    private int frames, kind;

    /// <summary>The building under the cloud; 0 when the slot is free.</summary>
    public int Building { get; set; }

    /// <summary>Frames the cloud has lasted.</summary>
    public int Frames { get => frames; set => frames = Wrap.Signed8(value); }

    /// <summary>What it covers: a demolition or a completion.</summary>
    public int Kind { get => kind; set => kind = Wrap.Signed8(value); }

    void ITableEntry.Exchange(StateExchange s)
    {
        Building = s.Value(Building);
        Frames = s.Value(Frames);
        Kind = s.Value(Kind);
    }
}

/// <summary>A trap ordered from an armoury and waiting to be set, or set.</summary>
public sealed class TrapOrder : ITableEntry
{
    private int kind;

    /// <summary>The building that ordered it; 0 when the slot is free.</summary>
    public int OwnerBuilding { get; set; }

    /// <summary>Trap kind (a building kind number of the trap range).</summary>
    public int Kind { get => kind; set => kind = Wrap.Signed8(value); }

    /// <summary>Cell column.</summary>
    public int X { get; set; }

    /// <summary>Cell row.</summary>
    public int Y { get; set; }

    void ITableEntry.Exchange(StateExchange s)
    {
        OwnerBuilding = s.Value(OwnerBuilding);
        Kind = s.Value(Kind);
        X = s.Value(X);
        Y = s.Value(Y);
    }
}

/// <summary>A construction site waiting for material deliveries.</summary>
public sealed class ConstructionJob : ITableEntry
{
    private int trips;

    /// <summary>The site building; 0 when the slot is free.</summary>
    public int Building { get; set; }

    /// <summary>Full loads of material delivered so far.</summary>
    public int Trips { get => trips; set => trips = Wrap.Signed8(value); }

    void ITableEntry.Exchange(StateExchange s)
    {
        Building = s.Value(Building);
        Trips = s.Value(Trips);
    }
}

/// <summary>An upgrade or repair in progress.</summary>
public sealed class UpgradeJob : ITableEntry
{
    private int done, needed;

    /// <summary>The building; 0 when the slot is free.</summary>
    public int Building { get; set; }

    /// <summary>Work done.</summary>
    public int Done { get => done; set => done = Wrap.Signed8(value); }

    /// <summary>Work needed (the original stores it negated until work starts).</summary>
    public int Needed { get => needed; set => needed = Wrap.Signed8(value); }

    void ITableEntry.Exchange(StateExchange s)
    {
        Building = s.Value(Building);
        Done = s.Value(Done);
        Needed = s.Value(Needed);
    }
}

/// <summary>A couple on its way to a hut to have a child.</summary>
public sealed class Couple : ITableEntry
{
    private int state;

    /// <summary>The man's unit slot; 0 when the slot is free.</summary>
    public int Man { get; set; }

    /// <summary>The woman's unit slot.</summary>
    public int Woman { get; set; }

    /// <summary>Hut entrance column.</summary>
    public int EntranceX { get; set; }

    /// <summary>Hut entrance row.</summary>
    public int EntranceY { get; set; }

    /// <summary>Progress (0 free, 1 on the way, ...).</summary>
    public int State { get => state; set => state = Wrap.Signed8(value); }

    /// <summary>The hut building slot.</summary>
    public int Hut { get; set; }

    void ITableEntry.Exchange(StateExchange s)
    {
        Man = s.Value(Man);
        Woman = s.Value(Woman);
        EntranceX = s.Value(EntranceX);
        EntranceY = s.Value(EntranceY);
        State = s.Value(State);
        Hut = s.Value(Hut);
    }
}

/// <summary>The unit fields a council scene changes, saved to put them back when it ends.</summary>
public sealed class MeetingSeat
{
    /// <summary>Fields of a unit the scene overwrites.</summary>
    public const int SavedFieldCount = 7;

    private readonly int[] saved = new int[SavedFieldCount];

    /// <summary>The seated unit; 0 when the seat is free.</summary>
    public int Unit { get; set; }

    /// <summary>Saved value <paramref name="i"/> (in the order the content table lists the fields).</summary>
    public int Saved(int i) => saved[i];

    /// <summary>Saves value <paramref name="i"/>.</summary>
    public void Save(int i, int value) => saved[i] = Wrap.Signed8(value);

    internal void Exchange(StateExchange s)
    {
        Unit = s.Value(Unit);
        for (int i = 0; i < SavedFieldCount; i++)
        {
            saved[i] = s.Value(saved[i]);
        }
    }
}

/// <summary>
/// A coarse spatial index: the map is split into 16x16-cell sectors and, per category of object,
/// each sector heads a chain of slots. It is rebuilt every few frames, so between rebuilds it can
/// be out of date — and the rules see it out of date, as in the original. Categories are the
/// object groups: the units and the buildings of each tribe and the animals (see
/// <see cref="CategoryCount"/>); a tribe added to the world adds its two categories at the end.
/// </summary>
public sealed class SectorIndex
{
    /// <summary>Object categories indexed in a world of two tribes (the original's).</summary>
    public const int Categories = 5;

    /// <summary>Sectors along each axis.</summary>
    public const int SectorsPerSide = 6;

    /// <summary>Entries per category to start with (a tribe's chain grows when it has more slots).</summary>
    public const int SlotsPerCategory = 50;

    /// <summary>A coordinate shifted right by this gives its sector.</summary>
    private const int SectorShift = 4;

    /// <summary>Cells along each side of a sector.</summary>
    public const int SectorSize = 16;

    private const int HeadsPerCategory = SectorsPerSide * SectorsPerSide;

    /// <summary>The head of a sector without slots.</summary>
    private const int EmptyChain = -1;


    private readonly List<int[]> heads = new();
    private readonly List<int[]> next = new();
    private readonly ChunkSectors? chunks;

    /// <summary>An index of the original's <see cref="Categories"/> categories.</summary>
    public SectorIndex()
        : this(Categories)
    {
    }

    internal SectorIndex(int categories) => AddCategories(categories);

    private SectorIndex(ChunkSectors chunks) => this.chunks = chunks;

    /// <summary>
    /// An index over the chunks of the endless map <paramref name="map"/>: sectors have no fixed
    /// number (sector coordinates may be negative) and every chain starts empty.
    /// </summary>
    internal static SectorIndex ForChunks(Maps.WorldMap map) => new(new ChunkSectors(map));

    /// <summary>Whether the index covers an endless map (sectors without a fixed number).</summary>
    public bool IsChunked => chunks != null;

    /// <summary>Object categories indexed.</summary>
    public int CategoryCount => next.Count;

    /// <summary>The sector of a cell coordinate: within 0..<see cref="SectorsPerSide"/> - 1 on a fixed map, unbounded on an endless one.</summary>
    public int SectorOf(int coordinate)
    {
        int sector = coordinate >> SectorShift;
        if (chunks != null)
        {
            return sector;
        }
        return sector < 0 ? 0 : sector < SectorsPerSide - 1 ? sector : SectorsPerSide - 1;
    }

    /// <summary>First slot in a sector's chain.</summary>
    public int Head(int category, int sectorX, int sectorY) =>
        chunks?.Head(category, sectorX, sectorY) ?? heads[category][(sectorX * SectorsPerSide) + sectorY];

    /// <summary>Sets the first slot of a sector's chain.</summary>
    public void SetHead(int category, int sectorX, int sectorY, int slot)
    {
        if (chunks != null)
        {
            chunks.SetHead(category, sectorX, sectorY, slot, CategoryCount);
            return;
        }
        heads[category][(sectorX * SectorsPerSide) + sectorY] = slot;
    }

    /// <summary>The slot after <paramref name="slot"/> in its chain.</summary>
    public int Next(int category, int slot)
    {
        int[] chain = next[category];
        return slot < chain.Length ? chain[slot] : EmptyChain;
    }

    /// <summary>Sets the slot after <paramref name="slot"/> (the chain grows as needed).</summary>
    public void SetNext(int category, int slot, int value)
    {
        if (slot >= next[category].Length)
        {
            int[] grown = new int[Math.Max(slot + 1, 2 * next[category].Length)];
            next[category].CopyTo(grown, 0);
            next[category] = grown;
        }
        next[category][slot] = value;
    }

    /// <summary>Empties every chain (on an endless map every head reads as empty).</summary>
    public void Clear()
    {
        chunks?.Clear();
        foreach (int[] chain in heads)
        {
            Array.Clear(chain);
        }
        foreach (int[] chain in next)
        {
            Array.Clear(chain);
        }
    }

    /// <summary>
    /// Whether a search of <paramref name="category"/> over the sectors from (<paramref name="minX"/>,
    /// <paramref name="minY"/>) to (<paramref name="maxX"/>, <paramref name="maxY"/>) can find
    /// anything (always on a fixed map; on an endless map false when none of those sectors heads a chain).
    /// </summary>
    internal bool MayHold(int category, int minX, int maxX, int minY, int maxY) =>
        chunks == null || chunks.MayHold(category, minX, maxX, minY, maxY);

    /// <summary>Sets every head to the empty chain (the start of a rebuild on an endless map).</summary>
    internal void EmptyHeads() => chunks?.Clear();

    /// <summary>
    /// Adds <paramref name="count"/> categories at the end. The original's categories start zeroed,
    /// as there (the first rebuild empties them); further ones start empty, since they can be
    /// searched before the next rebuild.
    /// </summary>
    internal void AddCategories(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (chunks == null)
            {
                int[] head = new int[HeadsPerCategory];
                if (heads.Count >= Categories)
                {
                    Array.Fill(head, EmptyChain);
                }
                heads.Add(head);
            }
            next.Add(new int[SlotsPerCategory]);
        }
    }


    internal void Exchange(StateExchange s)
    {
        if (chunks != null)
        {
            chunks.Exchange(s, CategoryCount);
            ExchangeGrownChains(s);
            return;
        }
        for (int k = 0; k < heads.Count; k++)
        {
            int[] head = heads[k];
            for (int i = 0; i < HeadsPerCategory; i++)
            {
                head[i] = s.Value(head[i]);
            }
            if (s.Format < StateExchange.SlotsFormat)
            {
                int[] chain = next[k];
                for (int slot = 0; slot < SlotsPerCategory; slot++)
                {
                    chain[slot] = s.Value(chain[slot]);
                }
            }
        }
        if (s.Format >= StateExchange.SlotsFormat)
        {
            ExchangeGrownChains(s);
        }
    }

    private void ExchangeGrownChains(StateExchange s)
    {
        for (int k = 0; k < next.Count; k++)
        {
            int length = s.Value(next[k].Length);
            if (s.IsReading)
            {
                if (length < SlotsPerCategory || length > MaxChainLength)
                {
                    throw new System.IO.InvalidDataException($"sector chain of {length} slots");
                }
                next[k] = new int[length];
            }
            int[] chain = next[k];
            for (int slot = 0; slot < chain.Length; slot++)
            {
                chain[slot] = s.Value(chain[slot]);
            }
        }
    }

    /// <summary>Longest chain a saved endless index may have (far more slots than any category holds).</summary>
    private const int MaxChainLength = 1 << 20;
}
