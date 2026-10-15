using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// The room a camp has to build on its own side: how many standard footprints of
/// <see cref="Footprint"/> × <see cref="Footprint"/> cells fit, a path of a cell left between them,
/// on cells nearer its camp than any other camp and within <see cref="BuildReach"/> of it, where the
/// placement rules let a building stand (in play and empty: no rock, thicket, node, blocking
/// ground or other tribe's building; a unit or an animal only passes, and the tribe's own buildings
/// stand on its room). A camp needs <see cref="Needed"/> of them.
/// </summary>
internal static class CampRoom
{
    /// <summary>The side of a standard footprint (most buildings are 2×2 to 3×3 cells).</summary>
    public const int Footprint = 3;

    /// <summary>Standard footprints every camp has room for on its own side: a chief's hut and a base around it.</summary>
    public const int Needed = 12;

    /// <summary>The cells kept free between two footprints (a path).</summary>
    private const int Path = 1;

    /// <summary>How far (cells) from its camp a tribe's building room is counted: twice the reach of the land it defends.</summary>
    public static int BuildReach(GameWorld world) => 2 * CampLand.Reach(world);

    /// <summary>
    /// The footprints (up to <paramref name="enough"/>) that fit on the side of camp
    /// <paramref name="index"/> of <paramref name="camps"/> (all the camps that count, this one
    /// included); <paramref name="owner"/> is the tribe of that camp (its buildings stand on its
    /// room), or -1 for a camp not settled yet.
    /// </summary>
    public static int Count(GameWorld world, IReadOnlyList<GridPoint> camps, int index, int owner, int enough = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camps);
        int reach = BuildReach(world);
        GridPoint camp = camps[index];
        var rivals = new List<GridPoint>();
        for (int i = 0; i < camps.Count; i++)
        {
            // A camp farther than twice the reach is never nearer to a cell within the reach.
            if (i != index && CampLand.Squared(camps[i], camp) <= 4L * reach * reach)
            {
                rivals.Add(camps[i]);
            }
        }
        var used = new HashSet<GridPoint>();
        int count = 0;
        for (int y = camp.Y - reach; y <= camp.Y + reach - Footprint + 1 && count < enough; y++)
        {
            for (int x = camp.X - reach; x <= camp.X + reach - Footprint + 1 && count < enough; x++)
            {
                if (Fits(world, camp, rivals, reach, owner, used, new GridPoint(x, y)))
                {
                    Take(used, new GridPoint(x, y));
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>The room of every camp of <paramref name="world"/>'s tribes as placed now (up to <paramref name="enough"/> each).</summary>
    public static int[] OfTribes(GameWorld world, int enough = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        var camps = new List<GridPoint>();
        foreach (TribeState tribe in world.Tribes)
        {
            camps.Add(tribe.Camp);
        }
        var room = new int[camps.Count];
        for (int i = 0; i < camps.Count; i++)
        {
            room[i] = Count(world, camps, i, i, enough);
        }
        return room;
    }

    private static bool Fits(GameWorld world, GridPoint camp, List<GridPoint> rivals, int reach, int owner,
        HashSet<GridPoint> used, GridPoint origin)
    {
        for (int dy = -Path; dy < Footprint + Path; dy++)
        {
            for (int dx = -Path; dx < Footprint + Path; dx++)
            {
                var cell = new GridPoint(origin.X + dx, origin.Y + dy);
                bool inside = dx >= 0 && dy >= 0 && dx < Footprint && dy < Footprint;
                if (used.Contains(cell) || (inside && !Buildable(world, camp, rivals, reach, owner, cell)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool Buildable(GameWorld world, GridPoint camp, List<GridPoint> rivals, int reach, int owner, GridPoint cell)
    {
        long own = CampLand.Squared(camp, cell);
        if (own > (long)reach * reach || !world.Map.InPlay(cell))
        {
            return false;
        }
        MapCell content = world.Map.CellAt(cell);
        bool ownBuilding = content.Content == CellContent.Building && owner >= 0 && world.TribeOfBuilding(content.Index) == owner;
        if (!content.IsEmpty && !ownBuilding && content.Content is not (CellContent.Unit or CellContent.Animal))
        {
            return false;
        }
        foreach (GridPoint rival in rivals)
        {
            if (CampLand.Squared(rival, cell) <= own)
            {
                return false;
            }
        }
        return true;
    }

    private static void Take(HashSet<GridPoint> used, GridPoint origin)
    {
        for (int dy = 0; dy < Footprint; dy++)
        {
            for (int dx = 0; dx < Footprint; dx++)
            {
                used.Add(new GridPoint(origin.X + dx, origin.Y + dy));
            }
        }
    }
}
