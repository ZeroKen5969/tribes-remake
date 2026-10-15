using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>The icons of a unit's panel: its task and its two best civil and combat skills.</summary>
/// <param name="Task">The category of the unit's task (the task icon).</param>
/// <param name="Civil1">The best civil skill's icon, or -1.</param>
/// <param name="Civil2">The second civil skill's icon, or -1.</param>
/// <param name="Combat1">The best combat skill's icon, or -1.</param>
/// <param name="Combat2">The second combat skill's icon, or -1.</param>
public sealed record SkillIcons(int Task, int Civil1, int Civil2, int Combat1, int Combat2);

/// <summary>
/// Limits that follow from a unit's skills: how far it throws (at least 3 + half its ranged skill,
/// at most 7) and how far it sees (beyond its throw, at most 9); and, for the unit panel, which
/// of its skills stand out.
/// </summary>
internal static class UnitSkillLimits
{
    /// <summary>Shortest throw: this plus half the ranged skill.</summary>
    private const int BaseThrowRange = 3;

    /// <summary>Longest throw.</summary>
    private const int MaxThrowRange = 7;

    /// <summary>A unit sees at least one cell beyond its throw, and at most this far.</summary>
    private const int MaxSightRadius = 9;

    /// <summary>The combat skills (melee, ranged, scouting) are skills 0..2; the civil skills 3..8.</summary>
    private const int LastCombatSkill = 2, FirstCivilSkill = 3, LastCivilSkill = 8;

    /// <summary>The filler skills ranked before any real one: 9 is always 1, 10 always 0.</summary>
    private const int FillerOne = 9, FillerZero = 10;

    /// <summary>Task categories 1..7 (the gatherings) share one icon, shown as category 7.</summary>
    private const int FirstGatheringCategory = 1, GatheringCategory = 7;

    /// <summary>An icon slot that shows nothing.</summary>
    private const int NoIcon = -1;

    /// <summary>Keeps <paramref name="unit"/>'s throwing range and sight radius within what its ranged skill allows.</summary>
    public static void ClampRanges(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit.ThrowRange = GridMath.Clamp(unit.ThrowRange, BaseThrowRange + (unit.RangedSkill / 2), MaxThrowRange);
        unit.SightRadius = GridMath.Clamp(unit.SightRadius, unit.ThrowRange + 1, MaxSightRadius);
    }

    /// <summary>
    /// The panel icons of <paramref name="unit"/>: the task's category, the two best combat skills,
    /// and the two best civil skills other than the one its task already shows.
    /// </summary>
    public static SkillIcons Icons(GameWorld world, Unit unit)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(unit);
        GameRules rules = world.Content.Rules;
        IReadOnlyList<SkillKind> skills = rules.Skills.Skills;
        int task = rules.Actions.Actions[unit.AssignedTask].Category;
        int Value(int skill) => EntityProperties.Get(world, skills[skill].Property, unit.Id);
        int[] combat = { FillerZero, FillerZero };
        for (int skill = LastCombatSkill; skill >= 0; skill--)
        {
            RankCombat(combat, skill, Value);
        }
        int shownTask = task >= FirstGatheringCategory && task < GatheringCategory ? GatheringCategory : task;
        int[] civil = { FillerOne, FillerOne };
        for (int skill = FirstCivilSkill; skill <= LastCivilSkill; skill++)
        {
            if (Beats(civil, skill, Value) is int place && skills[skill].TaskIcon != shownTask)
            {
                Insert(civil, place, skill);
            }
        }
        int Icon(int skill) => skill > LastCivilSkill ? NoIcon : skills[skill].TaskIcon ?? NoIcon;
        return new SkillIcons(task, Icon(civil[0]), Icon(civil[1]), Icon(combat[0]), Icon(combat[1]));
    }

    /// <summary>Puts <paramref name="skill"/> before the first ranked skill it equals or beats (later skills win ties).</summary>
    private static void RankCombat(int[] ranking, int skill, Func<int, int> value)
    {
        for (int place = 0; place < ranking.Length; place++)
        {
            if (value(ranking[place]) <= value(skill))
            {
                Insert(ranking, place, skill);
                return;
            }
        }
    }

    /// <summary>The first place whose skill <paramref name="skill"/> beats, or null.</summary>
    private static int? Beats(int[] ranking, int skill, Func<int, int> value)
    {
        for (int place = 0; place < ranking.Length; place++)
        {
            if (value(ranking[place]) < value(skill))
            {
                return place;
            }
        }
        return null;
    }

    private static void Insert(int[] ranking, int place, int skill)
    {
        for (int i = ranking.Length - 1; i > place; i--)
        {
            ranking[i] = ranking[i - 1];
        }
        ranking[place] = skill;
    }
}
