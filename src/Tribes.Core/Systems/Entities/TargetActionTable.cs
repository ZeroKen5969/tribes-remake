using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// The action a unit takes for each cell category and sub-kind, laid out as the original keeps it:
/// one table with the categories one after the other (empty cell, own unit, the building kinds,
/// the node kinds, the terrain resources, animal, other, then the building kinds again for an
/// occupied and for a busy building). A sub-kind past its category's entries reads on into the
/// next category, as in the original; past the whole table the unit idles.
/// </summary>
internal sealed class TargetActionTable
{
    private readonly List<int> actions = new();
    private readonly int[] starts = new int[CellCategories.BusyBuilding + 1];

    /// <summary>Lays out the table from <paramref name="rules"/>.</summary>
    public TargetActionTable(GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        TargetActions targets = rules.Targets.TargetActions;
        IReadOnlyList<BuildingKind> kinds = rules.Buildings.Kinds;
        Add(CellCategories.Empty, new[] { targets.EmptyCell });
        Add(CellCategories.Unit, new[] { targets.OwnUnit });
        Add(CellCategories.Building, Operated(kinds, o => o.UseAction));
        Add(CellCategories.ResourceNode, rules.Targets.GatherActionByNodeKind);
        Add(CellCategories.TerrainResource, targets.TerrainResource);
        Add(CellCategories.Animal, new[] { targets.Animal });
        Add(CellCategories.Other, new[] { targets.Other });
        Add(CellCategories.OccupiedBuilding, Operated(kinds, o => o.UseActionWhenOccupied));
        Add(CellCategories.BusyBuilding, Operated(kinds, o => o.ActionWhileBusy));
    }

    /// <summary>The action for entry <paramref name="index"/> (sub-kind, or terrain resource) of <paramref name="category"/>.</summary>
    public int ActionFor(int category, int index)
    {
        int flat = starts[category] + index;
        return flat >= 0 && flat < actions.Count ? actions[flat] : ActionNumbers.Idle;
    }

    private static List<int> Operated(IReadOnlyList<BuildingKind> kinds, Func<BuildingOperation, int> action)
    {
        var list = new List<int>();
        foreach (BuildingKind kind in kinds)
        {
            // An endless world's own kinds stay out: the original reads past its kinds into the next
            // table, which must not move (units do nothing at those kinds, see UnitOrders).
            if (kind.Operation is { } operation && kind.Id < BuildingKindNumbers.FirstEndlessKind)
            {
                list.Add(action(operation));
            }
        }
        return list;
    }

    private void Add(int category, IReadOnlyList<int> entries)
    {
        starts[category] = actions.Count;
        actions.AddRange(entries);
    }
}
