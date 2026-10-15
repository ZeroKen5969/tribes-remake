using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>How a unit's attempt to swap cells with a tribe mate in its way ended.</summary>
internal enum SwapResult
{
    /// <summary>No swap was tried (the next cell is free).</summary>
    None = 0,

    /// <summary>The two swapped cells and slide past each other.</summary>
    Swapped = 1,

    /// <summary>The mate is busy and stays; the unit waits.</summary>
    Refused = 2,
}

/// <summary>
/// A walking unit and a tribe mate in its way swap cells unless the mate is too busy: the mate
/// gives up its route, turns round and steps sideways past the unit; a mate whose task site was
/// its cell keeps its new cell as its site.
/// </summary>
internal sealed class PlaceSwap
{
    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickBits = 0x0F;

    private readonly GameWorld world;
    private readonly Interruptions interruptions;
    private readonly StepMotion motion;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public PlaceSwap(GameWorld world, Interruptions interruptions, StepMotion motion)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.interruptions = interruptions ?? throw new ArgumentNullException(nameof(interruptions));
        this.motion = motion ?? throw new ArgumentNullException(nameof(motion));
    }

    /// <summary><paramref name="unit"/> tries to swap cells with <paramref name="mate"/>.</summary>
    public SwapResult Swap(Unit unit, Unit mate)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(mate);
        if (interruptions.HoldsOn(mate, mate.Id < unit.Id ? 0 : -1, byOrder: false))
        {
            return SwapResult.Refused;
        }
        mate.PathLength = 0;
        (mate.X, unit.X) = (unit.X, mate.X);
        (mate.Y, unit.Y) = (unit.Y, mate.Y);
        if (mate.TaskSiteX == unit.X && mate.TaskSiteY == unit.Y)
        {
            (mate.TaskSiteX, mate.TaskSiteY) = (mate.X, mate.Y);
            (mate.DestinationX, mate.DestinationY) = (mate.X, mate.Y);
        }
        world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.UnitAt(unit.Id));
        world.Map.SetCell(new GridPoint(mate.X, mate.Y), MapCell.UnitAt(mate.Id));
        mate.Facing = Facings.Opposite(unit.Facing);
        mate.StepAndActionTicks &= ~StepTickBits;
        mate.PathBlock = StepMotion.Swapping;
        unit.PathBlock = StepMotion.Swapping;
        mate.Animation = UnitActions.WalkingAnimation(mate);
        motion.Step(mate);
        if (mate.Id > unit.Id)
        {
            mate.StepAndActionTicks &= ~StepTickBits;
        }
        return SwapResult.Swapped;
    }
}
