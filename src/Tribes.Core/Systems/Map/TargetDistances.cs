using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>
/// Where a unit's or animal's current goal is and how far it is from a cell: the goal is the
/// target object of the unit's action (the nearest cell of a building's footprint), or else its
/// destination.
/// </summary>
/// <param name="Measured">10 * distance + facing from the cell to the goal (<see cref="GridDistances.Measure"/>).</param>
/// <param name="Goal">The goal cell.</param>
internal readonly record struct GoalDistance(int Measured, GridPoint Goal)
{
    /// <summary>The distance in cells.</summary>
    public int Cells => GridDistances.Cells(Measured);
}

/// <summary>Distances from cells to what units and animals are heading for.</summary>
internal sealed class TargetDistances
{
    /// <summary>A target index below this means the unit is still looking for a target.</summary>
    private const int FirstPendingSearch = -1;

    private readonly GameWorld world;
    private readonly GridDistances distances;

    /// <summary>Measures in <paramref name="world"/>.</summary>
    public TargetDistances(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
    }

    /// <summary>
    /// The goal of <paramref name="unit"/> as seen from <paramref name="from"/>. A building target is
    /// reached at the footprint cell nearest to <paramref name="from"/>; an attacker then also
    /// makes that cell its destination. A unit still looking for a target is taken to be there.
    /// </summary>
    public GoalDistance FromUnit(Unit unit, GridPoint from)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var destination = new GridPoint(unit.DestinationX, unit.DestinationY);
        if (unit.TargetIndex < FirstPendingSearch)
        {
            return new GoalDistance(0, destination);
        }
        ActionDefinition action = world.Content.Rules.Actions.Actions[unit.Action];
        if (unit.TargetIndex < 1 || action.TargetPositionProperty is not { } position
            || !Units.Work.EntitySlots.Holds(world, position, unit.TargetIndex))
        {
            return Measure(from, destination);
        }
        int target = unit.TargetIndex;
        int x = EntityProperties.Get(world, position, target);
        int y = EntityProperties.Get(world, position + 1, target);
        if (position == EntityProperty.BuildingX)
        {
            BuildingKind kind = world.Content.Rules.Buildings.Kinds[world.Building(target).Kind];
            x = GridMath.Clamp(from.X, x, x + kind.Width - 1);
            y = GridMath.Clamp(from.Y, y, y + kind.Height - 1);
            if (unit.Action <= ActionNumbers.LastBasicAttack)
            {
                unit.DestinationX = x;
                unit.DestinationY = y;
            }
        }
        return Measure(from, new GridPoint(x, y));
    }

    /// <summary>The destination of <paramref name="animal"/> as seen from <paramref name="from"/>.</summary>
    public GoalDistance FromAnimal(Animal animal, GridPoint from)
    {
        ArgumentNullException.ThrowIfNull(animal);
        return Measure(from, new GridPoint(animal.DestinationX, animal.DestinationY));
    }

    /// <summary>
    /// If <paramref name="unit"/> is inside a tower of its tribe (any building when
    /// <paramref name="anyKind"/>), makes that building its target and its own cell its
    /// destination; returns whether it was found inside one.
    /// </summary>
    public bool AimAtHoldingBuilding(Unit unit, bool anyKind)
    {
        ArgumentNullException.ThrowIfNull(unit);
        foreach (Building building in world.BuildingsOf(unit.Tribe))
        {
            if ((building.Kind == BuildingKindNumbers.Tower || anyKind) && building.OccupantCount >= 1 && Holds(building, unit))
            {
                unit.TargetIndex = building.Id;
                unit.TargetCellCode = CellCode.OfBuilding(building.Id);
                unit.DestinationX = unit.X;
                unit.DestinationY = unit.Y;
                return true;
            }
        }
        return false;
    }

    private bool Holds(Building building, Unit unit)
    {
        for (int occupant = building.FirstOccupant; occupant > 0; occupant = world.Unit(occupant).NextOccupantOrCellObject)
        {
            if (occupant == unit.Id)
            {
                return true;
            }
        }
        return false;
    }

    private GoalDistance Measure(GridPoint from, GridPoint goal) =>
        new(distances.Measure(goal.X - from.X, goal.Y - from.Y), goal);
}
