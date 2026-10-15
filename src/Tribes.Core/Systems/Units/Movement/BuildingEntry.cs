using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// A unit reaching the entrance of the building it is to work in: with room inside it turns to
/// the door and steps in; a full building sends a unit on a free errand to the next building of the
/// kind, and stops other workers (trainees wait).
/// </summary>
internal sealed class BuildingEntry
{
    /// <summary><see cref="Unit.EntranceFacingOrOrder"/> when no entrance was picked.</summary>
    public const int NoEntranceFacing = -100;

    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickBits = 0x0F;

    /// <summary>A visit to the witch doctor, who must be at work with nobody else inside.</summary>
    private const int VisitAction = 51;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly Arrival arrival;
    private readonly NearestBuildings nearest;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public BuildingEntry(GameWorld world, UnitActions actions, UnitOrders orders, Arrival arrival, NearestBuildings nearest)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.arrival = arrival ?? throw new ArgumentNullException(nameof(arrival));
        this.nearest = nearest ?? throw new ArgumentNullException(nameof(nearest));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// The end of an action step of <paramref name="unit"/> while it may be standing at an
    /// entrance: steps in or gives up as described above. Returns whether the step is used up
    /// (it was at the entrance, or it is walking).
    /// </summary>
    public bool ReachEntrance(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        bool atEntrance = actions.Definition(unit.Action).BuildingInteraction == BuildingInteraction.WorkInside
            && unit.EntranceFacingOrOrder > 0 && unit.AnimationNumber > AnimationNumbers.LastWalking
            && unit.DestinationX == unit.X && unit.DestinationY == unit.Y
            && unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots;
        if (!atEntrance)
        {
            return unit.AnimationNumber <= AnimationNumbers.LastWalking;
        }
        Building building = world.Building(unit.TargetIndex);
        if (HasRoom(unit, building))
        {
            StepIn(unit, building);
            return true;
        }
        int category = actions.Definition(unit.Action).Category;
        if (category == ActionCategoryNumbers.Free && unit.Action != ActionNumbers.Idle && building.Kind != BuildingKindNumbers.Tower)
        {
            if (nearest.HeadFor(unit, building.Kind))
            {
                orders.FinishOrder(unit, FinishModes.Kept);
            }
        }
        else if (category != ActionCategoryNumbers.Free && (category < WorkCategories.FirstTraining || category > WorkCategories.LastTraining))
        {
            actions.Stop(unit);
        }
        return true;
    }

    /// <summary>
    /// Whether <paramref name="building"/> has room for <paramref name="unit"/>: it takes its work
    /// (otherwise the unit stops), and fewer are inside or on their way than it has places, the
    /// operator's place kept for an operator who is away. A visitor of the witch doctor needs her
    /// at work and alone.
    /// </summary>
    public bool HasRoom(Unit unit, Building building)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(building);
        if (!arrival.AcceptsWork(unit, building))
        {
            actions.Stop(unit);
            return false;
        }
        BuildingKind kind = kinds[building.Kind];
        bool normal = building.WorkMode == BuildingWorkMode.Normal;
        int room = !normal ? kind.WorkerSlotsWhileBusy : building.Level >= 1 ? kind.WorkerSlots[building.Level - 1] : 0;
        Unit? holder = Holder(building);
        if ((kind.ManagerSlot > -1 || !normal) && holder is not null && holder.Id != unit.Id
            && (CoupleLinks.IsOnItsCell(world, holder) || holder.TargetIndex != building.Id))
        {
            room--;
        }
        if (unit.Action == VisitAction)
        {
            return holder is not null && holder.Action == ActionNumbers.WitchDoctor && holder.ActionSubState >= 1
                && building.OccupantCount == 1 && building.Aux == 0;
        }
        return room > building.OccupantCount;
    }

    private static void StepIn(Unit unit, Building building)
    {
        unit.Animation = UnitActions.WalkingAnimation(unit);
        if (building.OperatorOrBeds != unit.Id
            || (building.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal))
        {
            building.OccupantCount++;
        }
        unit.Facing = unit.EntranceFacingOrOrder;
        unit.ActionSubState = SubStates.Entering;
        unit.EntranceFacingOrOrder = NoEntranceFacing;
        unit.StepAndActionTicks &= ~StepTickBits;
        unit.PathBlock = 0;
    }

    /// <summary>The unit the building's operator field names (for a hut, the unit numbered by its beds), or null.</summary>
    private Unit? Holder(Building building) =>
        building.OperatorOrBeds >= 1 && building.OperatorOrBeds <= world.UnitSlots ? world.Unit(building.OperatorOrBeds) : null;
}
