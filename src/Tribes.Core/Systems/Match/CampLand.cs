using System;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// The land of a camp in a match or an endless world. Its <em>side</em> is the land nearer its camp
/// than any other tribe's. Its computer defends the cells within <see cref="Reach"/> of the camp
/// (a disc) that are nearer its camp than any other camp by at least <see cref="SideMargin"/>
/// cells (<see cref="Defends"/>): so no tribe's camp, and no cell on another tribe's side, is ever
/// in a computer's defended land; one has to walk onto the computer's side. No wild animal is born
/// within <see cref="Reach"/> of a camp. The campaign has no such land.
/// </summary>
internal static class CampLand
{
    /// <summary>A defended cell is at least this many cells nearer its camp than any other tribe's camp.</summary>
    public const int SideMargin = 4;

    /// <summary>Spiral rounds per cell of distance a birth place is looked for beyond a camp's land (a round reaches half a cell farther).</summary>
    private const int OffLandRoundsPerCell = 4;

    /// <summary>How far (cells) the land a camp defends reaches: half the camp spacing.</summary>
    public static int Reach(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return (world.Endless != null ? world.Content.Endless!.Camps.MinimumDistance : CampPlacer.RoomyDistance) / 2;
    }

    /// <summary>
    /// Whether the computer of <paramref name="tribe"/> defends <paramref name="cell"/>: within
    /// <see cref="Reach"/> of its camp and nearer its camp than any other tribe's camp by
    /// <see cref="SideMargin"/> cells. Outside matches every cell counts (the original's alarm area decides).
    /// </summary>
    public static bool Defends(GameWorld world, int tribe, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Mode != WorldMode.Match)
        {
            return true;
        }
        int reach = Reach(world);
        GridPoint own = world.Tribes[tribe].Camp;
        long ownSquared = Squared(own, cell);
        if (ownSquared > (long)reach * reach)
        {
            return false;
        }
        double needed = Math.Sqrt(ownSquared) + SideMargin;
        return CampIndex.For(world).Near(cell, reach + SideMargin, (world, tribe, cell, needed),
            static (s, other) => other == s.tribe || Math.Sqrt(Squared(s.world.Tribes[other].Camp, s.cell)) >= s.needed);
    }

    /// <summary>Whether <paramref name="cell"/> is nearer the camp of <paramref name="tribe"/> than any other tribe's camp.</summary>
    public static bool IsOnOwnSide(GameWorld world, int tribe, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        long own = Squared(world.Tribes[tribe].Camp, cell);
        int reach = (int)Math.Ceiling(Math.Sqrt(own));
        return CampIndex.For(world).Near(cell, reach, (world, tribe, cell, own),
            static (s, other) => other == s.tribe || Squared(s.world.Tribes[other].Camp, s.cell) > s.own);
    }

    /// <summary>
    /// Where an animal of the habitat at <paramref name="home"/> is born: the first empty cell
    /// around it within <paramref name="rounds"/> spiral rounds, or, when that lies near a camp,
    /// the first empty cell away from every camp a little farther out; null when there is none.
    /// </summary>
    public static GridPoint? BirthPlace(GameWorld world, Search.SpiralSearch spiral, GridPoint home, int rounds)
    {
        ArgumentNullException.ThrowIfNull(spiral);
        GridPoint? first = spiral.FindEmptyCell(home, rounds);
        if (first is not { } cell || !IsNearACamp(world, cell))
        {
            return first;
        }
        GridPoint? found = null;
        spiral.Visit(home, rounds + (OffLandRoundsPerCell * Reach(world)), place =>
        {
            if (place.Code != Maps.CellCode.Empty || IsNearACamp(world, place.Cell))
            {
                return false;
            }
            found = place.Cell;
            return true;
        });
        return found;
    }

    /// <summary>Whether <paramref name="cell"/> lies within <see cref="Reach"/> of a tribe's camp.</summary>
    public static bool IsNearACamp(GameWorld world, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Mode != WorldMode.Match)
        {
            return false;
        }
        long reach = Reach(world);
        return !CampIndex.For(world).Near(cell, (int)reach, (world, cell, reach),
            static (s, other) => Squared(s.world.Tribes[other].Camp, s.cell) > s.reach * s.reach);
    }

    /// <summary>The squared distance between two cells.</summary>
    public static long Squared(GridPoint a, GridPoint b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}
