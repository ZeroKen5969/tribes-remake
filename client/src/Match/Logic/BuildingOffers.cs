using System;
using System.Collections.Generic;
using Tribes.Core.Commands;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>A product line of an armoury's menu: a weapon to make, or a trap to set.</summary>
/// <param name="Product">The product (0..5 weapons, 6.. traps).</param>
/// <param name="Pending">Items still to make (weapons) or traps ordered and not made yet.</param>
public readonly record struct ArmouryLine(int Product, int Pending)
{
    /// <summary>Whether the product is a trap (set at a cell) rather than a weapon (made to stock).</summary>
    public bool IsTrap => Product >= OrderMenus.FirstTrapProduct;

    /// <summary>The trap's building kind (20 net, 21 spring, 22 pit) for a trap product.</summary>
    public int TrapKind => RuleNumbers.FirstTrap + Product - OrderMenus.FirstTrapProduct;
}

/// <summary>
/// What a building's panel offers right now, worked out the way the order handlers check it, so a
/// button is only enabled when its order would be carried out.
/// </summary>
public static class BuildingOffers
{
    /// <summary>The work orders <paramref name="building"/> takes now (upgrade, repair, or stopping one).</summary>
    public static IReadOnlyList<BuildingWork> Work(GameWorld world, Building building)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(building);
        var offers = new List<BuildingWork>(2);
        if (!building.Stands)
        {
            return offers;
        }
        bool planned = RuleNumbers.IsPlanned(building.Kind);
        if (building.WorkMode == BuildingWorkMode.Normal && planned && building.Level < world.UnlockLevel(building.Kind))
        {
            offers.Add(BuildingWork.Upgrade);
        }
        if (building.WorkMode != BuildingWorkMode.Upgrading && building.WorkMode != BuildingWorkMode.Repairing
            && planned && building.HitPoints < building.MaxHitPoints)
        {
            offers.Add(BuildingWork.Repair);
        }
        if (building.WorkMode == BuildingWorkMode.Upgrading)
        {
            offers.Add(BuildingWork.StopUpgrade);
        }
        if (building.WorkMode == BuildingWorkMode.Repairing)
        {
            offers.Add(BuildingWork.StopRepair);
        }
        return offers;
    }

    /// <summary>Whether <paramref name="tribe"/> may tear <paramref name="building"/> down (a site: call the construction off).</summary>
    public static bool CanDemolish(int tribe, Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        return building.Tribe == tribe && building.Stands;
    }

    /// <summary>The units inside <paramref name="building"/> that can be sent out (its operator first).</summary>
    public static IReadOnlyList<int> Evictable(GameWorld world, Building building)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(building);
        var units = new List<int>();
        bool hutBeds = building.Kind == RuleNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal;
        int operatorUnit = building.OperatorOrBeds;
        if (!hutBeds && operatorUnit >= 1 && operatorUnit <= world.UnitSlots && world.Unit(operatorUnit).HitPoints > 0)
        {
            units.Add(operatorUnit);
        }
        foreach (int id in Inside(world, building))
        {
            if (world.Unit(id).ActionSubState > 0 && !units.Contains(id))
            {
                units.Add(id);
            }
        }
        return units;
    }

    /// <summary>Every unit on <paramref name="building"/>'s occupant list.</summary>
    public static IReadOnlyList<int> Inside(GameWorld world, Building building)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(building);
        var units = new List<int>();
        int guard = world.UnitSlots;
        for (int id = building.FirstOccupant; id > 0 && id <= world.UnitSlots && guard-- > 0; id = world.Unit(id).NextOccupantOrCellObject)
        {
            units.Add(id);
        }
        return units;
    }

    /// <summary>The order queue of armoury <paramref name="armoury"/> in its tribe's list, or -1.</summary>
    public static int ArmouryQueue(GameWorld world, int tribe, int armoury)
    {
        ArgumentNullException.ThrowIfNull(world);
        ListView<ArmouryOrders> queues = world.Tribes[tribe].Armouries;
        for (int i = 0; i < queues.Count; i++)
        {
            if (queues[i].Armoury == armoury)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The product lines of an armoury at its level, with what is still to make.</summary>
    public static IReadOnlyList<ArmouryLine> Armoury(GameWorld world, Building armoury)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(armoury);
        var lines = new List<ArmouryLine>();
        if (armoury.Kind != RuleNumbers.Armoury || !armoury.Stands)
        {
            return lines;
        }
        int queue = ArmouryQueue(world, armoury.Tribe, armoury.Id);
        foreach (int product in OrderMenus.ArmouryMakes(armoury.Level))
        {
            int pending = queue >= 0 ? world.Tribes[armoury.Tribe].Armouries[queue].Pending(product) : 0;
            lines.Add(new ArmouryLine(product, pending));
        }
        return lines;
    }

    /// <summary>The spells (0, 1, ...) <paramref name="sanctuary"/> casts at its level.</summary>
    public static IReadOnlyList<int> Spells(GameWorld world, Building sanctuary)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sanctuary);
        var spells = new List<int>();
        if (sanctuary.Kind != RuleNumbers.Sanctuary || !sanctuary.Stands)
        {
            return spells;
        }
        int count = OrderMenus.SanctuarySpells(world, sanctuary.Level);
        for (int spell = 0; spell < count; spell++)
        {
            spells.Add(spell);
        }
        return spells;
    }

    /// <summary>The weapons (0 club ... 5 tomahawk) a store of <paramref name="tribe"/> can hand out now.</summary>
    public static IReadOnlyList<int> WeaponsInStock(GameWorld world, int tribe)
    {
        var weapons = new List<int>();
        for (int weapon = 0; weapon < RuleNumbers.StoreWeapons; weapon++)
        {
            if (OrderMenus.InStock(world, tribe, weapon))
            {
                weapons.Add(weapon);
            }
        }
        return weapons;
    }

    /// <summary>Whether any of <paramref name="units"/> carries goods (a store takes them).</summary>
    public static bool AnyCarrying(GameWorld world, IEnumerable<int> units)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(units);
        foreach (int id in units)
        {
            if (id >= 1 && id <= world.UnitSlots && world.Unit(id).HitPoints > 0 && world.Unit(id).CarriedAmount > 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The tribe's adults who could be named chief (when the tribe waits for a new one).</summary>
    public static IReadOnlyList<int> ChiefCandidates(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        var units = new List<int>();
        foreach (int id in LivingUnits(world, tribe))
        {
            if (world.Unit(id).Age >= RuleNumbers.AdultAge)
            {
                units.Add(id);
            }
        }
        return units;
    }

    /// <summary>The living units of <paramref name="tribe"/>, by slot.</summary>
    public static IReadOnlyList<int> LivingUnits(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        var units = new List<int>();
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            if (unit.HitPoints > 0 && unit.Tribe == tribe)
            {
                units.Add(id);
            }
        }
        return units;
    }

    /// <summary>The standing buildings of <paramref name="tribe"/>, by slot.</summary>
    public static IReadOnlyList<int> StandingBuildings(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        var buildings = new List<int>();
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            Building building = world.Building(id);
            if (building.Stands && building.Tribe == tribe)
            {
                buildings.Add(id);
            }
        }
        return buildings;
    }

    /// <summary>
    /// The building <paramref name="unit"/> is inside (it is not on its map cell and its target
    /// is a standing building of its tribe), or 0.
    /// </summary>
    public static int BuildingInside(GameWorld world, Unit unit)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(unit);
        var cell = new Tribes.Core.Content.GridPoint(unit.X, unit.Y);
        if (unit.HitPoints <= 0 || (world.Map.Contains(cell) && world.Map.CellAt(cell) == Tribes.Core.Maps.MapCell.UnitAt(unit.Id)))
        {
            return 0;
        }
        int building = unit.TargetIndex;
        return building >= 1 && building <= world.BuildingSlots && world.Building(building).Stands
            && world.Building(building).Tribe == unit.Tribe ? building : 0;
    }
}
