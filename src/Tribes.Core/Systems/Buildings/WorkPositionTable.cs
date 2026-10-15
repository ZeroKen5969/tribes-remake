using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Systems.Buildings;

/// <summary>
/// The work positions of all building kinds laid out as in the original: one list, the kinds'
/// positions one after the other. A position is looked up from its kind's first entry, so a
/// position outside the kind's own range reads a neighbouring kind's entry, as the original does;
/// outside the whole list there is no entry.
/// </summary>
internal sealed class WorkPositionTable
{
    private readonly List<BuildingSlot> entries = new();
    private readonly int[] starts;

    /// <summary>Lays out the positions <paramref name="positions"/> gives for each of <paramref name="kinds"/>.</summary>
    public WorkPositionTable(IReadOnlyList<BuildingKind> kinds, Func<BuildingOperation, IReadOnlyList<BuildingSlot>> positions)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(positions);
        starts = new int[kinds.Count];
        for (int k = 0; k < kinds.Count; k++)
        {
            starts[k] = entries.Count;
            if (kinds[k].Operation is { } operation)
            {
                entries.AddRange(positions(operation));
            }
        }
    }

    /// <summary>Position <paramref name="position"/> of <paramref name="kind"/>, or null outside the table.</summary>
    public BuildingSlot? At(int kind, int position)
    {
        int flat = kind >= 0 && kind < starts.Length ? starts[kind] + position : -1;
        return flat >= 0 && flat < entries.Count ? entries[flat] : null;
    }
}
