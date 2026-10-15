using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// The army calls (集合, 全員備戰) of a match belong to the tribe that gave them: its units answer
/// them, and nothing another tribe does (its calls and cancels, the computers' troops) changes them.
/// </summary>
public sealed class ArmyCallTests
{
    /// <summary>Two humans (tribes 0 and 1) and two computers (tribes 2 and 3), as in the reported game.</summary>
    private static GameSimulation FourTribes(int map) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, 11 + map, MatchWorlds.Controllers(2, 2)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SecondHumansCalls_AreItsOwn_WhileTheOthersCallAndTheComputersFight(int map)
    {
        GameSimulation game = FourTribes(map);
        GameWorld world = game.World;
        ArmTribe(world, 1);
        Run(game, 200);
        GridPoint rally = RallyPoint(world, 1);

        Assert.Equal(OrderOutcome.Applied, Order(game, new CallArmy(1, ArmyCall.Rally, rally)));

        ArmyOrders army = world.Tribes[1].Army;
        Assert.True(army.Rally && !army.AllOut);
        Assert.False(world.Tribes[0].Army.Rally);
        Assert.Same(world.Tribes[0].Army, world.Orders);
        Assert.Equal(OrderOutcome.NotOffered, Order(game, new CancelArmyCall(0, ArmyCall.Rally)));
        int[] called = Mustered(world, 1);
        Assert.NotEmpty(called);
        for (int tick = 0; tick < 1200; tick++)
        {
            var orders = new List<GameOrder>();
            if (tick % 300 == 50)
            {
                orders.Add(new CallArmy(0, ArmyCall.AllOut, world.Tribes[0].Camp));
            }
            if (tick % 300 == 200)
            {
                orders.Add(new CancelArmyCall(0, ArmyCall.AllOut));
            }
            game.Step(new TickInput { Orders = orders.Select(PlayerOrder.From).ToArray() });
            Assert.True(army.Rally && !army.AllOut, $"tribe 1's call is still on at tick {tick}");
            Assert.Equal((rally.X, rally.Y), (army.PointX, army.PointY));
        }
        Unit[] answering = called.Select(world.Unit).Where(u => u.HitPoints > 0 && (u.Status & UnitStatus.Mustered) != 0).ToArray();
        Assert.NotEmpty(answering);
        Assert.True(answering.Count(u => Near(u, rally, 4) || (u.DestinationX, u.DestinationY) == (rally.X, rally.Y)) * 2 >= answering.Length,
            "most of tribe 1's army goes to its rally point");
        Assert.All(world.UnitsOf(0).Concat(world.UnitsOf(2)).Concat(world.UnitsOf(3)).Where(u => u.HitPoints > 0),
            u => Assert.False((u.DestinationX, u.DestinationY) == (rally.X, rally.Y) && (u.Status & UnitStatus.Mustered) != 0 && u.Tribe != 1));

        Assert.Equal(OrderOutcome.Applied, Order(game, new CancelArmyCall(1, ArmyCall.Rally)));
        Assert.False(army.Rally);
        Assert.All(world.UnitsOf(1), u => Assert.Equal(UnitStatus.None, u.Status & (UnitStatus.Mustered | UnitStatus.RallyPending)));
    }

    [Fact]
    public void AllOut_CallsTheWholeTribe_AndOnlyItsOwnerCancelsIt()
    {
        GameSimulation game = FourTribes(2);
        GameWorld world = game.World;
        Run(game, 100);
        GridPoint point = RallyPoint(world, 1);

        Assert.Equal(OrderOutcome.Applied, Order(game, new CallArmy(1, ArmyCall.AllOut, point)));

        Assert.All(world.UnitsOf(1).Where(u => u.HitPoints > 0), u => Assert.NotEqual(UnitStatus.None, u.Status & UnitStatus.Mustered));
        Assert.All(world.UnitsOf(0).Where(u => u.HitPoints > 0), u => Assert.Equal(UnitStatus.None, u.Status & UnitStatus.RallyPending));
        Assert.Equal(OrderOutcome.NotOffered, Order(game, new CancelArmyCall(0, ArmyCall.AllOut)));
        Assert.Equal(OrderOutcome.NotOffered, Order(game, new CallArmy(1, ArmyCall.Rally, point)));
        Assert.Equal(OrderOutcome.Applied, Order(game, new CallArmy(0, ArmyCall.AllOut, world.Tribes[0].Camp)));
        Run(game, 600);
        Assert.True(world.Tribes[1].Army.AllOut);
        Assert.Equal(OrderOutcome.Applied, Order(game, new CancelArmyCall(0, ArmyCall.AllOut)));
        Assert.True(world.Tribes[1].Army.AllOut, "tribe 0 going back to work leaves tribe 1 called out");
        Assert.Equal(OrderOutcome.Applied, Order(game, new CancelArmyCall(1, ArmyCall.AllOut)));
        Assert.False(world.Tribes[1].Army.AllOut);
    }

    [Fact]
    public void ACall_SurvivesEveryOtherTribesOrders()
    {
        GameSimulation game = FourTribes(4);
        GameWorld world = game.World;
        ArmTribe(world, 1);
        Run(game, 100);
        GridPoint rally = RallyPoint(world, 1);
        Assert.Equal(OrderOutcome.Applied, Order(game, new CallArmy(1, ArmyCall.Rally, rally)));
        var fuzzer = new OrderFuzzer(5);

        for (int tick = 0; tick < 1500; tick++)
        {
            TickInput input = fuzzer.Tick(game);
            game.Step(input with { Orders = input.Orders.Where(o => o.Tribe != 1).ToArray() });
            Assert.True(world.Tribes[1].Army.Rally && !world.Tribes[1].Army.AllOut);
            Assert.Equal((rally.X, rally.Y), (world.Tribes[1].Army.PointX, world.Tribes[1].Army.PointY));
        }
        Assert.Contains(world.UnitsOf(1), u => u.HitPoints > 0 && (u.Status & UnitStatus.Mustered) != 0);
    }

    /// <summary>Gives the adults of <paramref name="tribe"/> clubs and some fighting skill, so a rally (not only an all-out call) musters them.</summary>
    private static void ArmTribe(GameWorld world, int tribe)
    {
        foreach (Unit unit in world.UnitsOf(tribe).Where(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult))
        {
            (unit.Weapon, unit.MeleeSkill) = (0, Math.Max(unit.MeleeSkill, 6));
        }
    }

    private static GridPoint RallyPoint(GameWorld world, int tribe)
    {
        GridPoint camp = world.Tribes[tribe].Camp;
        for (int d = 6; d < 30; d++)
        {
            foreach (var cell in new[] { new GridPoint(camp.X + d, camp.Y), new GridPoint(camp.X, camp.Y + d), new GridPoint(camp.X - d, camp.Y) })
            {
                if (world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty)
                {
                    return cell;
                }
            }
        }
        return camp;
    }

    private static int[] Mustered(GameWorld world, int tribe) =>
        world.UnitsOf(tribe).Where(u => u.HitPoints > 0 && (u.Status & UnitStatus.Mustered) != 0).Select(u => u.Id).ToArray();

    private static bool Near(Unit unit, GridPoint cell, int cells) => Math.Max(Math.Abs(unit.X - cell.X), Math.Abs(unit.Y - cell.Y)) <= cells;

    private static void Run(GameSimulation game, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            game.Step();
        }
    }

    private static OrderOutcome Order(GameSimulation game, GameOrder order) =>
        game.Step(TickInput.WithOrders(order)).Outcomes.Single();
}
