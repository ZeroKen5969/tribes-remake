using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// The top bar's head counts (<see cref="Workforce"/>): villagers by what they gather, the builders
/// at work and waiting, the grown-ups standing idle, the children and everyone else, outside the
/// buildings, and the jump through one group in turn.
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
    public void Idle_counts_only_grown_ups_standing_with_nothing_to_do()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        int[] outside = OutsideAdults(world);
        Unit idle = world.Unit(outside[0]), pausing = world.Unit(outside[1]), walking = world.Unit(outside[2]);
        Unit fighting = world.Unit(outside[3]), soldier = world.Unit(outside[4]);
        foreach (Unit unit in new[] { idle, pausing, walking, fighting, soldier })
        {
            Stand(unit, IdleTask);
        }
        pausing.Action = PauseAction;
        pausing.SavedAction = IdleTask;
        walking.DestinationX = walking.X + 1;
        fighting.Action = 0; // a melee attack ordered by the player: its long-term task is the idle one
        soldier.Status |= UnitStatus.Mustered;

        Assert.Equal(WorkGroup.Idle, Workforce.GroupOf(idle));
        Assert.Equal(WorkGroup.Idle, Workforce.GroupOf(pausing));
        Assert.Equal(WorkGroup.Other, Workforce.GroupOf(walking));
        Assert.Equal(WorkGroup.Other, Workforce.GroupOf(fighting));
        Assert.Equal(WorkGroup.Other, Workforce.GroupOf(soldier));
        Assert.Contains(idle.Id, Workforce.Members(world, 0, WorkGroup.Idle));
        Assert.DoesNotContain(walking.Id, Workforce.Members(world, 0, WorkGroup.Idle));
    }

    [Fact]
    public void Children_have_their_own_count_whatever_they_do()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        Unit child = world.Unit(OutsideAdults(world)[0]);
        Stand(child, IdleTask);
        child.Age = RuleNumbers.AdultAge - 1;

        Assert.Equal(WorkGroup.Children, Workforce.GroupOf(child));
        child.AssignedTask = 11;
        Assert.Equal(WorkGroup.Children, Workforce.GroupOf(child));
    }

    [Fact]
    public void Builders_are_the_villagers_on_a_construction_task_and_wait_apart()
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
            Stand(builder, task);
            Assert.Equal(WorkGroup.IdleBuilders, Workforce.GroupOf(builder));
            builder.Action = BuildAction;
            Assert.Equal(WorkGroup.Builders, Workforce.GroupOf(builder));
        }
        Assert.Contains(builder.Id, Workforce.Members(world, 0, WorkGroup.Builders));
        Assert.Equal(Workforce.Members(world, 0, WorkGroup.Builders).Count, Workforce.Counts(world, 0)[(int)WorkGroup.Builders]);
    }

    [Fact]
    public void Every_villager_outside_is_counted_once()
    {
        GameSimulation game = Repo.Instance.Started(2);
        for (int tick = 0; tick < 200; tick++)
        {
            game.Step(TickInput.Idle);
        }
        GameWorld world = game.World;
        int outside = BuildingOffers.LivingUnits(world, 0).Count(id => BuildingOffers.BuildingInside(world, world.Unit(id)) == 0);

        Assert.Equal(outside, Workforce.Counts(world, 0).Sum());
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

    private const int IdleTask = 69, PauseAction = 84, BuildAction = 56;

    /// <summary>Makes <paramref name="unit"/> an idling grown-up on <paramref name="task"/>, standing where it was going.</summary>
    private static void Stand(Unit unit, int task)
    {
        unit.AssignedTask = task;
        unit.Action = IdleTask;
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        unit.Status &= ~UnitStatus.Mustered;
    }

    private static int[] OutsideAdults(GameWorld world) => BuildingOffers.LivingUnits(world, 0)
        .Where(id => BuildingOffers.BuildingInside(world, world.Unit(id)) == 0 && world.Unit(id).Age >= RuleNumbers.AdultAge)
        .ToArray();
}
