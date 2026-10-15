using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Commands;

/// <summary>The checks every order makes before it changes anything: whose units and buildings, which cells.</summary>
internal sealed class OrderChecks
{
    private readonly GameWorld world;

    public OrderChecks(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Whether <paramref name="tribe"/> holds the state that only the player's tribe has in this
    /// model: army calls, hints, the tutorial, story scenes and the chief's succession.
    /// </summary>
    public bool HasPlayerState(int tribe) => TribeRoles.IsPlayer(world, tribe);

    /// <summary>Whether orders for <paramref name="tribe"/> are taken: not while its player is away or after it gave up.</summary>
    public bool MayGiveOrders(int tribe) =>
        world.Tribes[tribe].Controller.Kind is not (TribeControl.Absent or TribeControl.Forfeited);

    /// <summary>Whether <paramref name="tribe"/> is a tribe of the world.</summary>
    public bool IsTribe(int tribe) => tribe >= 0 && tribe < world.Tribes.Count;

    /// <summary>Whether unit slot <paramref name="unit"/> exists and belongs to <paramref name="tribe"/>.</summary>
    public bool OwnsUnit(int tribe, int unit) =>
        unit >= 1 && unit <= world.UnitSlots && world.Unit(unit).Tribe == tribe;

    /// <summary>The unit in slot <paramref name="unit"/> if it belongs to <paramref name="tribe"/> and lives, else null.</summary>
    public Unit? LivingUnit(int tribe, int unit) =>
        OwnsUnit(tribe, unit) && world.Unit(unit).HitPoints > 0 ? world.Unit(unit) : null;

    /// <summary>Whether every unit of <paramref name="units"/> belongs to <paramref name="tribe"/> (and there is one at least).</summary>
    public bool OwnsAll(int tribe, UnitSet units)
    {
        if (units is null || units.Count == 0)
        {
            return false;
        }
        foreach (int unit in units)
        {
            if (!OwnsUnit(tribe, unit))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The building in slot <paramref name="building"/> if it belongs to <paramref name="tribe"/> and stands, else null.</summary>
    public Building? StandingBuilding(int tribe, int building)
    {
        if (building < 1 || building > world.BuildingSlots)
        {
            return null;
        }
        Building found = world.Building(building);
        return found.Tribe == tribe && found.Stands ? found : null;
    }

    /// <summary>Whether <paramref name="cell"/> is on the playable map.</summary>
    public bool InPlay(GridPoint cell) => world.Map.InPlay(cell);

    /// <summary>Whether <paramref name="cell"/> is a cell of <paramref name="building"/>'s footprint.</summary>
    public bool OnFootprint(Building building, GridPoint cell)
    {
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        return cell.X >= building.X && cell.X < building.X + kind.Width
            && cell.Y >= building.Y && cell.Y < building.Y + kind.Height;
    }
}
