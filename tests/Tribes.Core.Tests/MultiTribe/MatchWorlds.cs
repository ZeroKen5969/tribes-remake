using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>Matches for the multi-tribe tests, and made-up human players that drive them.</summary>
internal static class MatchWorlds
{
    /// <summary>The game's content.</summary>
    public static GameContent Content { get; } = GameContent.Load(Path.Combine(TestPaths.RepoRoot(), "data"));

    /// <summary>A setup of the given controllers on <paramref name="map"/>, every tribe on its own team unless <paramref name="teams"/> says.</summary>
    public static MatchSetup Setup(int map, long seed, IReadOnlyList<TribeController> controllers, IReadOnlyList<int>? teams = null) => new()
    {
        Map = map,
        Seed = seed,
        Difficulty = 1,
        Tribes = controllers.Select((c, i) => new MatchTribe(c, teams?[i] ?? TribeState.NoTeam)).ToArray(),
    };

    /// <summary><paramref name="humans"/> humans (slots 0..) and then <paramref name="computers"/> computer tribes (alternating difficulty).</summary>
    public static TribeController[] Controllers(int humans, int computers) =>
        Enumerable.Range(0, humans).Select(TribeController.Human)
            .Concat(Enumerable.Range(0, computers).Select(i => TribeController.Computer(i % 2)))
            .ToArray();

    /// <summary>
    /// The orders every present human player gives on tick <paramref name="tick"/>: a function of
    /// the tick, the tribe and the game's state only, so two games given them stay alike. Players
    /// send groups of units to cells (often towards an enemy camp), place buildings near their camp,
    /// call their army out and back, and appoint a chief when asked.
    /// </summary>
    public static TickInput Orders(GameSimulation game, long tick, int seed)
    {
        GameWorld world = game.World;
        var orders = new List<GameOrder>();
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.Kind == TribeControl.Human)
            {
                AddOrders(world, tribe.Index, new Random(unchecked((int)(tick * 7919) ^ (seed * 31) ^ (tribe.Index * 104729))), orders);
            }
        }
        return new TickInput { Orders = orders.Select(PlayerOrder.From).ToArray() };
    }

    private static void AddOrders(GameWorld world, int tribe, Random random, List<GameOrder> orders)
    {
        int roll = random.Next(1000);
        GridSize size = world.Map.PlayableSize;
        GridPoint camp = world.Tribes[tribe].Camp;
        if (TribeRoles.AwaitsNewChief(world, tribe) && LiveAdults(world, tribe).FirstOrDefault() is int adult and > 0)
        {
            orders.Add(new AppointChief(tribe, adult));
        }
        if (roll < 25)
        {
            int[] units = LiveAdults(world, tribe).Where(_ => random.Next(3) == 0).Take(5).ToArray();
            GridPoint target = random.Next(3) == 0 ? EnemyCamp(world, tribe, random) : Near(camp, random, size, 12);
            if (units.Length > 0)
            {
                orders.Add(new MoveUnits(tribe, new UnitSet(units), target, 0, random.Next(4) == 0));
            }
        }
        else if (roll < 35)
        {
            orders.Add(new PlaceBuilding(tribe, random.Next(12), Near(camp, random, size, 10)));
        }
        else if (roll < 37)
        {
            orders.Add(new CallArmy(tribe, random.Next(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut, EnemyCamp(world, tribe, random)));
        }
        else if (roll < 40)
        {
            orders.Add(new CancelArmyCall(tribe, random.Next(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut));
        }
        else if (roll < 60)
        {
            AddOtherOrder(world, tribe, random, orders, Near(camp, random, size, 10));
        }
    }

    /// <summary>One of the other orders a player gives: about its units, its buildings, spells and traps.</summary>
    private static void AddOtherOrder(GameWorld world, int tribe, Random random, List<GameOrder> orders, GridPoint cell)
    {
        int unit = LiveAdults(world, tribe).Skip(random.Next(8)).FirstOrDefault();
        int building = OwnBuildings(world, tribe).Skip(random.Next(4)).FirstOrDefault();
        if (unit == 0 || building == 0)
        {
            return;
        }
        int[] group = LiveAdults(world, tribe).Where(_ => random.Next(4) == 0).Take(3).DefaultIfEmpty(unit).ToArray();
        GameOrder order = random.Next(12) switch
        {
            0 => new TendNeed(tribe, unit, random.Next(2) == 0 ? UnitNeed.Food : UnitNeed.Rest),
            1 => new CallOutUnit(tribe, unit),
            2 => new OrderBuildingWork(tribe, building, (BuildingWork)random.Next(4)),
            3 => new SetArmouryOrder(tribe, building, random.Next(10), random.Next(4)),
            4 => new FetchWeapon(tribe, new UnitSet(group), building, random.Next(6), cell),
            5 => new DeliverToStore(tribe, new UnitSet(group), building, cell),
            6 => new CastSpell(tribe, building, random.Next(6), unit),
            7 => new CastSpellAt(tribe, building, random.Next(6), cell),
            8 => new PlaceTrap(tribe, building, CellCode.FirstTrapKind + random.Next(CellCode.TrapKinds), cell),
            9 => new EvictUnit(tribe, building, unit),
            10 => new SetHints(tribe, random.Next(2) == 0),
            _ => random.Next(20) == 0 ? new DemolishBuilding(tribe, building) : new ViewedHelp(tribe),
        };
        orders.Add(order);
    }

    private static IEnumerable<int> OwnBuildings(GameWorld world, int tribe)
    {
        return world.BuildingsOf(tribe).Where(building => building.HitPoints > 0).Select(building => building.Id);
    }

    private static GridPoint EnemyCamp(GameWorld world, int tribe, Random random)
    {
        int[] enemies = TribeRoles.EnemiesOf(world, tribe).ToArray();
        return enemies.Length == 0 ? world.Tribes[tribe].Camp : world.Tribes[enemies[random.Next(enemies.Length)]].Camp;
    }

    private static GridPoint Near(GridPoint centre, Random random, GridSize size, int radius) => new(
        Math.Clamp(centre.X + random.Next(-radius, radius + 1), 1, size.Width - 2),
        Math.Clamp(centre.Y + random.Next(-radius, radius + 1), 1, size.Height - 2));

    /// <summary>The living adults of <paramref name="tribe"/>.</summary>
    public static IEnumerable<int> LiveAdults(GameWorld world, int tribe)
    {
        return world.UnitsOf(tribe)
            .Where(unit => unit.HitPoints > 0 && unit.Age >= Core.Systems.Entities.UnitAges.Adult)
            .Select(unit => unit.Id);
    }
}
