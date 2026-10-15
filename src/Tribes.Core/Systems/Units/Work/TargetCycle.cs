using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// Where a unit goes next: a route to its goal (or another spot to work at when there is none),
/// the next target when one is done, and the fallbacks of each kind of work (the task site, the
/// store, the spell or trap it was given).
/// </summary>
internal sealed partial class TargetCycle
{
    /// <summary>Order modes <see cref="UnitOrders.FinishOrder"/> gets from this cycle.</summary>
    private const int KeptOrder = OrderModes.Keep, SentOrder = OrderModes.Walk, CoupleOrder = OrderModes.Forced;

    /// <summary>A unit looking for more work searches a square of this size around a random nearby cell.</summary>
    private const int WanderMask = 0xFF, WanderSpan = 6, WanderOffset = 3;

    /// <summary>The run bonus of units hurrying along their route.</summary>
    private const int Hurry = 3;

    /// <summary>The objective of mission 3 during which the player's units all hurry.</summary>
    private const int RaidMission = 3, RaidObjective = 4;

    /// <summary>Actions with their own meaning here.</summary>
    private const int SleepOutside = 79, WakeUp = 80, WaitForMaterial = 84, Resume = 85, Rest = 83;
    private const int GatherWood = 11, GatherStones = 14, DeliverProduct = 26, SetTrap = 27, RestAtHome = 65;
    private const int Courting = 109, Wooing = 110, FirstSpell = 94, LastSpell = 97, MarkEnemy = 97;

    /// <summary>The store, where units take what they carry when they have nothing else to do.</summary>
    private const int Store = 6;

    /// <summary>The hut: a unit inside at work position 0 hands it on when it leaves.</summary>
    private const int Hut = BuildingKindNumbers.Hut;

    /// <summary>The target index of a unit that will look for a target near its destination.</summary>
    private const int LookForTarget = -6;

    /// <summary>A trap kind is the armoury's current product + this.</summary>
    private const int TrapKindOffset = 14;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly UnitActions unitActions;
    private readonly UnitOrders orders;
    private readonly RouteFinder routes;
    private readonly TargetDistances distances;
    private readonly NearbySearches searches;
    private readonly NearestBuildings buildings;

