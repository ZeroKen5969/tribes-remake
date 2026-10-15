using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// The work positions of all building kinds, laid out as the original keeps them: each kind's
/// positions after the previous kind's, so a position number beyond a kind's own range reads the
/// next kind's entry; beyond the whole table there is none. Positions while the building works
/// normally and while it is upgraded or repaired are separate tables.
/// </summary>
internal sealed class WorkPositions
{
    private readonly Table normal;
    private readonly Table busy;

    /// <summary>Lays out the positions of <paramref name="kinds"/>.</summary>
    public WorkPositions(IReadOnlyList<BuildingKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        normal = new Table(kinds, o => o.Slots);
        busy = new Table(kinds, o => o.SlotsWhileBusy);
    }

    /// <summary>
    /// Position <paramref name="slot"/> of <paramref name="building"/> as its current work mode
    /// has it (normal, or upgraded or repaired), or null outside the table.
    /// </summary>
    public BuildingSlot? Of(Building building, int slot)
    {
        ArgumentNullException.ThrowIfNull(building);
        return (building.WorkMode == BuildingWorkMode.Normal ? normal : busy).Get(building.Kind, slot);
    }

    /// <summary>Position <paramref name="slot"/> of kind <paramref name="kind"/> while it works normally, or null.</summary>
    public BuildingSlot? Normal(int kind, int slot) => normal.Get(kind, slot);

    /// <summary>Position <paramref name="slot"/> of kind <paramref name="kind"/> while it is upgraded or repaired, or null.</summary>
    public BuildingSlot? Busy(int kind, int slot) => busy.Get(kind, slot);

    private sealed class Table
    {
        private readonly List<BuildingSlot> entries = new();
        private readonly int[] starts;

        public Table(IReadOnlyList<BuildingKind> kinds, Func<BuildingOperation, IReadOnlyList<BuildingSlot>> list)
        {
            starts = new int[kinds.Count];
            for (int k = 0; k < kinds.Count; k++)
            {
                starts[k] = entries.Count;
                if (kinds[k].Operation is { } operation)
                {
                    entries.AddRange(list(operation));
                }
            }
        }

        public BuildingSlot? Get(int kind, int slot)
        {
            int flat = kind >= 0 && kind < starts.Length ? starts[kind] + slot : -1;
            return flat >= 0 && flat < entries.Count ? entries[flat] : null;
        }
    }
}
