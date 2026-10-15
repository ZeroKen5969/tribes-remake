using System;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// The original's map cursor and target marks while the player has units selected: the cursor
/// turns into the attack cursor over an enemy or an animal in sight, and a small mark on the cell
/// tells what an order there does (fell a tree, break rocks, go into an own building, pick up bones,
/// gather or hunt). Unexplored land shows no mark.
/// </summary>
public static class TargetMarks
{
    /// <summary>The cursor with units selected.</summary>
    public const int SelectedCursor = 33;

    /// <summary>The cursor over an enemy unit or building or an animal in sight.</summary>
    public const int AttackCursor = 34;

    /// <summary>The marks: own building, gather or hunt, tree, rocks, bones.</summary>
    public const int OwnBuildingMark = 41, GatherMark = 43, TreeMark = 44, RocksMark = 45, BonesMark = 46;

    private const int BonesNode = 7, Nursery = 10;

    /// <summary>The mark an order at <paramref name="cell"/> shows to <paramref name="tribe"/>, or null for none.</summary>
    public static int? Mark(GameWorld world, int tribe, ViewerSight sight, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sight);
        if (!world.Map.Contains(cell) || !sight.IsExplored(cell))
        {
            return null;
        }
        MapCell content = world.Map.CellAt(cell);
        switch (content.Content)
        {
            case CellContent.Thicket:
                return TreeMark;
            case CellContent.Rocks:
                return RocksMark;
            case CellContent.Building:
                Building building = world.Building(content.Index);
                return building.InUse && building.Tribe == tribe && building.Kind != Nursery ? OwnBuildingMark : null;
            case CellContent.ResourceNode:
                return world.Map.ResourceNodes[content.Index].Kind == BonesNode ? BonesMark : GatherMark;
            case CellContent.Animal:
                return sight.IsVisible(cell) ? GatherMark : null;
            default:
                return null;
        }
    }

    /// <summary>The cursor with units selected at <paramref name="cell"/>: the attack cursor over an enemy or an animal in sight.</summary>
    public static int Cursor(GameWorld world, int tribe, ViewerSight sight, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sight);
        if (!world.Map.Contains(cell) || !sight.IsVisible(cell))
        {
            return SelectedCursor;
        }
        MapCell content = world.Map.CellAt(cell);
        bool attack = content.Content switch
        {
            CellContent.Animal => true,
            CellContent.Unit => IsSeenEnemyUnit(world, tribe, sight, world.Unit(content.Index)),
            CellContent.Building => world.Building(content.Index) is { InUse: true } b && TribeRoles.AreEnemies(world, tribe, b.Tribe),
            _ => false,
        };
        return attack ? AttackCursor : SelectedCursor;
    }

    private static bool IsSeenEnemyUnit(GameWorld world, int tribe, ViewerSight sight, Unit unit) =>
        unit.HitPoints > 0 && TribeRoles.AreEnemies(world, tribe, unit.Tribe)
        && ((unit.Status & UnitStatus.Hidden) == 0 || sight.Includes(unit.Tribe));
}
