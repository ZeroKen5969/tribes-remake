using System;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// The walking part of a unit's turn: a unit between cells, stepping through an entrance, or with a
/// route towards a goal out of reach walks on (and may be caught by a trap on arriving at a cell);
/// a unit that stopped walking stands still.
/// </summary>
internal sealed class MovementDriver
{
    /// <summary>The action tick counter (high four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int ActionTickBits = 0xF0;

    private readonly GameWorld world;
    private readonly RouteFinder routes;
    private readonly Waypoints waypoints;
    private readonly StepMotion motion;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public MovementDriver(GameWorld world, RouteFinder routes, Waypoints waypoints, StepMotion motion)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
        this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
    }

    /// <summary>The walking part of the turn of <paramref name="turn"/>'s unit.</summary>
    public void Update(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        bool walks = !unit.IsCentred || unit.ActionSubState == SubStates.Entering
            || (turn.GoalCells > routes.ActionRange(unit) && unit.PathLength > 0);
        if (!walks)
        {
            if (unit.AnimationNumber <= AnimationNumbers.LastWalking)
            {
                unit.AnimationTick = 0;
                unit.StepAndActionTicks &= ~ActionTickBits;
                UnitActions.Stand(unit);
                unit.PathLength = 0;
            }
            return;
        }
        unit.Animation = UnitActions.WalkingAnimation(unit);
        WaypointStep step = waypoints.Next(turn);
        if (step.Stopped || unit.AnimationNumber > AnimationNumbers.LastWalking || step.Swap == SwapResult.Refused)
        {
            return;
        }
        motion.Step(unit);
        if (unit.IsCentred && StepMotion.IsCaught(unit))
        {
            Caught(unit);
        }
    }

    /// <summary>
    /// <paramref name="unit"/> stepped onto an enemy trap: it struggles in it and stops. A trap of a
    /// computer tribe counts as no longer set.
    /// </summary>
    private void Caught(Unit unit)
    {
        int code = unit.NextOccupantOrCellObject;
        int owner = CellCode.TrapOwnerOf(code);
        int trap = owner >= 0
            ? CellCode.TrapKindOf(code) - CellCode.FirstTrapKind
            : CellCode.FirstTrap - (TribeRoles.Enemy(world, unit.Tribe) * CellCode.TrapCodesPerTribe) - code;
        unit.Animation = UnitAnimations.FirstTrapped + trap;
        unit.AnimationTick = 0;
        unit.PathLength = 0;
        unit.ActionSubState = SubStates.FirstPose;
        int setter = owner >= 0 ? owner : TribeRoles.Enemy(world, unit.Tribe);
        if (TribeRoles.IsComputer(world, setter))
        {
            world.Tribes[setter].Ai.Flags--;
        }
    }
}
