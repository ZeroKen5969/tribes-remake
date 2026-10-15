using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Commands;

/// <summary>Orders given to units: going somewhere, the store, eating or resting, leaving a building, becoming chief.</summary>
internal sealed class UnitOrderHandler
{
    /// <summary>Units at least this happy obey without a call to arms.</summary>
    private const int WillingHappiness = 10;

    /// <summary>The hint of a unit refusing an order.</summary>
    private const int RefusalHint = 12;

    /// <summary>A running unit's speed class is this much faster.</summary>
    private const int RunningBonus = 3;

    /// <summary>The step ticks in the low bits of a unit's step and action ticks; the action's in the high bits.</summary>
    private const int StepTicksMask = 0x0F, ActionTicksMask = 0xF0;

    /// <summary>The action of changing to weapon w is this plus w.</summary>
    private const int FirstWeaponChangeAction = 44;

    /// <summary>The action of taking the carried goods into a store.</summary>
    private const int DeliverAction = 73;

    private readonly RuleSystems systems;
    private readonly GameWorld world;
    private readonly OrderChecks checks;

    public UnitOrderHandler(RuleSystems systems, OrderChecks checks)
    {
        this.systems = systems ?? throw new ArgumentNullException(nameof(systems));
        this.checks = checks ?? throw new ArgumentNullException(nameof(checks));
        world = systems.World;
    }

    public OrderOutcome Move(MoveUnits order)
    {
        if (!checks.OwnsAll(order.Tribe, order.Units))
        {
            return OrderOutcome.UnknownUnit;
        }
        if (!checks.InPlay(order.Cell))
        {
            return OrderOutcome.OutOfMap;
        }
        int target = Target(order);
        foreach (int id in order.Units)
        {
            Unit unit = world.Unit(id);
            if (unit.HitPoints > 0)
            {
                Obey(unit, order.Cell, target, order.Hurry);
            }
        }
        return OrderOutcome.Applied;
    }

    public OrderOutcome Fetch(FetchWeapon order)
    {
        OrderOutcome outcome = CheckStoreErrand(order.Tribe, order.Units, order.Store, order.Cell);
        if (outcome != OrderOutcome.Applied)
        {
            return outcome;
        }
        if (!OrderMenus.InStock(world, order.Tribe, order.Weapon))
        {
            return OrderOutcome.NotOffered;
        }
        Send(order.Units, order.Cell, FirstWeaponChangeAction + order.Weapon, OrderModes.Forced);
        return OrderOutcome.Applied;
    }

    public OrderOutcome Deliver(DeliverToStore order)
    {
        OrderOutcome outcome = CheckStoreErrand(order.Tribe, order.Units, order.Store, order.Cell);
        if (outcome != OrderOutcome.Applied)
        {
            return outcome;
        }
        bool anyLoad = false;
        foreach (int id in order.Units)
        {
            anyLoad |= world.Unit(id).HitPoints > 0 && world.Unit(id).CarriedAmount > 0;
        }
        if (!anyLoad)
        {
            return OrderOutcome.NotOffered;
        }
        Send(order.Units, order.Cell, DeliverAction, OrderModes.Walk);
        return OrderOutcome.Applied;
    }

    public OrderOutcome TendNeed(TendNeed order)
    {
        Unit? unit = checks.LivingUnit(order.Tribe, order.Unit);
        if (unit == null)
        {
            return OrderOutcome.UnknownUnit;
        }
        int kind = order.Need switch
        {
            UnitNeed.Food => BuildingKindNumbers.Kitchen,
            UnitNeed.Rest => BuildingKindNumbers.Hut,
            _ => -1,
        };
        if (kind < 0)
        {
            return OrderOutcome.NotOffered;
        }
        if (systems.NearestBuildings.HeadFor(unit, kind))
        {
            unit.OrderMode = OrderModes.Walk;
        }
        return OrderOutcome.Applied;
    }

    public OrderOutcome CallOut(CallOutUnit order)
    {
        Unit? unit = checks.LivingUnit(order.Tribe, order.Unit);
        if (unit == null)
        {
            return OrderOutcome.UnknownUnit;
        }
        ComeOut(unit);
        return OrderOutcome.Applied;
    }

