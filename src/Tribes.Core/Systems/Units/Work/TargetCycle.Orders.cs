using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The fallbacks of each kind of work and the orders armouries and sanctuaries give their workers.</summary>
internal sealed partial class TargetCycle
{
    /// <summary>The armoury worker's action while it waits for an order.</summary>
    private const int ArmouryWorker = 58;

    /// <summary>The animation of a unit settling at a workplace; a witch doctor doing it is not given a spell yet.</summary>
    private const int SettlingAnimation = -109;

    /// <summary>
    /// What the turn's unit does when its target is done, by its kind of work: idle and waiting units
    /// stay; a witch doctor goes to its spell's target (or takes the next spell); an armoury worker
    /// carrying a trap goes to where the trap is to be set; workers who return to their task site
    /// (a rester to its home) do so; anyone else takes what it carries to the nearest store, or
    /// looks for a target near its destination when there is no store.
    /// </summary>
    public void FallBack(UnitTurn turn)
    {
        System.ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (unit.Action is ActionNumbers.Idle or WakeUp or WaitForMaterial or Resume)
        {
            StayHere(unit);
        }
        else if (unit.Action is >= FirstSpell and <= LastSpell)
        {
            GoCast(turn);
        }
        else if (unit.Action == SetTrap)
        {
            GoSetTrap(unit);
        }
        else if ((actions[unit.Action].Traits & ActionTraits.ReturnsToTaskSite) != 0)
        {
            if (unit.Action != RestAtHome)
            {
                unit.DestinationX = unit.TaskSiteX;
                unit.DestinationY = unit.TaskSiteY;
            }
            else if (EntitySlots.Holds(world, EntityProperty.BuildingX, unit.Home))
            {
                unit.DestinationX = world.Building(unit.Home).X;
                unit.DestinationY = world.Building(unit.Home).Y;
            }
            orders.FinishOrder(unit, SentOrder);
        }
        else if (buildings.HeadFor(unit, Store))
        {
            orders.FinishOrder(unit, SentOrder);
        }
        else
        {
            unit.TargetIndex = LookForTarget;
        }
    }

    /// <summary>
    /// The witch doctor of the turn goes to the target of its sanctuary's task order: a unit to
    /// cast on, or a cell to mark; an order whose target is gone (or no order) means the next spell.
    /// </summary>
    private void GoCast(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        TaskOrder? order = OrderOf(unit.Tribe, unit.EntranceFacingOrOrder);
        if (order is null || order.A < 0)
        {
            NextSpell(turn);
        }
        else if (unit.Action != MarkEnemy)
        {
            if (order.A >= 1 && order.A <= world.UnitSlots)
            {
                unit.DestinationX = world.Unit(order.A).X;
                unit.DestinationY = world.Unit(order.A).Y;
            }
            unit.TargetIndex = order.A;
            unit.TargetCellCode = order.A >= 1 && order.A <= world.UnitSlots ? CellCode.OfUnit(order.A) : order.A;
        }
        else
        {
            unit.DestinationX = order.A;
            unit.DestinationY = order.B;
            orders.FinishOrder(unit, SentOrder);
        }
    }

    /// <summary>An armoury worker goes to where the trap it made is to be set.</summary>
    private void GoSetTrap(Unit unit)
    {
        foreach (ArmouryOrders armoury in world.Tribes[unit.Tribe].Armouries)
        {
            if (armoury.Armoury <= 0 || world.Building(armoury.Armoury).OperatorOrBeds != unit.Id)
            {
                continue;
            }
            foreach (TrapOrder trap in world.Traps)
            {
                if (trap.OwnerBuilding == armoury.Armoury && trap.Kind == armoury.Current + TrapKindOffset)
                {
                    unit.DestinationX = trap.X;
                    unit.DestinationY = trap.Y;
                    orders.FinishOrder(unit, SentOrder);
                    return;
                }
            }
            return;
        }
    }

    /// <summary>
    /// A witch doctor is done with its spell: the order is closed, it goes back to waiting in the
    /// sanctuary and looks for its next target, the sanctuaries hand out their next orders, and it
    /// heads for whatever it was given.
    /// </summary>
    public void NextSpell(UnitTurn turn)
    {
        System.ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        CloseSpellOrder(unit);
        unitActions.SetAction(unit, ActionNumbers.WitchDoctor);
        NextTarget(turn);
        HandOutOrders(unit.Tribe);
        turn.GoalDistance = distances.FromUnit(unit, At(unit)).Measured;
        Approach(turn);
    }

    /// <summary>
    /// The workplaces of <paramref name="tribe"/> hand out work: each armoury worker waiting for work
    /// starts the first product its armoury has orders for, and each witch doctor waiting in its
    /// sanctuary takes the sanctuary's first task order (an order whose target is gone is dropped).
    /// </summary>
    public void HandOutOrders(int tribe)
    {
        foreach (ArmouryOrders armoury in world.Tribes[tribe].Armouries)
        {
            if (armoury.Armoury > 0)
            {
                StartProduct(armoury);
            }
        }
        foreach (Building building in world.BuildingsOf(tribe))
        {
            while (building.HitPoints > 0 && building.Kind == BuildingKindNumbers.Sanctuary && building.OperatorOrBeds >= 1
                && GiveSpell(tribe, building))
            {
            }
        }
    }

