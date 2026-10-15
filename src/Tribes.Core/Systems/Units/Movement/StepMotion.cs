using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// One frame of a unit's step from one cell to the next: the pixel offset follows the speed
/// class's gait, and half way the unit moves over to the next cell (putting back the trap it
/// stood on, or stepping onto one). A unit stepping through a building's entrance arrives inside
/// instead. Two tribe mates swapping cells slide past each other sideways.
/// </summary>
internal sealed class StepMotion
{
    /// <summary><see cref="Unit.PathBlock"/> while the unit swaps cells with a tribe mate.</summary>
    public const int Swapping = 120;

    /// <summary><see cref="Unit.PathBlock"/> once a swap is over.</summary>
    private const int SwapDone = 1;

    /// <summary>Cell size in pixels.</summary>
    private const int CellWidth = 22, CellHeight = 16;

    private readonly GameWorld world;
    private readonly Arrival arrival;
    private readonly GaitTables gaits;
    private readonly IReadOnlyList<DirectionStep> directions;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public StepMotion(GameWorld world, Arrival arrival)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.arrival = arrival ?? throw new ArgumentNullException(nameof(arrival));
        gaits = new GaitTables(world.Content.Rules.Movement.SpeedClasses);
        directions = world.Content.Rules.Movement.Directions;
    }

    /// <summary>Whether <paramref name="unit"/> stands on a trap that caught it (a trap's object code).</summary>
    public static bool IsCaught(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return CellCode.IsCatching(unit.NextOccupantOrCellObject);
    }

    /// <summary>One frame of <paramref name="unit"/>'s step.</summary>
    public void Step(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int steps = Advance(unit);
        int ticks = unit.StepTicks;
        int half = (steps + 1) / 2;
        DirectionStep direction = directions[unit.Facing];
        if (ticks == half && unit.PathBlock != Swapping && !MoveOver(unit, direction))
        {
            return;
        }
        if (unit.PathBlock == Swapping)
        {
            if (ticks < half)
            {
                unit.OffsetX -= direction.Dx * CellWidth;
                unit.OffsetY -= direction.Dy * CellHeight;
            }
            int speed = unit.MoveDelay - unit.RunBonus;
            bool diagonal = (unit.Facing & 1) != 0;
            unit.OffsetX += gaits.Offset(speed, diagonal, GaitOffset.SwapX, ticks) * direction.SideDx;
            unit.OffsetY += gaits.Offset(speed, diagonal, GaitOffset.SwapY, ticks) * direction.SideDy;
        }
    }

    /// <summary>
    /// Advances the step's frame counter and sets the pixel offset; returns the step's frames. The
    /// offset jumps a cell back where the unit changes cells moving left or up with an even
    /// number of frames.
    /// </summary>
    private int Advance(Unit unit)
    {
        int speed = unit.MoveDelay - unit.RunBonus;
        bool diagonal = (unit.Facing & 1) != 0;
        int steps = gaits.Gait(speed, diagonal).StepFrames;
        unit.StepAndActionTicks++;
        int ticks = unit.StepTicks;
        DirectionStep direction = directions[unit.Facing];
        unit.OffsetX = gaits.Offset(speed, diagonal, GaitOffset.StepX, (steps + (ticks * direction.Dx)) % steps);
        unit.OffsetY = gaits.Offset(speed, diagonal, GaitOffset.StepY, (steps + (ticks * direction.Dy)) % steps);
        if (unit.IsCentred && unit.PathBlock == Swapping)
        {
            unit.PathBlock = SwapDone;
        }
        if (ticks == (steps + 1) / 2 && steps % 2 == 0)
        {
            if (direction.Dx < 0)
            {
                unit.OffsetX += CellWidth;
            }
            if (direction.Dy < 0)
            {
                unit.OffsetY += CellHeight;
            }
        }
        return steps;
    }

    /// <summary>
    /// Half way through the step <paramref name="unit"/> leaves its cell for the next one; a unit
    /// stepping through an entrance arrives inside instead. Returns false when it arrived.
    /// </summary>
    private bool MoveOver(Unit unit, DirectionStep direction)
    {
        var from = new GridPoint(unit.X, unit.Y);
        world.Map.SetCell(from, MapCell.Empty);
        if (CellCode.IsStandingObject(unit.NextOccupantOrCellObject))
        {
            world.Map.SetCell(from, CellCode.FromCode(unit.NextOccupantOrCellObject, world));
            unit.NextOccupantOrCellObject = 0;
        }
        if (CellCode.IsSteppingObject(unit.NextOccupantOrCellObject))
        {
            unit.NextOccupantOrCellObject = -unit.NextOccupantOrCellObject;
            unit.ActionSubState = SubStates.Normal;
        }
        if (unit.ActionSubState == SubStates.Entering)
        {
            arrival.Arrive(unit);
            return false;
        }
        unit.X += direction.Dx;
        unit.Y += direction.Dy;
        world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.UnitAt(unit.Id));
        return true;
    }
}