    public OrderOutcome AppointChief(AppointChief order)
    {
        if (!checks.HasPlayerState(order.Tribe) || !TribeRoles.AwaitsNewChief(world, order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        Unit? unit = checks.LivingUnit(order.Tribe, order.Unit);
        if (unit == null)
        {
            return OrderOutcome.UnknownUnit;
        }
        if (unit.Age < UnitAges.Adult)
        {
            return OrderOutcome.NotOffered;
        }
        ComeOut(unit);
        world.Tribes[order.Tribe][TribeCounter.Chief] = unit.Id;
        TribeRoles.ChiefNamed(world, order.Tribe);
        systems.Occupancy.FreeHome(unit);
        return OrderOutcome.Applied;
    }

    /// <summary>A live creature aimed at stays the target; otherwise the target is what fills the cell now.</summary>
    private int Target(MoveUnits order)
    {
        int code = order.Target;
        int unit = CellCode.UnitOf(code);
        bool livingUnit = unit >= 1 && unit <= world.UnitSlots && world.Unit(unit).HitPoints > 0;
        int animal = CellCode.AnimalOf(code);
        bool livingAnimal = animal >= 0 && animal < world.AnimalSlots && world.Animal(animal).HitPoints > 0;
        return livingUnit || livingAnimal ? code : CellCode.At(world.Map, order.Cell);
    }

    private void Obey(Unit unit, GridPoint cell, int target, bool hurry)
    {
        ArmyOrders? army = checks.HasPlayerState(unit.Tribe) ? TribeRoles.Army(world, unit.Tribe) : null;
        bool called = army != null && (army.AllOut || (army.Rally && (unit.Status & UnitStatus.Mustered) != 0));
        if (unit.Happiness < WillingHappiness && !called)
        {
            systems.Hints.Offer(RefusalHint, unit.Id, 0);
            return;
        }
        if (IsOwnStore(unit.Tribe, target))
        {
            // The game opens the store's menu instead (see FetchWeapon and DeliverToStore).
            return;
        }
        bool sameCell = unit.DestinationX == cell.X && unit.DestinationY == cell.Y;
        if (hurry || sameCell)
        {
            StartRunning(unit);
        }
        else if (unit.RunBonus > 0)
        {
            unit.StepAndActionTicks = Remapped(unit, start: false);
            unit.RunBonus = 0;
        }
        unit.DestinationX = cell.X;
        unit.DestinationY = cell.Y;
        unit.Status &= ~UnitStatus.RallyPending;
        unit.OrderMode = OrderModes.Walk;
        unit.TargetCellCode = target;
    }

    private void StartRunning(Unit unit)
    {
        if (unit.RunBonus != 0)
        {
            return;
        }
        unit.RunBonus = RunningBonus;
        UnitActions.WalkingAnimation(unit);
        if (unit.RunBonus > 0)
        {
            unit.StepAndActionTicks = Remapped(unit, start: true);
        }
    }

    /// <summary>The unit's ticks with its frame within the step carried over to running or walking pace.</summary>
    private int Remapped(Unit unit, bool start)
    {
        IReadOnlyList<SpeedClass> classes = world.Content.Rules.Movement.SpeedClasses;
        if (unit.MoveDelay < 0 || unit.MoveDelay >= classes.Count)
        {
            return unit.StepAndActionTicks;
        }
        SpeedGait gait = (unit.Facing & 1) == 0 ? classes[unit.MoveDelay].Straight : classes[unit.MoveDelay].Diagonal;
        IReadOnlyList<int> frames = start ? gait.RunStartTicks : gait.RunStopTicks;
        int step = unit.StepAndActionTicks & StepTicksMask;
        return step < frames.Count ? (unit.StepAndActionTicks & ActionTicksMask) + frames[step] : unit.StepAndActionTicks;
    }

    private bool IsOwnStore(int tribe, int target)
    {
        int building = -target;
        return building >= 1 && building <= world.BuildingSlots && world.Building(building).Tribe == tribe
            && world.Building(building).Kind == BuildingKindNumbers.Store;
    }

    private OrderOutcome CheckStoreErrand(int tribe, UnitSet units, int store, GridPoint cell)
    {
        if (!checks.OwnsAll(tribe, units))
        {
            return OrderOutcome.UnknownUnit;
        }
        Building? building = checks.StandingBuilding(tribe, store);
        if (building == null)
        {
            return OrderOutcome.UnknownBuilding;
        }
        if (building.Kind != BuildingKindNumbers.Store)
        {
            return OrderOutcome.WrongBuilding;
        }
        return checks.OnFootprint(building, cell) ? OrderOutcome.Applied : OrderOutcome.OutOfMap;
    }

    private void Send(UnitSet units, GridPoint cell, int action, int mode)
    {
        foreach (int id in units)
        {
            Unit unit = world.Unit(id);
            if (unit.HitPoints > 0)
            {
                unit.DestinationX = cell.X;
                unit.DestinationY = cell.Y;
                unit.Action = action;
                unit.OrderMode = mode;
            }
        }
    }

    /// <summary>A unit hidden inside a building (not on its cell) is sent out of it.</summary>
    private void ComeOut(Unit unit)
    {
        int building = unit.TargetIndex;
        if (!CoupleLinks.IsOnItsCell(world, unit) && building >= 1 && building <= world.BuildingSlots)
        {
            systems.Occupancy.LeaveBuilding(unit, world.Building(building), forced: true, evictOthers: true);
        }
    }
}
