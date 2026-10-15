using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// How far a unit is from its goal, measured without touching anything: the goal is its target
/// object (a building's nearest footprint cell) or its destination, as for
/// <see cref="TargetDistances.FromUnit"/>, which an attacker also uses to retarget its destination.
/// </summary>
internal sealed class UnitGoals
{
    /// <summary>Target index values below this mean "still looking for a target"; such a unit is at its goal.</summary>
    private const int FirstPendingSearch = -1;

    private readonly GameWorld world;
    private readonly GridDistances distances;

    /// <summary>Measures goals in <paramref name="world"/>.</summary>
    public UnitGoals(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
    }

    /// <summary>The measured distance (10 × cells + facing) from <paramref name="unit"/>'s cell to its goal.</summary>
    public int Measure(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.TargetIndex < FirstPendingSearch)
        {
            return 0;
        }
        int x = unit.DestinationX, y = unit.DestinationY;
        ActionDefinition action = world.Content.Rules.Actions.Actions[unit.Action];
        if (unit.TargetIndex >= 1 && action.TargetPositionProperty is { } position
            && EntitySlots.Holds(world, position, unit.TargetIndex))
        {
            x = EntityProperties.Get(world, position, unit.TargetIndex);
            y = EntityProperties.Get(world, position + 1, unit.TargetIndex);
            if (position == EntityProperty.BuildingX)
            {
                BuildingKind kind = world.Content.Rules.Buildings.Kinds[world.Building(unit.TargetIndex).Kind];
                x = GridMath.Clamp(unit.X, x, x + kind.Width - 1);
                y = GridMath.Clamp(unit.Y, y, y + kind.Height - 1);
            }
        }
        return distances.Measure(x - unit.X, y - unit.Y);
    }

    /// <summary>Starts <paramref name="unit"/>'s turn, measuring how far it is from its goal.</summary>
    public UnitTurn StartTurn(Unit unit) => new(unit, Measure(unit));

    /// <summary>Starts <paramref name="unit"/>'s turn on <paramref name="turn"/> (used again unit after unit), measuring how far it is from its goal.</summary>
    public UnitTurn StartTurn(UnitTurn turn, Unit unit)
    {
        ArgumentNullException.ThrowIfNull(turn);
        turn.Restart(unit, Measure(unit));
        return turn;
    }
}
