using System;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// The alliance building of endless worlds: built like any building (its construction site is the
/// 3 x 3 site), one per tribe; while a finished one stands its tribe can invite another tribe's
/// player into an alliance and give allies goods. Units do not go inside it.
/// </summary>
public static class AllianceBuilding
{
    /// <summary>Its building kind (after the original kinds; only an endless world's content has it).</summary>
    public const int Kind = 26;

    /// <summary>The picture id the client draws the finished building with.</summary>
    public const string SpriteId = "alliance-hall";

    /// <summary>Whether <paramref name="kind"/> is the alliance building.</summary>
    public static bool Is(int kind) => kind == Kind;

    /// <summary>Whether a finished alliance building of <paramref name="tribe"/> stands.</summary>
    public static bool Stands(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return SlotOf(world, tribe) > 0;
    }

    /// <summary>
    /// The slot of a finished alliance building of <paramref name="tribe"/>, or 0 when it has none
    /// (also when the world is not endless or there is no such tribe).
    /// </summary>
    public static int SlotOf(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Endless == null || tribe < 0 || tribe >= world.Tribes.Count)
        {
            return 0;
        }
        foreach (Building building in world.BuildingsOf(tribe))
        {
            if (building.HitPoints > 0 && building.Kind == Kind)
            {
                return building.Id;
            }
        }
        return 0;
    }

    /// <summary>Whether <paramref name="tribe"/> has an alliance building, finished or as a construction site.</summary>
    internal static bool Planned(GameWorld world, int tribe)
    {
        foreach (Building building in world.BuildingsOf(tribe))
        {
            if (building.HitPoints > 0 && (building.Kind == Kind || (Entities.BuildingKindNumbers.IsSite(building.Kind) && building.Aux == Kind)))
            {
                return true;
            }
        }
        return false;
    }
}
