using System;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// The thing a cell code stands for in a world: the code says which kind and slot (see
/// <see cref="CellCode"/>; codes of units and buildings past the original's 100 and 60 slots lie
/// far outside its byte range), and the slot must exist in the world.
/// </summary>
public static class CellTargets
{
    /// <summary>The unit slot <paramref name="code"/> stands for, if the world has it.</summary>
    public static int? UnitOf(GameWorld world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        int unit = CellCode.UnitOf(code);
        return unit >= 1 && unit <= world.UnitSlots ? unit : null;
    }

    /// <summary>The building slot <paramref name="code"/> stands for, if the world has it.</summary>
    public static int? BuildingOf(GameWorld world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        int building = CellCode.BuildingOf(code);
        return building >= 1 && building <= world.BuildingSlots ? building : null;
    }

    /// <summary>The animal slot <paramref name="code"/> stands for, if the world has it.</summary>
    public static int? AnimalOf(GameWorld world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        int animal = CellCode.AnimalOf(code);
        return animal >= 0 && animal < world.AnimalSlots ? animal : null;
    }

    /// <summary>The resource node slot <paramref name="code"/> stands for, if the map has it.</summary>
    public static int? NodeOf(GameWorld world, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        int node = CellCode.NodeOf(code);
        return node >= 0 && node < world.Map.ResourceNodes.Count ? node : null;
    }
}
