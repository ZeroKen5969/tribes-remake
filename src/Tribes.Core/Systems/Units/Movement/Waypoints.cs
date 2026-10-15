using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>What starting a step towards the next waypoint gave.</summary>
/// <param name="Stopped">Whether the unit does not step this frame (its way is blocked or its route broken).</param>
/// <param name="Swap">How a swap with a tribe mate on the waypoint went.</param>
internal readonly record struct WaypointStep(bool Stopped, SwapResult Swap);

/// <summary>
/// A unit on a cell centre turns to the next waypoint of its route and reserves the cell. A blocked
/// waypoint makes it plan again (a fighter chasing a threat switches to its chasing variant); a
/// tribe mate on it is asked to swap. A unit at the end of its route does not step (it plans again
/// at its next decision); a route whose next waypoint is not next to the unit is dropped and the
/// unit decides anew.
/// </summary>
internal sealed class Waypoints
{
    /// <summary>The melee attacks that have a chasing variant (the next action number).</summary>
    private const int MeleeOnUnit = 0, MeleeOnBuilding = 3;

    /// <summary>The facing table is indexed (dx + 1) * this + (dy + 1) for deltas of -1..1.</summary>
    private const int DeltaSide = 3;

    /// <summary>A threat of more than this priority makes a blocked fighter switch to chasing.</summary>
    private const int ChasedThreat = 1;

    private readonly GameWorld world;
    private readonly Passability passability;
    private readonly PlaceSwap swaps;
    private readonly Action<UnitTurn> respond;
    private readonly Action<UnitTurn> replan;
    private readonly IReadOnlyList<int> facingByDelta;
    private readonly IReadOnlyList<DirectionStep> directions;

    /// <summary>
    /// Creates the system for <paramref name="world"/>; <paramref name="respond"/> makes a unit
    /// decide what to do anew and <paramref name="replan"/> plans its route again (the units' call cycle).
    /// </summary>
    public Waypoints(GameWorld world, Passability passability, PlaceSwap swaps, Action<UnitTurn> respond, Action<UnitTurn> replan)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        this.swaps = swaps ?? throw new ArgumentNullException(nameof(swaps));
        this.respond = respond ?? throw new ArgumentNullException(nameof(respond));
        this.replan = replan ?? throw new ArgumentNullException(nameof(replan));
        facingByDelta = world.Content.Rules.Movement.FacingByDeltaColumnMajor;
        directions = world.Content.Rules.Movement.Directions;
    }

    /// <summary>Starts the step of <paramref name="turn"/>'s unit towards its next waypoint, when it stands on a cell centre.</summary>
    public WaypointStep Next(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (!unit.IsCentred || unit.ActionSubState == SubStates.Entering)
        {
            return new WaypointStep(false, SwapResult.None);
        }
        if (unit.PathIndex >= unit.PathLength)
        {
            return new WaypointStep(true, SwapResult.None);
        }
        if (Waypoint(unit) is not { } waypoint)
        {
            respond(turn);
            unit.PathLength = 0;
            return new WaypointStep(true, SwapResult.None);
        }
        unit.Facing = facingByDelta[((waypoint.X - unit.X + 1) * DeltaSide) + (waypoint.Y - unit.Y + 1)];
        if (passability.BlocksUnit(unit, waypoint))
        {
            if (turn.RegularDecision && turn.Threat.Priority > ChasedThreat)
            {
                if (unit.Action is MeleeOnUnit or MeleeOnBuilding)
                {
                    unit.Action++;
                }
                if (unit.OrderMode == Entities.OrderModes.Keep)
                {
                    unit.OrderMode = Entities.OrderModes.Free;
                }
            }
            replan(turn);
            return new WaypointStep(true, SwapResult.None);
        }
        MapCell content = world.Map.CellAt(waypoint);
        SwapResult swap = SwapResult.None;
        if (content.Content == CellContent.Unit && world.TribeOfUnit(content.Index) == unit.Tribe)
        {
            swap = swaps.Swap(unit, world.Unit(content.Index));
        }
        else
        {
            unit.PathBlock = 0;
        }
        if (swap == SwapResult.None)
        {
            Reserve(unit);
        }
        if (unit.AnimationNumber <= Entities.AnimationNumbers.LastWalking && swap != SwapResult.Refused)
        {
            unit.PathIndex++;
        }
        return new WaypointStep(false, swap);
    }

    /// <summary>The unit's next waypoint, or null when its route is broken (no waypoint there, or not next to the unit).</summary>
    private static GridPoint? Waypoint(Unit unit)
    {
        if (unit.PathIndex < 0 || unit.PathIndex >= UnitPath.Capacity)
        {
            return null;
        }
        var waypoint = new GridPoint(unit.Path.XAt(unit.PathIndex), unit.Path.YAt(unit.PathIndex));
        return Math.Abs(waypoint.X - unit.X) <= 1 && Math.Abs(waypoint.Y - unit.Y) <= 1 ? waypoint : null;
    }

    /// <summary>Reserves the cell ahead; what was there (a trap, the shore) is kept with the unit to step onto.</summary>
    private void Reserve(Unit unit)
    {
        DirectionStep ahead = directions[unit.Facing];
        var next = new GridPoint(unit.X + ahead.Dx, unit.Y + ahead.Dy);
        int code = CellCode.At(world.Map, next);
        if (code != CellCode.Empty)
        {
            unit.NextOccupantOrCellObject = CellCode.SteppingValue(code);
        }
        world.Map.SetCell(next, MapCell.Of(CellContent.Reserved));
    }
}
