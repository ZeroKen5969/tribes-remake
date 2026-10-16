using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// The top bar's head counts (<see cref="Workforce"/>): villagers by what they gather, the builders
/// and the idle grown-ups, outside the buildings, and the jump through one group in turn.
/// </summary>
public sealed class WorkforceTests
{
    [Fact]
    public void Gatherers_count_by_the_resource_of_their_task()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        int[] outside = OutsideAdults(world);
        Assert.True(outside.Length >= 5);

        world.Unit(outside[0]).AssignedTask = 11;
        world.Unit(outside[1]).AssignedTask = 14;
        world.Unit(outside[2]).AssignedTask = 16;
        world.Unit(outside[3]).AssignedTask = 60;
        world.Unit(outside[4]).AssignedTask = 13;

        Assert.Equal(WorkGroup.Wood, Workforce.GroupOf(world.Unit(outside[0])));
        Assert.Equal(WorkGroup.Stones, Workforce.GroupOf(world.Unit(outside[1])));
        Assert.Equal(WorkGroup.Bones, Workforce.GroupOf(world.Unit(outside[2])));
        Assert.Equal(WorkGroup.Food, Workforce.GroupOf(world.Unit(outside[3])));
        Assert.Equal(WorkGroup.Food, Workforce.GroupOf(world.Unit(outside[4])));
        int[] counts = Workforce.Counts(world, 0);
        for (int g = 0; g < Workforce.Groups; g++)
        {
            Assert.Equal(Workforce.Members(world, 0, (WorkGroup)g).Count, counts[g]);
        }
        Assert.Contains(outside[0], Workforce.Members(world, 0, WorkGroup.Wood));
    }

    [Fact]
    public void Idle_counts_only_grown_ups_who_are_not_called_to_arms()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        int[] outside = OutsideAdults(world);
        Unit idle = world.Unit(outside[0]), child = world.Unit(outside[1]), soldier = world.Unit(outside[2]);
        idle.AssignedTask = child.AssignedTask = soldier.AssignedTask = 69;
        child.Age = RuleNumbers.AdultAge - 1;
        soldier.Status |= UnitStatus.Mustered;

        Assert.Equal(WorkGroup.Idle, Workforce.GroupOf(idle));
        Assert.Null(Workforce.GroupOf(child));
        Assert.Null(Workforce.GroupOf(soldier));
        Assert.Contains(idle.Id, Workforce.Members(world, 0, WorkGroup.Idle));
        Assert.DoesNotContain(child.Id, Workforce.Members(world, 0, WorkGroup.Idle));
    }

    [Fact]
    public void Builders_are_the_villagers_on_a_construction_task()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        int[] outside = OutsideAdults(world);
        Tribes.Core.Content.Rules.ActionTables rules = world.Content.Rules.Actions;
        // Every long-term task of the actions the rules send as builders (service group 2).
        int[] construction = rules.Actions.Where(a => rules.Categories[a.Category].ServiceGroup == 2)
            .Select(a => a.StandingTask).Distinct().ToArray();
        Assert.NotEmpty(construction);
        Unit builder = world.Unit(outside[0]);
        foreach (int task in construction)
        {
            builder.AssignedTask = task;
            Assert.Equal(WorkGroup.Builders, Workforce.GroupOf(builder));
        }
        Assert.Contains(builder.Id, Workforce.Members(world, 0, WorkGroup.Builders));
        Assert.Equal(Workforce.Members(world, 0, WorkGroup.Builders).Count, Workforce.Counts(world, 0)[(int)WorkGroup.Builders]);
    }

    [Fact]
    public void The_jump_goes_through_the_group_in_turn_and_round_again()
    {
        int[] members = { 3, 8, 12 };

        Assert.Equal(3, Workforce.Next(members, null));
        Assert.Equal(8, Workforce.Next(members, 3));
        Assert.Equal(12, Workforce.Next(members, 8));
        Assert.Equal(3, Workforce.Next(members, 12));
        Assert.Equal(8, Workforce.Next(members, 5)); // the last one jumped to has left the group
        Assert.Null(Workforce.Next(System.Array.Empty<int>(), 3));
    }

    [Fact]
    public void Another_tribe_or_none_counts_nobody_of_this_one()
    {
        GameWorld world = Repo.Instance.Started(2).World;

        Assert.Equal(new int[Workforce.Groups], Workforce.Counts(world, -1));
        Assert.Equal(new int[Workforce.Groups], Workforce.Counts(world, world.Tribes.Count));
    }

    private static int[] OutsideAdults(GameWorld world) => BuildingOffers.LivingUnits(world, 0)
        .Where(id => BuildingOffers.BuildingInside(world, world.Unit(id)) == 0 && world.Unit(id).Age >= RuleNumbers.AdultAge)
        .ToArray();
}
