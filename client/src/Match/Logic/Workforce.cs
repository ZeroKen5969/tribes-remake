using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>What a tribe's villagers outside the buildings are working at, as the top bar counts them.</summary>
public enum WorkGroup
{
    /// <summary>Gathering food: berries and the like, hunting, butchering a carcass, fishing.</summary>
    Food,

    /// <summary>Cutting wood.</summary>
    Wood,

    /// <summary>Breaking stones.</summary>
    Stones,

    /// <summary>Gathering bones.</summary>
    Bones,

    /// <summary>Builders: building a site, upgrading or repairing, or waiting to be sent to one.</summary>
    Builders,

    /// <summary>Grown-ups with no task (and not called to arms).</summary>
    Idle,
}

/// <summary>
/// Counts a tribe's villagers by <see cref="WorkGroup"/> from their long-term task (so a
/// gatherer walking to the store still counts as one), and picks the next one of a group for the
/// top bar's jump: villagers inside a building are left out, since the view cannot show them.
/// </summary>
public static class Workforce
{
    /// <summary>Number of <see cref="WorkGroup"/> values.</summary>
    public const int Groups = 6;

    // Long-term tasks (action numbers): the gathering actions 10..16 (one per carried item; 2
    // wood, 3 stones, 4 bones, the rest food), the hunting task, and the idle task.
    private const int GatherFood = 10, GatherWood = 11, GatherMeat = 12, Fishing = 13, GatherStones = 14,
        GatherPlants = 15, GatherBones = 16, HuntingTask = 60, IdleTask = 69;

    // The long-term tasks of the construction work (the standing tasks of the actions whose
    // service group is construction, the ones the rules send as builders): building, upgrade or
    // repair work, sent to a site, waiting for material.
    private const int BuildTask = 56, UpgradeTask = 76, SentBuilderTask = 77, WaitForMaterialTask = 78;

    /// <summary>The group <paramref name="unit"/> belongs to, or null when it does something else or is a child.</summary>
    public static WorkGroup? GroupOf(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.AssignedTask switch
        {
            GatherFood or GatherMeat or Fishing or GatherPlants or HuntingTask => WorkGroup.Food,
            GatherWood => WorkGroup.Wood,
            GatherStones => WorkGroup.Stones,
            GatherBones => WorkGroup.Bones,
            BuildTask or UpgradeTask or SentBuilderTask or WaitForMaterialTask => WorkGroup.Builders,
            IdleTask when unit.Age >= RuleNumbers.AdultAge && (unit.Status & UnitStatus.Mustered) == 0 => WorkGroup.Idle,
            _ => null,
        };
    }

    /// <summary>How many of <paramref name="tribe"/>'s villagers outside the buildings are in each group (indexed by <see cref="WorkGroup"/>).</summary>
    public static int[] Counts(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        var counts = new int[Groups];
        foreach (int id in Outside(world, tribe))
        {
            if (GroupOf(world.Unit(id)) is { } group)
            {
                counts[(int)group]++;
            }
        }
        return counts;
    }

    /// <summary>The villagers of <paramref name="group"/> outside the buildings, in unit order.</summary>
    public static IReadOnlyList<int> Members(GameWorld world, int tribe, WorkGroup group)
    {
        ArgumentNullException.ThrowIfNull(world);
        var members = new List<int>();
        foreach (int id in Outside(world, tribe))
        {
            if (GroupOf(world.Unit(id)) == group)
            {
                members.Add(id);
            }
        }
        return members;
    }

    /// <summary>
    /// The member to jump to after <paramref name="last"/> (the one jumped to before): the next
    /// in unit order, round to the first; null when the group is empty.
    /// </summary>
    public static int? Next(IReadOnlyList<int> members, int? last)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
        {
            return null;
        }
        if (last is int previous)
        {
            foreach (int id in members)
            {
                if (id > previous)
                {
                    return id;
                }
            }
        }
        return members[0];
    }

    private static IEnumerable<int> Outside(GameWorld world, int tribe)
    {
        if (tribe < 0 || tribe >= world.Tribes.Count)
        {
            yield break;
        }
        foreach (int id in BuildingOffers.LivingUnits(world, tribe))
        {
            if (BuildingOffers.BuildingInside(world, world.Unit(id)) == 0)
            {
                yield return id;
            }
        }
    }
}
