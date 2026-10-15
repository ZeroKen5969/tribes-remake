using System;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Commands;

/// <summary>Orders about buildings: placing, demolishing, upgrades and repairs, occupants, armouries and sanctuaries.</summary>
internal sealed class BuildingOrderHandler
{
    /// <summary>Alert flag asking the upgrade or repair in progress to stop.</summary>
    private const int StopRequested = 0x01;

    /// <summary>A new task order takes the first free slot before this one, else this one.</summary>

    private readonly RuleSystems systems;
    private readonly GameWorld world;
    private readonly OrderChecks checks;

    public BuildingOrderHandler(RuleSystems systems, OrderChecks checks)
    {
        this.systems = systems ?? throw new ArgumentNullException(nameof(systems));
        this.checks = checks ?? throw new ArgumentNullException(nameof(checks));
        world = systems.World;
    }

    public OrderOutcome Place(PlaceBuilding order)
    {
        if (!OrderMenus.BuildableKinds(world).Contains(order.Kind))
        {
            return OrderOutcome.NotOffered;
        }
        if (!checks.InPlay(order.Origin))
        {
            return OrderOutcome.OutOfMap;
        }
        if (Systems.Endless.AllianceBuilding.Is(order.Kind) && Systems.Endless.AllianceBuilding.Planned(world, order.Tribe))
        {
            // One alliance building per tribe.
            return OrderOutcome.NotOffered;
        }
        return systems.OrderConstruction(order.Tribe, order.Kind, order.Origin) != 0 ? OrderOutcome.Applied : OrderOutcome.Blocked;
    }

    public OrderOutcome PlaceTrap(PlaceTrap order)
    {
        OrderOutcome outcome = CheckKind(order.Tribe, order.Armoury, BuildingKindNumbers.Armoury, out Building? armoury);
        if (armoury == null)
        {
            return outcome;
        }
        int product = OrderMenus.FirstTrapProduct + order.TrapKind - BuildingKindNumbers.FirstTrap;
        if (!BuildingKindNumbers.IsTrap(order.TrapKind) || !OrderMenus.ArmouryMakes(armoury.Level).Contains(product))
        {
            return OrderOutcome.NotOffered;
        }
        int queue = QueueOf(order.Tribe, armoury.Id);
        if (queue < 0)
        {
            return OrderOutcome.WrongBuilding;
        }
        if (!checks.InPlay(order.Cell))
        {
            return OrderOutcome.OutOfMap;
        }
        return systems.OrderTrap(order.Tribe, order.TrapKind, order.Cell, armoury.Id, queue) ? OrderOutcome.Applied : OrderOutcome.Blocked;
    }

    public OrderOutcome Demolish(DemolishBuilding order)
    {
        if (checks.StandingBuilding(order.Tribe, order.Building) == null)
        {
            return OrderOutcome.UnknownBuilding;
        }
        systems.Demolish(order.Building);
        return OrderOutcome.Applied;
    }

    public OrderOutcome Work(OrderBuildingWork order)
    {
        Building? building = checks.StandingBuilding(order.Tribe, order.Building);
        if (building == null)
        {
            return OrderOutcome.UnknownBuilding;
        }
        bool planned = building.Kind < BuildingKindNumbers.PlannedKinds;
        switch (order.Work)
        {
            case BuildingWork.Upgrade when building.WorkMode == BuildingWorkMode.Normal && planned
                && building.Level < world.UnlockLevel(building.Kind):
                systems.StartBuildingWork(building.Id, upgrade: true);
                return OrderOutcome.Applied;
            case BuildingWork.Repair when building.WorkMode != BuildingWorkMode.Upgrading && planned
                && building.HitPoints < building.MaxHitPoints:
                systems.StartBuildingWork(building.Id, upgrade: false);
                return OrderOutcome.Applied;
            case BuildingWork.StopUpgrade when building.WorkMode == BuildingWorkMode.Upgrading:
                KeepLevelLimit(building);
                building.AlertFlags |= StopRequested;
                return OrderOutcome.Applied;
            case BuildingWork.StopRepair when building.WorkMode == BuildingWorkMode.Repairing:
                building.AlertFlags |= StopRequested;
                return OrderOutcome.Applied;
            default:
                return OrderOutcome.NotOffered;
        }
    }

    public OrderOutcome Evict(EvictUnit order)
    {
        Building? building = checks.StandingBuilding(order.Tribe, order.Building);
        if (building == null)
        {
            return OrderOutcome.UnknownBuilding;
        }
        if (!checks.OwnsUnit(order.Tribe, order.Unit))
        {
            return OrderOutcome.UnknownUnit;
        }
        Unit unit = world.Unit(order.Unit);
        if (!IsListed(building, unit))
        {
            return OrderOutcome.NotOffered;
        }
        systems.Occupancy.LeaveBuilding(unit, building, forced: true, evictOthers: true);
        return OrderOutcome.Applied;
    }

