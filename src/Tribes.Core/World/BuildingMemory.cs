using System;
using System.Collections.Generic;
using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>A building as a tribe last saw it.</summary>
/// <param name="Slot">The building's slot.</param>
/// <param name="Tribe">The tribe it belongs to.</param>
/// <param name="X">Column of the footprint's top-left cell.</param>
/// <param name="Y">Row of the footprint's top-left cell.</param>
/// <param name="Kind">Building kind (a site's kind while it is built).</param>
/// <param name="Level">Level.</param>
/// <param name="Aux">What a site becomes (see <see cref="Building.Aux"/>).</param>
/// <param name="WorkMode">The work going on, as <see cref="Building.WorkMode"/>.</param>
/// <param name="HitPoints">Hit points (0 or less: a ruin).</param>
/// <param name="MaxHitPoints">Most hit points.</param>
/// <param name="Frame">The frame it was seen looking like this.</param>
public readonly record struct RememberedBuilding(int Slot, int Tribe, int X, int Y, int Kind, int Level, int Aux, int WorkMode,
    int HitPoints, int MaxHitPoints, int Frame)
{
    /// <summary>How <paramref name="building"/> looks at frame <paramref name="frame"/>.</summary>
    public static RememberedBuilding Of(Building building, int frame)
    {
        ArgumentNullException.ThrowIfNull(building);
        return new RememberedBuilding(building.Id, building.Tribe, building.X, building.Y, building.Kind, building.Level, building.Aux,
            (int)building.WorkMode, building.HitPoints, building.MaxHitPoints, frame);
    }

    /// <summary>Whether <paramref name="other"/> looks the same (it may have been seen at another frame).</summary>
    public bool LooksLike(RememberedBuilding other) => this with { Frame = other.Frame } == other;

    /// <summary>The cells of its footprint (by the kinds of <paramref name="content"/>).</summary>
    public GridRect Footprint(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var kinds = content.Rules.Buildings.Kinds;
        return Kind >= 0 && Kind < kinds.Count ? new GridRect(X, Y, kinds[Kind].Width, kinds[Kind].Height) : new GridRect(X, Y, 1, 1);
    }
}

/// <summary>
/// The buildings a tribe has seen, each as it looked the last time the tribe saw it
/// (in a match the land out of its sight shows them so: a building put up there since is not known,
/// one destroyed since still stands). Kept in slot order; the rules record it, the screen reads it.
/// </summary>
public sealed class BuildingMemory
{
    private readonly SortedDictionary<int, RememberedBuilding> buildings = new();
    private ulong digest;
    private bool stale = true;

    /// <summary>Grows whenever anything remembered changes (screen caches compare it; not saved).</summary>
    public long Revision { get; private set; }

    /// <summary>The buildings remembered, in slot order.</summary>
    public IReadOnlyCollection<RememberedBuilding> Buildings => buildings.Values;

    /// <summary>Number of buildings remembered.</summary>
    public int Count => buildings.Count;

    /// <summary>The building remembered in <paramref name="slot"/>, if any.</summary>
    public bool TryGet(int slot, out RememberedBuilding building) => buildings.TryGetValue(slot, out building);

    /// <summary>Remembers <paramref name="building"/> (a new look replaces the old one; the same look keeps its frame).</summary>
    internal void Remember(RememberedBuilding building)
    {
        if (buildings.TryGetValue(building.Slot, out RememberedBuilding known) && known.LooksLike(building))
        {
            return;
        }
        buildings[building.Slot] = building;
        Changed();
    }

    /// <summary>Forgets the building in <paramref name="slot"/>.</summary>
    internal void Forget(int slot)
    {
        if (buildings.Remove(slot))
        {
            Changed();
        }
    }

    /// <summary>The slots remembered, in order, into <paramref name="slots"/> (cleared first).</summary>
    internal void SlotsInto(List<int> slots)
    {
        slots.Clear();
        slots.AddRange(buildings.Keys);
    }

    /// <summary>
    /// Takes from <paramref name="other"/> (an ally's memory) every building it saw later than this
    /// memory did, or that this memory does not hold.
    /// </summary>
    internal void AddFrom(BuildingMemory other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (RememberedBuilding seen in other.buildings.Values)
        {
            if (!buildings.TryGetValue(seen.Slot, out RememberedBuilding known) || (known.Frame < seen.Frame && !known.LooksLike(seen)))
            {
                buildings[seen.Slot] = seen;
                Changed();
            }
        }
    }

    /// <summary>Saves, loads or hashes the memory (a hash takes a digest made again only after a change).</summary>
    internal void Exchange(StateExchange s)
    {
        if (s is StateHasher hasher)
        {
            hasher.Value64((long)Digest());
            return;
        }
        int count = s.Value(buildings.Count);
        if (s.IsReading)
        {
            buildings.Clear();
            for (int i = 0; i < count; i++)
            {
                RememberedBuilding b = Read(s);
                buildings[b.Slot] = b;
            }
            Changed();
            return;
        }
        foreach (RememberedBuilding b in buildings.Values)
        {
            Write(s, b);
        }
    }

    private static RememberedBuilding Read(StateExchange s) => new(s.Value(0), s.Value(0), s.Value(0), s.Value(0), s.Value(0),
        s.Value(0), s.Value(0), s.Value(0), s.Value(0), s.Value(0), s.Value(0));

    private static void Write(StateExchange s, RememberedBuilding b)
    {
        s.Value(b.Slot);
        s.Value(b.Tribe);
        s.Value(b.X);
        s.Value(b.Y);
        s.Value(b.Kind);
        s.Value(b.Level);
        s.Value(b.Aux);
        s.Value(b.WorkMode);
        s.Value(b.HitPoints);
        s.Value(b.MaxHitPoints);
        s.Value(b.Frame);
    }

    private ulong Digest()
    {
        if (stale)
        {
            var local = new StateHasher();
            local.Value(buildings.Count);
            foreach (RememberedBuilding b in buildings.Values)
            {
                Write(local, b);
            }
            digest = local.Hash;
            stale = false;
        }
        return digest;
    }

    private void Changed()
    {
        Revision++;
        stale = true;
    }
}