    /// <summary>Runs the target cycle of <paramref name="world"/>'s units.</summary>
    public TargetCycle(GameWorld world, UnitActions unitActions, UnitOrders orders, RouteFinder routes,
        TargetDistances distances, NearbySearches searches, NearestBuildings buildings)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.unitActions = unitActions ?? throw new ArgumentNullException(nameof(unitActions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.searches = searches ?? throw new ArgumentNullException(nameof(searches));
        this.buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        actions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// Plans the route of the turn's unit to its destination. Without a route and out of reach the
    /// unit counts a blocked try and, unless it is walled in, gives up: idle units stay where they
    /// are, some go on to their next target, and workers look for the same work nearby. A unit on its
    /// way to fight (or everyone, while the army is called out) hurries.
    /// </summary>
    public void Approach(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        RouteResult route = routes.RequestRoute(unit);
        if (unit.PathLength < 1 && distances.FromUnit(unit, At(unit)).Cells > routes.ActionRange(unit))
        {
            unit.PathBlock++;
            if (!route.Surrounded)
            {
                GiveUpRoute(turn);
            }
        }
        else if (unit.PathLength > 0 && Hurries(unit))
        {
            unit.RunBonus = Hurry;
        }
    }

    /// <summary>
    /// The turn's unit has finished with its target: one inside a building settles there; a child,
    /// a hunter, a resting unit or a gatherer of trees and rocks with a load looks for the same work
    /// near its destination; a courting couple heads for its hut; otherwise the unit's kind of work
    /// decides (<see cref="FallBack"/>).
    /// </summary>
    public void NextTarget(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            HandOnHutPosition(unit);
            orders.FinishOrder(unit, SentOrder);
        }
        else if (unit.Action is ActionNumbers.ChildIdle or ActionNumbers.HuntingTask or Rest
            || (unit.Action is GatherWood or GatherStones && unit.CarriedAmount > 0))
        {
            LookAgain(turn);
        }
        else if (unit.Action == Courting)
        {
            GoToHut(turn);
            orders.FinishOrder(unit, CoupleOrder);
        }
        else if (unit.Action == Wooing)
        {
            turn.ClearThreat();
            if (!searches.SendToGround(unit, At(unit), (unit.SightRadius * 2) + 1))
            {
                unitActions.SetAction(unit, Courting);
                GoToHut(turn);
            }
            orders.FinishOrder(unit, CoupleOrder);
        }
        else
        {
            FallBack(turn);
        }
    }

    private void GiveUpRoute(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        unit.Status &= ~UnitStatus.RallyPending;
        ActionDefinition action = actions[unit.Action];
        if (action.BuildingInteraction == BuildingInteraction.WorkInside && EntitySlots.Holds(world, EntityProperty.BuildingKind, unit.TargetIndex))
        {
            orders.PickEntrance(unit, world.Building(unit.TargetIndex).Kind, unit.TargetIndex);
        }
        if (unit.Action is ActionNumbers.Idle or SleepOutside or ActionNumbers.StayInside or ActionNumbers.LeaveBuilding)
        {
            unit.PathBlock = 0;
            if (unit.AssignedTask == ActionNumbers.Idle && unit.TaskSiteX == unit.DestinationX && unit.TaskSiteY == unit.DestinationY)
            {
                unit.TaskSiteX = unit.X;
                unit.TaskSiteY = unit.Y;
            }
            StayHere(unit);
        }
        else if (unit.Action is ActionNumbers.ChildIdle or Courting or Wooing or SetTrap)
        {
            NextTarget(turn);
        }
        else if (action.BuildingInteraction != BuildingInteraction.WorkInside && unit.Action > ActionNumbers.LastBasicAttack)
        {
            SearchNearby(turn);
        }
    }

    private bool Hurries(Unit unit) =>
        unit.Action <= ActionNumbers.LastBasicAttack || unit.Action is ActionNumbers.StayInside or ActionNumbers.LeaveBuilding
        || (TribeRoles.IsPlayer(world, unit.Tribe) && TribeRoles.Army(world, unit.Tribe).AllOut)
        || (TribeRoles.IsComputer(world, unit.Tribe) && TribeRoles.Ai(world, unit.Tribe).Intruder > 0)
        || (world.Mission == RaidMission && world.Script.ObjectiveStage == RaidObjective);

    /// <summary>
    /// A unit leaving a hut from work position 0 gives it to the next occupant (who had it before).
    /// </summary>
    private void HandOnHutPosition(Unit unit)
    {
        if (!EntitySlots.Holds(world, EntityProperty.BuildingKind, unit.TargetIndex) || unit.BuildingSlot != 0)
        {
            return;
        }
        Building hut = world.Building(unit.TargetIndex);
        if (hut.Kind != Hut)
        {
            return;
        }
        int next = hut.FirstOccupant == unit.Id ? unit.NextOccupantOrCellObject : hut.FirstOccupant;
        if (next > 0)
        {
            Unit other = world.Unit(next);
            unit.BuildingSlot = other.BuildingSlot;
            other.BuildingSlot = 0;
        }
    }

    /// <summary>A child, a hunter or a gatherer looks again near where it is (a child taken by an enemy follows it).</summary>
    private void LookAgain(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        unit.ActionSubState = 0;
        if (unit.Action == ActionNumbers.ChildIdle && IsEnemyUnit(unit.TargetIndex, unit.Tribe))
        {
            Unit captor = world.Unit(unit.TargetIndex);
            if (captor.Action == ActionNumbers.CaptureChild)
            {
                unit.DestinationX = captor.X;
                unit.DestinationY = captor.Y;
            }
            else
            {
                unit.TargetIndex = 0;
            }
            return;
        }
        if (unit.Action is ActionNumbers.ChildIdle or ActionNumbers.HuntingTask or Rest)
        {
            unit.TargetCellCode = 0;
        }
        SearchNearby(turn);
    }

    /// <summary>Looks for more of what the unit is after around a random cell near it.</summary>
    private void SearchNearby(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        int x = world.Map.ClampColumn(unit.X - WanderOffset + world.Random.NextMasked(WanderMask, WanderSpan));
        int y = world.Map.ClampRow(unit.Y - WanderOffset + world.Random.NextMasked(WanderMask, WanderSpan));
        turn.ClearThreat();
        searches.SendToMoreWork(unit, new GridPoint(x, y), (unit.SightRadius * 2) + 1);
    }

    /// <summary>A courting unit goes to its couple's place by the hut (a unit in no couple has nowhere to go).</summary>
    private void GoToHut(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        int slot = CoupleLinks.CoupleSlot(unit);
        if (slot < 0)
        {
            return;
        }
        turn.ClearThreat();
        searches.PlaceCouple(unit, world.Couples[slot]);
    }

    private bool IsEnemyUnit(int slot, int tribe) =>
        slot >= 1 && slot <= world.UnitSlots && TribeRoles.AreEnemies(world, world.TribeOfUnit(slot), tribe);

    private static void StayHere(Unit unit)
    {
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
    }

    private static GridPoint At(Unit unit) => new(unit.X, unit.Y);
}
