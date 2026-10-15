using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>
/// A unit's mood, updated every 32 frames once the mission's needs have started: what it does
/// raises or lowers it (a dull task costs more without a home), hunger and tiredness lower it, the
/// building it works in can cheer it up, and the first missions and the computer's tribe are a
/// little happier.
/// </summary>
internal sealed class UnitMood
{
    /// <summary>Highest happiness.</summary>
    public const int HighestHappiness = 99;

    /// <summary>A dull action costs this much more to a unit without a home.</summary>
    private const int HomelessPenalty = 3;

    /// <summary>Below this much rest or food a unit's mood drops one more point.</summary>
    private const int LowNeed = 20;

    /// <summary>Missions before this one cheer every unit up a little.</summary>
    private const int FirstMissionWithoutBonus = 2;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitMood(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        actions = world.Content.Rules.Actions.Actions;
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>Updates <paramref name="unit"/>'s happiness.</summary>
    public void Update(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (world.Frame < world.Content.Rules.Missions.Missions[world.Mission].NeedsStartFrame)
        {
            return;
        }
        int delta = actions[unit.Action].HappinessDelta;
        int mood = unit.Happiness + delta - (delta < 0 && unit.Home < 1 ? HomelessPenalty : 0)
            - (unit.Energy < LowNeed ? 1 : 0) - (unit.Satiety < LowNeed ? 1 : 0)
            + BuildingBonus(unit)
            + (world.Mission < FirstMissionWithoutBonus ? 1 : 0)
            + (TribeRoles.IsComputer(world, unit.Tribe) ? 1 : 0);
        unit.Happiness = GridMath.Clamp(mood, 0, HighestHappiness);
    }

    /// <summary>The cheer of the building <paramref name="unit"/> works in (tower guards get none).</summary>
    private int BuildingBonus(Unit unit)
    {
        if (unit.BuildingSlot < 0 || IsTowerAction(unit.Action)
            || unit.TargetIndex < 1 || unit.TargetIndex > world.BuildingSlots)
        {
            return 0;
        }
        Building building = world.Building(unit.TargetIndex);
        return building.Level >= 1 ? kinds[building.Kind].InsideHappinessBonus[building.Level - 1] : 0;
    }

    /// <summary>Whether <paramref name="action"/> is one of a tower guard's.</summary>
    public static bool IsTowerAction(int action) =>
        action is ActionNumbers.TowerGuard or ActionNumbers.TowerShot or ActionNumbers.TowerAnimalGuard;
}
