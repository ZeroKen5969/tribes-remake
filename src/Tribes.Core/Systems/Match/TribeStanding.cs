using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>Whether a tribe is still in a match, and where it is.</summary>
internal static class TribeStanding
{
    /// <summary>
    /// Whether <paramref name="tribe"/> is out of the match: its player gave up, or none of its
    /// units is a living adult and none of its building slots holds a standing building (a ruin
    /// does not count; a construction site does). Children alone cannot keep a tribe in.
    /// </summary>
    public static bool IsOut(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Tribes[tribe].Controller.Kind == TribeControl.Forfeited)
        {
            return true;
        }
        return LivingAdults(world, tribe) == 0 && FirstStandingBuilding(world, tribe) is null;
    }

    /// <summary>The living adults of <paramref name="tribe"/>.</summary>
    public static int LivingAdults(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        int count = 0;
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>The standing building of <paramref name="tribe"/> in its lowest slot (its chief's hut in a camp), or null.</summary>
    public static Building? FirstStandingBuilding(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (Building building in world.BuildingsOf(tribe))
        {
            if (building.HitPoints > 0)
            {
                return building;
            }
        }
        return null;
    }

    /// <summary>
    /// Where <paramref name="tribe"/> is: the centre of its first standing building, else the cell
    /// of its first living adult, else its camp.
    /// </summary>
    public static GridPoint Seat(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (FirstStandingBuilding(world, tribe) is { } building)
        {
            BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
            return new GridPoint(building.X + (kind.Width / 2), building.Y + (kind.Height / 2));
        }
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult)
            {
                return new GridPoint(unit.X, unit.Y);
            }
        }
        return world.Tribes[tribe].Camp;
    }
}
