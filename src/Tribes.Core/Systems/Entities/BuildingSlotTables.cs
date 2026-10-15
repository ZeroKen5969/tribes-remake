using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// The work positions of all building kinds as the original lays them out: one table per column,
/// the kinds' positions one after the other. A unit's position is looked up from its kind's first
/// entry, so a position number outside the kind's own range lands on a neighbouring kind's entry,
/// as in the original; outside the whole table there is no entry.
/// </summary>
internal sealed class BuildingSlotTables
{
    private readonly Column<BuildingSlot> slots;
    private readonly Column<BuildingSlot> busySlots;
    private readonly Column<int> busyPlaces;
    private readonly Column<int> busyPlaceSlots;

    /// <summary>Lays out the tables of <paramref name="kinds"/>.</summary>
    public BuildingSlotTables(IReadOnlyList<BuildingKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        slots = new Column<BuildingSlot>(kinds, k => k.Operation?.Slots);
        busySlots = new Column<BuildingSlot>(kinds, k => k.Operation?.SlotsWhileBusy);
        busyPlaces = new Column<int>(kinds, k => k.Operation?.SlotPlacesWhileBusy);
        busyPlaceSlots = new Column<int>(kinds, k => k.Operation?.PlaceSlotsWhileBusy);
    }

    /// <summary>The animation played when leaving position <paramref name="slot"/> of <paramref name="kind"/>, or null.</summary>
    public int? ExitAnimation(int kind, int slot) =>
        slots.TryGet(kind, slot, out BuildingSlot? entry) ? entry!.ExitAnimation : null;

    /// <summary>The same while the building is upgraded or repaired, or null.</summary>
    public int? BusyExitAnimation(int kind, int slot) =>
        busySlots.TryGet(kind, slot, out BuildingSlot? entry) ? entry!.ExitAnimation : null;

    /// <summary>The place a work position takes while the building is busy, or null.</summary>
    public int? PlaceOfBusySlot(int kind, int slot) =>
        busyPlaces.TryGet(kind, slot, out int place) ? place : null;

    /// <summary>The work position of a place while the building is busy, or null.</summary>
    public int? BusySlotOfPlace(int kind, int place) =>
        busyPlaceSlots.TryGet(kind, place, out int slot) ? slot : null;

    private sealed class Column<T>
    {
        private readonly List<T> entries = new();
        private readonly int[] starts;

        public Column(IReadOnlyList<BuildingKind> kinds, Func<BuildingKind, IReadOnlyList<T>?> list)
        {
            starts = new int[kinds.Count];
            for (int k = 0; k < kinds.Count; k++)
            {
                starts[k] = entries.Count;
                if (list(kinds[k]) is { } own)
                {
                    entries.AddRange(own);
                }
            }
        }

        public bool TryGet(int kind, int index, out T value)
        {
            int flat = kind >= 0 && kind < starts.Length ? starts[kind] + index : -1;
            bool found = flat >= 0 && flat < entries.Count;
            value = found ? entries[flat] : default!;
            return found;
        }
    }
}