    private void StartProduct(ArmouryOrders armoury)
    {
        int worker = world.Building(armoury.Armoury).OperatorOrBeds;
        if (worker <= 0 || world.Unit(worker).Action != ArmouryWorker || world.Unit(worker).ActionSubState < 0)
        {
            return;
        }
        IReadOnlyList<ArmouryProduct> products = world.Content.Rules.Armoury.Products;
        for (int product = 0; product < products.Count; product++)
        {
            if (armoury.Pending(product) > 0)
            {
                Unit unit = world.Unit(worker);
                unitActions.SetAction(unit, products[product].CraftAction);
                SendOrHold(unit);
                armoury.Current = product;
                return;
            }
        }
    }

    /// <summary>
    /// The witch doctor of <paramref name="sanctuary"/> takes the sanctuary's first order; returns true
    /// when an order whose target is gone was dropped (the next one is then looked at).
    /// </summary>
    private bool GiveSpell(int tribe, Building sanctuary)
    {
        Unit doctor = world.Unit(sanctuary.OperatorOrBeds);
        if (doctor.Action != ActionNumbers.WitchDoctor || doctor.ActionSubState < 0 || doctor.Animation == SettlingAnimation
            || (sanctuary.OccupantCount != 1 && !CoupleLinks.IsOnItsCell(world, doctor)))
        {
            return false;
        }
        ListView<TaskOrder> taskOrders = world.Tribes[tribe].TaskOrders;
        int index = FirstOrderOf(taskOrders, sanctuary.Id);
        if (index < 0)
        {
            return false;
        }
        TaskOrder order = taskOrders[index];
        if (order.A < 0)
        {
            RemoveOrder(taskOrders, index);
            return true;
        }
        unitActions.SetAction(doctor, world.Content.Rules.Armoury.SpellActions[order.Kind]);
        doctor.EntranceFacingOrOrder = sanctuary.Id;
        doctor.Status &= ~UnitStatus.SpellPending;
        SendOrHold(doctor);
        sanctuary.Aux = 1;
        return false;
    }

    /// <summary>A unit given new work goes for its target, or is first told to come out of the building it is in.</summary>
    private void SendOrHold(Unit unit)
    {
        if (CoupleLinks.IsOnItsCell(world, unit))
        {
            NextTarget(new UnitTurn(unit, distances.FromUnit(unit, At(unit)).Measured));
        }
        else
        {
            unit.OrderMode = OrderModes.Forced;
        }
    }

    /// <summary>Closes the task order of the sanctuary <paramref name="unit"/> runs.</summary>
    public void CloseSpellOrder(Unit unit)
    {
        System.ArgumentNullException.ThrowIfNull(unit);
        foreach (Building building in world.BuildingsOf(unit.Tribe))
        {
            if (building.HitPoints > 0 && building.Kind == BuildingKindNumbers.Sanctuary && building.OperatorOrBeds == unit.Id)
            {
                ListView<TaskOrder> taskOrders = world.Tribes[unit.Tribe].TaskOrders;
                int index = IndexOf(taskOrders, building.Id);
                if (index >= 0)
                {
                    RemoveOrder(taskOrders, index);
                }
                building.Aux = 0;
                return;
            }
        }
    }

    private TaskOrder? OrderOf(int tribe, int building)
    {
        foreach (TaskOrder order in world.Tribes[tribe].TaskOrders)
        {
            if (order.Building == building)
            {
                return order;
            }
        }
        return null;
    }

    /// <summary>The first order of <paramref name="building"/> before the first free slot, or -1.</summary>
    private static int FirstOrderOf(ListView<TaskOrder> taskOrders, int building)
    {
        for (int i = 0; i < taskOrders.Count && taskOrders[i].Building != 0; i++)
        {
            if (taskOrders[i].Building == building)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The first order of <paramref name="building"/> anywhere in the list, or -1.</summary>
    private static int IndexOf(ListView<TaskOrder> taskOrders, int building)
    {
        for (int i = 0; i < taskOrders.Count; i++)
        {
            if (taskOrders[i].Building == building)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Removes order <paramref name="index"/>: the later ones move up and the last slot is freed.</summary>
    private static void RemoveOrder(ListView<TaskOrder> taskOrders, int index)
    {
        for (int i = index; i < taskOrders.Count - 1; i++)
        {
            TaskOrder next = taskOrders[i + 1];
            TaskOrder slot = taskOrders[i];
            (slot.Building, slot.Kind, slot.A, slot.B, slot.Extra) = (next.Building, next.Kind, next.A, next.B, next.Extra);
        }
        taskOrders[taskOrders.Count - 1].Building = 0;
    }
}
