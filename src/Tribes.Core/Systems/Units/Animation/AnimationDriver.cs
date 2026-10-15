using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Animation;

/// <summary>
/// The animation part of a unit's turn: a thrower waits for its projectile to land, others play a
/// frame of their animation; when the animation ends its effect follows (getting up from hiding,
/// answering the chief's call, the damage of a trap), a visitor leaves its building, a unit at an
/// entrance steps in, and the action step is over. Then the unit's work goes on.
/// </summary>
internal sealed class AnimationDriver
{
    /// <summary>The ranged attacks.</summary>
    private const int FirstRangedAttack = 5, LastRangedAttack = 9;

    /// <summary>The weapon a thrower waits with for its stone to land before throwing again.</summary>
    private const int StoneWeapon = 1;

    /// <summary>A thrower waits while its projectile has more than this many steps to fly.</summary>
    private const int ImpactWaitSteps = 2;

    /// <summary>The waking and resuming actions keep their order when the goal is in reach.</summary>
    private const int WakeUp = 80, ResumeAction = 85;

    private readonly GameWorld world;
    private readonly ActionTicks ticks;
    private readonly ActionAnimation animation;
    private readonly BuildingExit exit;
    private readonly BuildingEntry entry;
    private readonly Occupancy occupancy;
    private readonly UnitOrders orders;
    private readonly RouteFinder routes;
    private readonly Deaths deaths;
    private readonly UnitCalls calls;
    private readonly Action<UnitTurn> workCycle;
    private readonly Action<GameEvent> raise;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>
    /// Creates the system for <paramref name="world"/>; <paramref name="workCycle"/> goes on with a
    /// unit's work after its animation (the units' work cycle).
    /// </summary>
    public AnimationDriver(GameWorld world, ActionTicks ticks, ActionAnimation animation, BuildingExit exit, BuildingEntry entry,
        Occupancy occupancy, UnitOrders orders, RouteFinder routes, Deaths deaths, UnitCalls calls, Action<UnitTurn> workCycle,
        Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
        this.animation = animation ?? throw new ArgumentNullException(nameof(animation));
        this.exit = exit ?? throw new ArgumentNullException(nameof(exit));
        this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
        this.calls = calls ?? throw new ArgumentNullException(nameof(calls));
        this.workCycle = workCycle ?? throw new ArgumentNullException(nameof(workCycle));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>The animation part of the turn of <paramref name="turn"/>'s unit.</summary>
    public void Update(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        turn.StepEnded = false;
        if (AwaitsImpact(unit))
        {
            unit.Animation = UnitAnimations.AwaitingImpact;
            return;
        }
        ticks.Advance(unit);
        if (unit.AnimationTick >= animations[unit.AnimationNumber].FrameCount && EndAnimation(turn))
        {
            return;
        }
        workCycle(turn);
    }

    /// <summary>A stone thrower waits at the start of its throw while its last stone is still far from landing.</summary>
    private static bool AwaitsImpact(Unit unit) =>
        unit.ProjectileFlightSteps > 0 && unit.AnimationTick == 0 && unit.Weapon == StoneWeapon
        && (unit.Action is >= FirstRangedAttack and <= LastRangedAttack || Body.UnitMood.IsTowerAction(unit.Action))
        && unit.AnimationNumber > AnimationNumbers.LastWalking
        && unit.ProjectileStep + ImpactWaitSteps < unit.ProjectileFlightSteps;

    /// <summary>The unit's animation ended; returns whether that ended its turn.</summary>
    private bool EndAnimation(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        unit.AnimationTick = 0;
        if (unit.BuildingSlot < WorkPositionNumbers.None)
        {
            unit.BuildingSlot = WorkPositionNumbers.None;
        }
        if (unit.AnimationNumber <= AnimationNumbers.LastWalking)
        {
            return false;
        }
        if (Finish(turn))
        {
            return true;
        }
        if (world.Content.Rules.Actions.Actions[unit.Action].BuildingInteraction == BuildingInteraction.Visit && unit.ActionSubState == SubStates.Inside)
        {
            LeaveVisit(turn);
            return true;
        }
        if (entry.ReachEntrance(unit))
        {
            return true;
        }
        turn.StepEnded = true;
        return false;
    }

    /// <summary>A visitor leaves; a unit staying inside at the first work position lets the others out first.</summary>
    private void LeaveVisit(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        Building building = world.Building(unit.TargetIndex);
        if (unit.Action == ActionNumbers.StayInside && unit.BuildingSlot == 0
            && (world.Content.Rules.Buildings.Kinds[building.Kind].ManagerSlot >= 0 || building.WorkMode != BuildingWorkMode.Normal))
        {
            occupancy.Evict(building, all: false);
        }
        exit.Leave(turn);
    }

    /// <summary>
    /// The effect of the animation that ended: getting up or crouching goes on with the next step,
    /// a summoned unit walks to the chief's hut, a trap hurts. Returns whether that took the turn.
    /// </summary>
    private bool Finish(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        int ended = unit.AnimationNumber;
        if (ended is UnitAnimations.Hide or UnitAnimations.Unhide)
        {
            animation.Start(unit, turn.GoalDistance);
            return true;
        }
        if (ended == UnitAnimations.Summoned)
        {
            int hut = world.FirstBuildingSlot(TribeRoles.PlayerOf(world, unit.Tribe));
            if (hut > 0)
            {
                (unit.DestinationX, unit.DestinationY) = (world.Building(hut).X, world.Building(hut).Y);
            }
            orders.FinishOrder(unit, FinishModes.CoupleMove);
            calls.Replan(turn);
            return true;
        }
        if (turn.GoalCells <= routes.ActionRange(unit) && unit.Action != ResumeAction && unit.Action != WakeUp)
        {
            if (unit.OrderMode == OrderModes.Keep)
            {
                unit.OrderMode = OrderModes.Free;
            }
            unit.RunBonus = 0;
        }
        if (unit.Animation is >= UnitAnimations.FirstTrapped and <= UnitAnimations.LastTrapped)
        {
            BreakFree(unit);
            return true;
        }
        return false;
    }

    /// <summary>A unit caught in a trap breaks free, hurt by the trap; it may die of it.</summary>
    private void BreakFree(Unit unit)
    {
        int damage = world.Content.Rules.Armoury.Traps[unit.Animation - UnitAnimations.FirstTrapped].Damage;
        unit.HitPoints -= damage;
        raise(new HitPointsChanged(unit.Id, -damage));
        UnitActions.Stand(unit);
        unit.ActionSubState = SubStates.Normal;
        unit.NextOccupantOrCellObject = 0;
        if (unit.HitPoints <= 0)
        {
            deaths.Die(EntityProperty.UnitHitPoints, new GridPoint(unit.X, unit.Y), unit.Id, updatedLaterThisFrame: false);
        }
    }
}
