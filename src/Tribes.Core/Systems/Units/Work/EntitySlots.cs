using System;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// Property access through a slot number the rules keep in a unit (its target, the object its work
/// acts on). Such a number may name no entity of the property's kind (an animal's slot read as a
/// unit, the unused slot 0, a point target): reading it then gives 0 and writing does nothing.
/// </summary>
internal static class EntitySlots
{
    /// <summary>Whether <paramref name="slot"/> is an entity of <paramref name="property"/>'s kind in <paramref name="world"/>.</summary>
    public static bool Holds(GameWorld world, EntityProperty property, int slot)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (EntityProperties.IsConstant(property))
        {
            return true;
        }
        return EntityProperties.EntityOf(property) switch
        {
            EntityKind.Unit => slot >= 1 && slot <= world.UnitSlots,
            EntityKind.Building => slot >= 1 && slot <= world.BuildingSlots,
            EntityKind.Animal => slot >= 0 && slot < world.AnimalSlots,
            _ => slot >= 0 && slot < world.Map.ResourceNodes.Count,
        };
    }

    /// <summary><paramref name="property"/> of <paramref name="slot"/>, or 0 when the slot names no such entity.</summary>
    public static int Read(GameWorld world, EntityProperty property, int slot) =>
        Holds(world, property, slot) ? EntityProperties.Get(world, property, slot) : 0;

    /// <summary>Adds <paramref name="delta"/> to <paramref name="property"/> of <paramref name="slot"/> if the slot names such an entity.</summary>
    public static void Add(GameWorld world, EntityProperty property, int slot, int delta)
    {
        if (Holds(world, property, slot))
        {
            EntityProperties.Add(world, property, slot, delta);
        }
    }

    /// <summary>Sets <paramref name="property"/> of <paramref name="slot"/> if the slot names such an entity.</summary>
    public static void Write(GameWorld world, EntityProperty property, int slot, int value)
    {
        if (Holds(world, property, slot))
        {
            EntityProperties.Set(world, property, slot, value);
        }
    }

    /// <summary>
    /// Whether <paramref name="unit"/>'s chief stands within <paramref name="cells"/> cells of it
    /// (columns and rows each); false without a chief.
    /// </summary>
    public static bool NearChief(GameWorld world, Unit unit, int cells)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(unit);
        int chief = world.Tribes[TribeRoles.PlayerOf(world, unit.Tribe)][TribeCounter.Chief];
        if (chief < 1 || chief > world.UnitSlots)
        {
            return false;
        }
        Unit head = world.Unit(chief);
        return Math.Abs(unit.X - head.X) <= cells && Math.Abs(unit.Y - head.Y) <= cells;
    }
}
