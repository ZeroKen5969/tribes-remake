using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>Answering a call to arms.</summary>
internal sealed partial class UnitDecisions
{
    /// <summary>
    /// Whether <paramref name="unit"/> answers a call: a computer unit called up goes for the
    /// intruder; a player's unit called to the rally point (or, while everyone is called out, any
    /// unit standing at its destination now and then) goes to the rally point, an army member
    /// without a weapon is armed at the store first, and a unit inside a building comes out.
    /// </summary>
    private bool AnswersCall(Unit unit)
    {
        bool called = (unit.Status & UnitStatus.RallyPending) != 0;
        if (TribeRoles.IsComputer(world, unit.Tribe))
        {
            return called && TribeRoles.Ai(world, unit.Tribe).Intruder >= 1 && ChaseIntruder(unit);
        }
        ArmyOrders army = TribeRoles.Army(world, unit.Tribe);
        bool allOutHere = army.AllOut && unit.DestinationX == unit.X && unit.DestinationY == unit.Y
            && (world.Frame & RangeCheckMask) <= unit.MoveDelay;
        if (!called && !allOutHere)
        {
            return false;
        }
        if (CoupleLinks.IsOnItsCell(world, unit))
        {
            return army.AllOut && (unit.Status & UnitStatus.Mustered) != 0 ? ArmUpOrStay(unit) : GoToRallyPoint(unit);
        }
        bool leaves = army.AllOut || unit.Satiety > FedEnough || !InKitchen(unit);
        if (leaves && unit.ActionSubState > 0)
        {
            unit.Action = ActionNumbers.LeaveBuilding;
            unit.OrderMode = OrderModes.Forced;
            return true;
        }
        return false;
    }

    private bool ChaseIntruder(Unit unit)
    {
        if (CoupleLinks.IsOnItsCell(world, unit))
        {
            Unit intruder = world.Unit(TribeRoles.Ai(world, unit.Tribe).Intruder);
            (unit.DestinationX, unit.DestinationY) = (intruder.X, intruder.Y);
            unit.Status &= ~UnitStatus.RallyPending;
            actions.SetAction(unit, ActionNumbers.Idle);
        }
        else
        {
            unit.Action = ActionNumbers.LeaveBuilding;
        }
        unit.OrderMode = OrderModes.Forced;
        return true;
    }

    /// <summary>An army member without a weapon is sent to the store to be armed; one that cannot be leaves the army.</summary>
    private bool ArmUpOrStay(Unit unit)
    {
        unit.CarriedAmount = 0;
        if (unit.Action == ArmUp)
        {
            return false;
        }
        if (services.Buildings.HeadFor(unit, Store) && unit.Weapon == BareHands)
        {
            actions.SetAction(unit, ArmUp);
            unit.OrderMode = OrderModes.Forced;
            unit.RunBonus = Hurry;
            return true;
        }
        unit.Status &= ~UnitStatus.Mustered;
        return false;
    }

    private bool GoToRallyPoint(Unit unit)
    {
        ArmyOrders army = TribeRoles.Army(world, unit.Tribe);
        unit.DestinationX = army.PointX;
        unit.DestinationY = army.PointY;
        unit.Status &= ~UnitStatus.RallyPending;
        if (unit.X == unit.DestinationX && unit.Y == unit.DestinationY)
        {
            return false;
        }
        actions.SetAction(unit, ActionNumbers.Idle);
        unit.OrderMode = OrderModes.Forced;
        unit.RunBonus = Hurry;
        return true;
    }

    private bool InKitchen(Unit unit) =>
        unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots
        && world.Building(unit.TargetIndex).Kind == BuildingKindNumbers.Kitchen;
}
