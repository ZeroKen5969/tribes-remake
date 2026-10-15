using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// A unit carrying out an order when it stands on a cell centre: a walking order reaches its
/// destination, a forced action ends, an alarm makes it chase the attacker.
/// </summary>
internal sealed class OrderFollowing
{
    /// <summary>The order mode of a unit chasing an attacker after an alarm.</summary>
    private const int Alarmed = 4;

    /// <summary>Below this happiness the player's units refuse work they are sent to (and say so with hint 12).</summary>
    private const int TooUnhappy = 10, RefusesWorkHint = 12;

    /// <summary>Frames without fighting before units take on a peaceful task by themselves.</summary>
    private const int QuietFrames = 50;

    /// <summary>The "the busy unit is disturbed" frame: none (the animation does not restart).</summary>
    private const int NoRestart = -1;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly TargetDistances distances;
    private readonly HintSystem hints;
    private readonly Interruptions interruptions;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>Lets the units of <paramref name="world"/> follow orders.</summary>
    public OrderFollowing(GameWorld world, UnitActions actions, UnitOrders orders, TargetDistances distances, HintSystem hints,
        Interruptions interruptions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.interruptions = interruptions ?? throw new ArgumentNullException(nameof(interruptions));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// <paramref name="unit"/>, standing on a cell centre under an order, carries it out. A unit sent to
    /// work it does not want (too unhappy) refuses; a busy one may first get up from its work. A unit
    /// chasing an attacker that has come within its sight takes it on, otherwise it leaves the building
    /// it is in. A walking order then ends as the unit's own choice, a forced one as an order.
    /// </summary>
    public void Follow(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.OrderMode < OrderModes.Walk || !unit.IsCentred)
        {
            return;
        }
        bool walking = unit.OrderMode == OrderModes.Walk;
        int category = 0;
        bool byOrder = false;
        if (walking)
        {
            OrderChoice choice = orders.PickAction(unit, new GridPoint(unit.DestinationX, unit.DestinationY), true);
            category = definitions[choice.Action].Category;
            byOrder = choice.RefusalHint < 0 && category != definitions[unit.AssignedTask].Category
                && (category != 0 || (!TribeRoles.Army(world, unit.Tribe).AllOut && TribeRoles.FramesSinceCombat(world, unit.Tribe) >= QuietFrames));
        }
        if (!TribeRoles.IsComputer(world, unit.Tribe) && unit.Happiness < TooUnhappy && walking && category != 0)
        {
            Refuse(unit);
            return;
        }
        if (interruptions.HoldsOn(unit, NoRestart, byOrder))
        {
            return;
        }
        if (unit.OrderMode == Alarmed)
        {
            ChaseAttacker(unit);
        }
        unit.PathLength = 0;
        if (unit.OrderMode > OrderModes.Free)
        {
            orders.FinishOrder(unit, unit.OrderMode == OrderModes.Walk ? 0 : OrderModes.Walk);
        }
    }

    private void Refuse(Unit unit)
    {
        unit.OrderMode = OrderModes.Free;
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        unit.TargetIndex = 0;
        unit.TargetCellCode = 0;
        actions.Stop(unit);
        hints.Offer(RefusesWorkHint, unit.Id, 0);
    }

    /// <summary>
    /// An alarmed unit heads for the attacker it was told about: in sight, it takes it on; out of
    /// sight it leaves the building it is in to look.
    /// </summary>
    private void ChaseAttacker(Unit unit)
    {
        bool animal = CellCode.AnimalOf(unit.TargetCellCode) >= 0;
        unit.TargetIndex = CellCode.SlotOf(unit.TargetCellCode, 1, animal);
        EntityProperty position = animal ? EntityProperty.AnimalX : EntityProperty.UnitX;
        unit.TargetCellCode = (int)position;
        unit.DestinationX = EntitySlots.Read(world, position, unit.TargetIndex);
        unit.DestinationY = EntitySlots.Read(world, position + 1, unit.TargetIndex);
        if (distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Cells <= unit.SightRadius)
        {
            orders.FinishOrder(unit, OrderModes.Keep);
            unit.OrderMode = OrderModes.Free;
        }
        else
        {
            actions.SetAction(unit, ActionNumbers.LeaveBuilding);
        }
    }
}