    public OrderOutcome SetArmouryOrder(SetArmouryOrder order)
    {
        OrderOutcome outcome = CheckKind(order.Tribe, order.Armoury, BuildingKindNumbers.Armoury, out Building? armoury);
        if (armoury == null)
        {
            return outcome;
        }
        if (order.Product < 0 || order.Product >= OrderMenus.FirstTrapProduct
            || !OrderMenus.ArmouryMakes(armoury.Level).Contains(order.Product)
            || order.Count < 0 || order.Count > OrderMenus.MaxArmouryCount)
        {
            return OrderOutcome.NotOffered;
        }
        int queue = QueueOf(order.Tribe, armoury.Id);
        if (queue < 0)
        {
            return OrderOutcome.WrongBuilding;
        }
        world.Tribes[order.Tribe].Armouries[queue].SetPending(order.Product, order.Count);
        return OrderOutcome.Applied;
    }

    public OrderOutcome CastSpell(CastSpell order)
    {
        OrderOutcome outcome = CheckSpell(order.Tribe, order.Sanctuary, order.Spell, onPlace: false);
        if (outcome != OrderOutcome.Applied)
        {
            return outcome;
        }
        Unit? target = checks.LivingUnit(order.Tribe, order.Unit);
        if (target == null)
        {
            return OrderOutcome.UnknownUnit;
        }
        if (target.Age < UnitAges.Adult)
        {
            return OrderOutcome.NotOffered;
        }
        TaskOrder task = FreeTaskOrder(order.Tribe);
        (task.Building, task.Kind, task.A) = (order.Sanctuary, order.Spell, target.Id);
        return OrderOutcome.Applied;
    }

    public OrderOutcome CastSpellAt(CastSpellAt order)
    {
        OrderOutcome outcome = CheckSpell(order.Tribe, order.Sanctuary, order.Spell, onPlace: true);
        if (outcome != OrderOutcome.Applied)
        {
            return outcome;
        }
        if (!checks.InPlay(order.Cell))
        {
            return OrderOutcome.OutOfMap;
        }
        TaskOrder task = FreeTaskOrder(order.Tribe);
        (task.Building, task.Kind, task.A, task.B) = (order.Sanctuary, order.Spell, order.Cell.X, order.Cell.Y);
        return OrderOutcome.Applied;
    }

    private OrderOutcome CheckKind(int tribe, int slot, int kind, out Building? building)
    {
        building = checks.StandingBuilding(tribe, slot);
        if (building == null)
        {
            return OrderOutcome.UnknownBuilding;
        }
        if (building.Kind != kind)
        {
            building = null;
            return OrderOutcome.WrongBuilding;
        }
        return OrderOutcome.Applied;
    }

    private OrderOutcome CheckSpell(int tribe, int slot, int spell, bool onPlace)
    {
        OrderOutcome outcome = CheckKind(tribe, slot, BuildingKindNumbers.Sanctuary, out Building? sanctuary);
        if (sanctuary == null)
        {
            return outcome;
        }
        bool offered = spell >= 0 && spell < OrderMenus.SanctuarySpells(world, sanctuary.Level);
        return offered && (spell == OrderMenus.PlaceSpell) == onPlace ? OrderOutcome.Applied : OrderOutcome.NotOffered;
    }

    /// <summary>The occupant list of a building's menu: its operator (not a hut's beds) and the occupants at work.</summary>
    private bool IsListed(Building building, Unit unit)
    {
        bool hutBeds = building.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal;
        bool isOperator = building.OperatorOrBeds == unit.Id && !hutBeds;
        if (isOperator)
        {
            return true;
        }
        return systems.Occupancy.Occupants(building).Any(o => o.Id == unit.Id && o.ActionSubState > 0);
    }

    /// <summary>A called-off upgrade keeps the current level's hit point limit.</summary>
    private void KeepLevelLimit(Building building)
    {
        var limits = world.Content.Rules.Buildings.Kinds[building.Kind].MaxHitPoints;
        if (building.Level < 1 || building.Level > limits.Count)
        {
            return;
        }
        building.MaxHitPoints = limits[building.Level - 1];
        building.HitPoints = Math.Min(building.HitPoints, building.MaxHitPoints);
    }

    private int QueueOf(int tribe, int armoury)
    {
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

    private TaskOrder FreeTaskOrder(int tribe) => world.Tribes[tribe].FreeTaskOrder(world.GrowsOnDemand);
}
