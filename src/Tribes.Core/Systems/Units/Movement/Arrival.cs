using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// A unit that walked through a building's entrance arrives inside: if the building still takes
/// its work it gets a work position, joins the occupants and starts the position's action (an
/// operator also takes the building's task and site); otherwise it stays outside and stops.
/// </summary>
internal sealed class Arrival
{
    /// <summary>A unit inside a building throws this much farther (a tower guard's height).</summary>
    public const int InsideThrowBonus = 2;

    /// <summary>Building flag: someone sick is inside.</summary>
    private const int SickOccupantFlag = 0x02;

    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickBits = 0x0F;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly Occupancy occupancy;
    private readonly TargetActionTable targetActions;
    private readonly WorkPositions positions;
    private readonly Action<GameEvent> raise;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public Arrival(GameWorld world, UnitActions actions, UnitOrders orders, Occupancy occupancy,
        TargetActionTable targetActions, WorkPositions positions, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.targetActions = targetActions ?? throw new ArgumentNullException(nameof(targetActions));
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// Whether <paramref name="building"/> takes <paramref name="unit"/>'s work: it stands (a site
    /// still needs building) and what the unit would do there is of its current action's category.
    /// Like any check of an order's target it makes the building's cell the unit's target cell.
    /// </summary>
    public bool AcceptsWork(Unit unit, Building building)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(building);
        if (BuildingKindNumbers.IsSite(building.Kind) && building.HitPoints >= kinds[building.Aux].MaxHitPoints[0])
        {
            return false;
        }
        if (building.HitPoints <= 0)
        {
            return false;
        }
        OrderChoice choice = orders.PickAction(unit, new GridPoint(building.X, building.Y), retarget: true);
        return Category(choice.Action) == Category(unit.Action);
    }

    /// <summary>
    /// <paramref name="unit"/>, stepping through the entrance of the building it targets, reaches
    /// the middle of its step: it goes in, or stays outside and stops.
    /// </summary>
    public void Arrive(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit.OffsetX = 0;
        unit.OffsetY = 0;
        unit.AnimationTick = 0;
        unit.StepAndActionTicks &= ~StepTickBits;
        Building? building = TargetBuilding(unit);
        if (building is not null && unit.OrderMode <= OrderModes.Keep
            && actions.Definition(unit.Action).BuildingInteraction == BuildingInteraction.WorkInside
            && AcceptsWork(unit, building))
        {
            GoIn(unit, building);
            return;
        }
        StayOutside(unit, building);
    }

    private void GoIn(Unit unit, Building building)
    {
        raise(new UnitDeselected(unit.Id));
        unit.OrderMode = OrderModes.Free;
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        unit.ActionSubState = SubStates.AtTheDoor;
        unit.ThrowRange += InsideThrowBonus;
        occupancy.PickWorkPosition(unit);
        unit.NextOccupantOrCellObject = building.FirstOccupant;
        building.FirstOccupant = unit.Id;
        occupancy.UpdateRange(building);
        bool normal = building.WorkMode == BuildingWorkMode.Normal;
        BuildingSlot? position = positions.Of(building, unit.BuildingSlot);
        if ((actions.Definition(unit.Action).Traits & ActionTraits.KeptOnEnteringBuilding) == 0)
        {
            actions.SetAction(unit, position!.IdleAction);
        }
        if (RunsTheBuilding(unit, building))
        {
            int use = targetActions.ActionFor(CellCategories.Building, building.Kind);
            if (world.Content.Rules.Actions.Categories[Category(use)].ServiceGroup == 0 && normal)
            {
                unit.AssignedTask = use;
            }
            unit.TaskSiteX = building.X;
            unit.TaskSiteY = building.Y;
        }
        if ((unit.Status & UnitStatus.Diseased) != 0 && (!normal || building.Kind != BuildingKindNumbers.Sanctuary))
        {
            building.Flags |= SickOccupantFlag;
        }
        unit.Animation = position!.EnterAnimation;
    }

    /// <summary>
    /// Whether the unit takes the building's task: it holds the operator's position, or the
    /// building needs no operator (huts and the chief's hut aside), or it is a site or busy.
    /// </summary>
    private bool RunsTheBuilding(Unit unit, Building building)
    {
        int manager = kinds[building.Kind].ManagerSlot;
        return unit.BuildingSlot == manager
            || (manager < 0 && building.Kind != BuildingKindNumbers.Hut && building.Kind != BuildingKindNumbers.ChiefsHut)
            || building.Kind >= BuildingKindNumbers.FirstSite
            || building.WorkMode != BuildingWorkMode.Normal;
    }

    private void StayOutside(Unit unit, Building? building)
    {
        if (unit.OrderMode == OrderModes.Keep)
        {
            unit.OrderMode = OrderModes.Free;
        }
        if (building is not null && (building.OperatorOrBeds != unit.Id
            || (building.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal)))
        {
            building.OccupantCount--;
        }
        unit.ActionSubState = SubStates.Normal;
        world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.UnitAt(unit.Id));
        UnitActions.Stand(unit);
        actions.Stop(unit);
    }

    private Building? TargetBuilding(Unit unit) =>
        unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots ? world.Building(unit.TargetIndex) : null;

    private int Category(int action) => actions.Definition(action).Category;
}
