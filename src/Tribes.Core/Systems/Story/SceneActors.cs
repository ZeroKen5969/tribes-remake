using System;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>The units a scene's actor slots hold.</summary>
internal static class SceneActors
{
    /// <summary>
    /// The unit in actor slot <paramref name="slot"/>, or null when the slot is empty or holds
    /// something else (a scene's subject may be an animal's cell code).
    /// </summary>
    public static Unit? Unit(GameWorld world, int slot)
    {
        ArgumentNullException.ThrowIfNull(world);
        int id = world.Script.Actor(slot);
        return id >= 1 && id <= world.UnitSlots ? world.Unit(id) : null;
    }
}
