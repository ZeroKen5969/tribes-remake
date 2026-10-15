using Tribes.Core.Simulation;

namespace Tribes.Core.Systems.Entities;

/// <summary>Kinds of notices (<see cref="Notices.NoticeEvent.Kind"/>) the entity rules raise.</summary>
internal static class EntityNotices
{
    /// <summary>A unit of the player was born (position and unit).</summary>
    public const int UnitBorn = 1;

    /// <summary>A unit of the player died (position and unit).</summary>
    public const int UnitDied = 3;

    /// <summary>A building of the player was placed by the game (position).</summary>
    public const int BuildingPlaced = 5;

    /// <summary>A building of the player was lost (position).</summary>
    public const int BuildingLost = 8;

    /// <summary>The player has no free building slot (or no room on the construction list).</summary>
    public const int NoBuildingSlot = 10;
}

/// <summary>A unit's hit points changed by <paramref name="Delta"/>; the screen shows the number over it.</summary>
/// <param name="Unit">The unit slot.</param>
/// <param name="Delta">The change (negative for damage).</param>
public sealed record HitPointsChanged(int Unit, int Delta) : GameEvent;

/// <summary>
/// A unit left its slot (it died or went over to another tribe); the screen drops it from
/// selections and panels.
/// </summary>
/// <param name="Unit">The unit slot.</param>
public sealed record UnitGone(int Unit) : GameEvent;

/// <summary>A building was destroyed; for the player's first building the camera goes there.</summary>
/// <param name="Building">The building slot.</param>
/// <param name="FocusCamera">Whether the view should move to the building.</param>
public sealed record BuildingDestroyed(int Building, bool FocusCamera) : GameEvent;
