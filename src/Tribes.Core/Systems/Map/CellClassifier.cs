using System;
using System.Collections.Generic;
using System.IO;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>What a tribe sees in a cell, as orders and attacks classify it.</summary>
/// <param name="Category">The cell category (<see cref="CellCategories"/>).</param>
/// <param name="SubKind">The content's sub-kind: building kind, node kind, otherwise 0.</param>
/// <param name="Slot">The entity slot (or terrain resource index) the cell refers to.</param>
/// <param name="OtherSide">Whether the content belongs to an enemy of the viewing tribe.</param>
internal readonly record struct CellProbe(int Category, int SubKind, int Slot, bool OtherSide);

/// <summary>
/// Classifies cell codes into the categories the order and attack rules work with. The original's
/// codes go by the content's code ranges; codes of the entities beyond them by what they stand for.
/// A unit or building of an ally of the viewing tribe reads as something in the way (category
/// "other"): it is neither the tribe's own nor an enemy.
/// </summary>
internal sealed class CellClassifier
{
    /// <summary>The viewing tribe of a look that sees through every fog and belongs to no side.</summary>
    public const int NoViewer = -1;

    /// <summary>A side divisor of this or more marks contents of no side.</summary>
    private const int NoSide = 127;

    private readonly GameWorld world;
    private readonly IReadOnlyList<CellCategory> categories;

    /// <summary>Classifies cells of <paramref name="world"/>.</summary>
    public CellClassifier(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        categories = world.Content.Rules.Targets.CellCategories;
    }

    /// <summary>What <paramref name="viewingTribe"/> sees in <paramref name="cell"/>.</summary>
    public CellProbe ProbeCell(GridPoint cell, int viewingTribe) =>
        Probe(CellCode.At(world.Map, cell), cell, viewingTribe);

    /// <summary>
    /// What <paramref name="viewingTribe"/> sees of the cell code <paramref name="unit"/> was sent to,
    /// standing at <paramref name="cell"/> (whose fog decides what can be seen).
    /// </summary>
    public CellProbe ProbeTarget(Unit unit, GridPoint cell, int viewingTribe)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return Probe(unit.TargetCellCode, cell, viewingTribe);
    }

    /// <summary>
    /// The category of <paramref name="code"/> at <paramref name="cell"/> for
    /// <paramref name="viewingTribe"/>: a human-ruled tribe sees nothing in cells it has not
    /// explored and no unit in cells it does not see now; a destroyed unit or building reads as a
    /// blocked cell.
    /// </summary>
    public CellProbe Probe(int code, GridPoint cell, int viewingTribe)
    {
        bool player = TribeRoles.IsPlayer(world, viewingTribe);
        FogOfWar? fog = player ? world.Fog(viewingTribe) : null;
        CellProbe probe;
        if (fog is not null && !fog.IsExplored(cell))
        {
            probe = Describe(CellCode.Empty, CellCategories.Empty, viewingTribe);
        }
        else
        {
            int category = CategoryOf(code);
            probe = fog is not null && category == CellCategories.Unit && !fog.IsVisible(cell)
                ? Describe(CellCode.Empty, CellCategories.Empty, viewingTribe)
                : Describe(code, category, viewingTribe);
        }
        if ((probe.Category == CellCategories.Unit || probe.Category == CellCategories.Building) && IsDestroyed(probe))
        {
            probe = Describe(CellCode.Impassable, CellCategories.Other, viewingTribe);
        }
        return probe;
    }

    private int CategoryOf(int code)
    {
        if (code < sbyte.MinValue || code > sbyte.MaxValue)
        {
            return CategoryOfFurther(code);
        }
        for (int i = 0; i < categories.Count; i++)
        {
            CellCategory category = categories[i];
            if (code >= category.CodeMin && code <= category.CodeMax)
            {
                return category.Id;
            }
        }
        throw new InvalidDataException($"no cell category holds code {code}");
    }

    /// <summary>The category of a code beyond the original's range, by what it stands for.</summary>
    private static int CategoryOfFurther(int code) =>
        CellCode.UnitOf(code) > 0 ? CellCategories.Unit
        : CellCode.BuildingOf(code) > 0 ? CellCategories.Building
        : CellCode.NodeOf(code) >= 0 ? CellCategories.ResourceNode
        : CellCode.AnimalOf(code) >= 0 ? CellCategories.Animal
        : CellCategories.Other;

    private CellProbe Describe(int code, int categoryId, int viewingTribe)
    {
        CellCategory category = categories[categoryId];
        int slot = SlotOf(code, categoryId, category);
        if (category.SideDivisor >= NoSide)
        {
            return new CellProbe(categoryId, EntityProperties.Get(world, category.SubKindProperty, slot), slot, false);
        }
        int owner = categoryId == CellCategories.Unit ? world.TribeOfUnit(slot) : world.TribeOfBuilding(slot);
        if (owner != viewingTribe && world.Relations.AreAllies(owner, viewingTribe))
        {
            return Describe(code, CellCategories.Other, viewingTribe);
        }
        int subKind = EntityProperties.Get(world, category.SubKindProperty, slot);
        return new CellProbe(categoryId, subKind, slot, TribeRoles.AreEnemies(world, owner, viewingTribe));
    }

    /// <summary>The slot a code of a category stands for (code * multiplier + offset, kept in a byte, for the original's codes).</summary>
    private static int SlotOf(int code, int categoryId, CellCategory category)
    {
        if (code is >= sbyte.MinValue and <= sbyte.MaxValue || categoryId == CellCategories.Other)
        {
            return Wrap.Signed8((code * category.IndexMultiplier) + category.IndexOffset);
        }
        return categoryId switch
        {
            CellCategories.Unit => CellCode.UnitOf(code),
            CellCategories.Building => CellCode.BuildingOf(code),
            CellCategories.ResourceNode => CellCode.NodeOf(code),
            _ => CellCode.AnimalOf(code),
        };
    }

    private bool IsDestroyed(CellProbe probe) =>
        categories[probe.Category].HitPointsProperty is { } hitPoints
        && EntityProperties.Get(world, hitPoints, probe.Slot) <= 0;
}
