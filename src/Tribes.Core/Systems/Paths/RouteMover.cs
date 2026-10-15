using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Paths;

/// <summary>What a route is planned for: a unit or an animal, which differ in what blocks them, where they head and how long a route they keep.</summary>
internal abstract class RouteMover
{
    /// <summary>Most waypoints the mover's route holds.</summary>
    public abstract int Capacity { get; }

    /// <summary>Whether <paramref name="cell"/> (in play) stops the mover.</summary>
    public abstract bool Blocks(GridPoint cell);

    /// <summary>The mover's goal as seen from <paramref name="from"/>, and how far it is.</summary>
    public abstract GoalDistance Goal(GridPoint from);

    /// <summary>Stores waypoint <paramref name="index"/> of the mover's route.</summary>
    public abstract void SetWaypoint(int index, GridPoint cell);

    /// <summary>Called once a route segment has been stored.</summary>
    public virtual void RouteStored()
    {
    }
}

/// <summary>A unit: blocked as <see cref="Passability.BlocksUnit"/> says, heading for its action's target, with long routes.</summary>
internal sealed class UnitRouteMover : RouteMover
{
    private Unit unit;
    private readonly Passability passability;
    private readonly TargetDistances targets;

    /// <summary>Plans for <paramref name="unit"/>.</summary>
    public UnitRouteMover(Unit unit, Passability passability, TargetDistances targets)
    {
        this.unit = unit ?? throw new ArgumentNullException(nameof(unit));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>Plans for <paramref name="next"/> from now on (the planner is used again unit after unit).</summary>
    public void MoveFor(Unit next) => unit = next ?? throw new ArgumentNullException(nameof(next));

    /// <inheritdoc/>
    public override int Capacity => UnitPath.Capacity;

    /// <inheritdoc/>
    public override bool Blocks(GridPoint cell) => passability.BlocksUnit(unit, cell);

    /// <inheritdoc/>
    public override GoalDistance Goal(GridPoint from) => targets.FromUnit(unit, from);

    /// <inheritdoc/>
    public override void SetWaypoint(int index, GridPoint cell) => unit.Path.Set(index, cell.X, cell.Y);

    /// <summary>A unit with a new route starts it from its first waypoint, standing.</summary>
    public override void RouteStored()
    {
        unit.PathIndex = 0;
        unit.AnimationTick = 0;
        UnitActions.Stand(unit);
    }
}

/// <summary>An animal: blocked by anything but its own cell, heading for its destination, with short routes.</summary>
internal sealed class AnimalRouteMover : RouteMover
{
    private Animal animal;
    private readonly Passability passability;
    private readonly TargetDistances targets;

    /// <summary>Plans for <paramref name="animal"/>.</summary>
    public AnimalRouteMover(Animal animal, Passability passability, TargetDistances targets)
    {
        this.animal = animal ?? throw new ArgumentNullException(nameof(animal));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>Plans for <paramref name="next"/> from now on (the planner is used again animal after animal).</summary>
    public void MoveFor(Animal next) => animal = next ?? throw new ArgumentNullException(nameof(next));

    /// <inheritdoc/>
    public override int Capacity => Animal.PathCapacity;

    /// <inheritdoc/>
    public override bool Blocks(GridPoint cell) => passability.BlocksAnimal(animal, cell);

    /// <inheritdoc/>
    public override GoalDistance Goal(GridPoint from) => targets.FromAnimal(animal, from);

    /// <inheritdoc/>
    public override void SetWaypoint(int index, GridPoint cell) => animal.SetPathPoint(index, cell.X, cell.Y);
}
