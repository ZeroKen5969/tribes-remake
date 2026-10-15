using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// Matches have no limit on units and buildings: a tribe gets new slots as it needs them, numbered
/// after every slot there is, while the campaign keeps the original's 50 units and 30 buildings.
/// </summary>
public sealed class SlotGrowthTests
{
    private const int Hut = BuildingKindNumbers.Hut, Armoury = BuildingKindNumbers.Armoury, Store = BuildingKindNumbers.Store;

    [Fact]
    public void Match_TribeGetsAsManyUnitsAndBuildingsAsItNeeds()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 4, MatchWorlds.Controllers(2, 1)));
        GameWorld world = game.World;
        Dictionary<int, int> others = OwnersOfOtherTribes(world, 1);
        Assert.Null(world.UnitLimit);
        Assert.Null(world.BuildingLimit);
        Assert.True(world.GrowsOnDemand);

        List<Unit> born = SpawnUnits(game, 1, 80);
        List<Building> built = PlaceBuildings(game, 1, Hut, 45);

        Assert.Equal(80, born.Count);
        Assert.Equal(45, built.Count);
        Assert.True(world.UnitsOf(1).Count(u => u.HitPoints > 0) > 80);
        Assert.True(world.BuildingsOf(1).Count(b => b.HitPoints > 0) > 45);
        Assert.All(born, unit => Assert.Equal(1, world.TribeOfUnit(unit.Id)));
        Assert.All(built, building => Assert.Equal(1, world.TribeOfBuilding(building.Id)));
        Assert.Equal(others, OwnersOfOtherTribes(world, 1));
        AssertListsInSlotOrder(world);
        Assert.Equal(world.UnitsOf(1)[0].Id, world.FirstUnitSlot(1));
        Assert.Equal(world.BuildingsOf(1)[0].Id, world.FirstBuildingSlot(1));
        Assert.True(world.Tribes[1][TribeCounter.LivingSpace] > BuildingPlacement.MaxLivingSpace, "huts give room without the campaign's 50");
    }

    [Fact]
    public void Match_FreedSlotsAreUsedFirst_AndSlotsKeepTheirNumbers()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 2, MatchWorlds.Controllers(1, 1)));
        GameWorld world = game.World;
        List<Unit> born = SpawnUnits(game, 0, 60);
        int slots = world.UnitSlots;
        Unit freed = born[10];
        game.Rules.UnitLifecycle.Kill(freed);
        game.Rules.UnitLifecycle.FreeSlot(freed);

        Unit next = SpawnUnits(game, 0, 1).Single();

        Assert.Same(freed, next);
        Assert.Equal(slots, world.UnitSlots);
        Assert.Equal(0, world.TribeOfUnit(next.Id));
    }

    [Fact]
    public void Campaign_KeepsTheOriginalSlotsAndTheirLimits()
    {
        GameSimulation game = Campaign();
        GameWorld world = game.World;

        List<Unit> born = SpawnUnits(game, 0, 60);
        List<Building> built = PlaceBuildings(game, 0, Hut, 40);
        NoticeEvent[] notices = game.Rules.PendingEvents.OfType<NoticeEvent>().ToArray();

        Assert.False(world.GrowsOnDemand);
        Assert.Equal((50, 30), (world.UnitLimit, world.BuildingLimit));
        Assert.Equal((100, 60), (world.UnitSlots, world.BuildingSlots));
        Assert.Equal(50, world.UnitsOf(0).Count(u => u.HitPoints > 0));
        Assert.True(born.Count < 60);
        Assert.Equal(30, world.BuildingsOf(0).Count(b => b.HitPoints != 0));
        Assert.True(built.Count < 40);
        Assert.Contains(notices, n => n.Kind == EntityNotices.NoBuildingSlot);
        Assert.Equal(BuildingPlacement.MaxLivingSpace, world.Tribes[0][TribeCounter.LivingSpace]);
        Assert.Equal(Enumerable.Range(1, 50), world.UnitsOf(0).Select(u => u.Id));
        Assert.Equal(Enumerable.Range(51, 50), world.UnitsOf(1).Select(u => u.Id));
        Assert.Equal(Enumerable.Range(31, 30), world.BuildingsOf(1).Select(b => b.Id));
    }

    [Fact]
    public void CustomGame_WithoutLimits_GrowsLikeAMatch_AndSavesTheFlag()
    {
        GameSimulation game = GameSimulation.StartMission(MatchWorlds.Content, 8, 0, new Core.Systems.Missions.MissionOptions { WithoutLimits = true });
        GameWorld world = game.World;
        Assert.Equal(WorldMode.Campaign, world.Mode);
        Assert.True(world.GrowsOnDemand);
        Assert.Null(world.UnitLimit);

        List<Unit> born = SpawnUnits(game, 0, 60);
        List<Building> built = PlaceBuildings(game, 0, Hut, 35);
        List<Building> sites = PlaceSites(game, 0, Store, GameWorld.ConstructionSlots + 5);

        Assert.Equal((60, 35, GameWorld.ConstructionSlots + 5), (born.Count, built.Count, sites.Count));
        Assert.True(world.UnitsOf(0).Count(u => u.HitPoints > 0) > 60);
        Assert.True(world.BuildingsOf(0).Count(b => b.HitPoints != 0) > 50);
        Assert.True(world.Construction.Count(j => j.Building >= 1) > GameWorld.ConstructionSlots);
        Assert.DoesNotContain(game.Rules.PendingEvents.OfType<NoticeEvent>(), n => n.Kind == EntityNotices.NoBuildingSlot);
        AssertListsInSlotOrder(world);
        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
        Assert.True(loaded.World.GrowsOnDemand);
        Assert.Equal(game.StateHash(), loaded.StateHash());
        for (int tick = 0; tick < 200; tick++)
        {
            game.Step();
            loaded.Step();
        }
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.False(GameSimulation.Load(MatchWorlds.Content, GameSimulation.StartMission(MatchWorlds.Content, 8, 0).Save()).World.GrowsOnDemand);
    }

    [Fact]
    public void Match_SaveAndLoad_KeepsGrownSlotsAndTables()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 6, MatchWorlds.Controllers(1, 2)));
        SpawnUnits(game, 0, 70);
        PlaceBuildings(game, 2, Armoury, 14);
        for (int tick = 0; tick < 300; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 6));
        }

        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());

        Assert.Equal(game.StateHash(), loaded.StateHash());
        for (int t = 0; t < game.World.Tribes.Count; t++)
        {
            Assert.Equal(game.World.UnitsOf(t).Select(u => u.Id), loaded.World.UnitsOf(t).Select(u => u.Id));
            Assert.Equal(game.World.BuildingsOf(t).Select(b => b.Id), loaded.World.BuildingsOf(t).Select(b => b.Id));
        }
        Assert.Equal(game.World.Tribes[2].Armouries.Count, loaded.World.Tribes[2].Armouries.Count);
        Assert.True(loaded.World.GrowsOnDemand);
        for (int tick = 0; tick < 300; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 7));
            loaded.Step(MatchWorlds.Orders(loaded, loaded.Tick, 7));
        }
        Assert.Equal(game.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void Match_ArmouriesAndTaskOrdersAreNotLimited()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 3, MatchWorlds.Controllers(1, 1)));
        GameWorld world = game.World;

        List<Building> armouries = PlaceBuildings(game, 0, Armoury, TribeState.ArmourySlots + 4);
        TaskOrder[] orders = Enumerable.Range(0, TribeState.TaskOrderSlots + 5)
            .Select(_ => world.Tribes[0].FreeTaskOrder(world.GrowsOnDemand))
            .Select(order => { order.Building = armouries[0].Id; return order; })
            .ToArray();

        Assert.Equal(TribeState.ArmourySlots + 4, armouries.Count);
        Assert.All(armouries, a => Assert.Contains(world.Tribes[0].Armouries, q => q.Armoury == a.Id));
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void Campaign_ArmouriesAndTaskOrdersKeepTheOriginalTables()
    {
        GameSimulation game = Campaign();
        GameWorld world = game.World;
        int taken = world.Tribes[0].Armouries.Count(q => q.Armoury != 0);

        List<Building> armouries = PlaceBuildings(game, 0, Armoury, TribeState.ArmourySlots + 4);
        TaskOrder last = world.Tribes[0].TaskOrders[^1];
        foreach (TaskOrder order in world.Tribes[0].TaskOrders)
        {
            order.Building = 1;
        }

        Assert.Equal(TribeState.ArmourySlots - taken, armouries.Count);
        Assert.Equal(TribeState.ArmourySlots, world.Tribes[0].Armouries.Count);
        Assert.Same(last, world.Tribes[0].FreeTaskOrder(world.GrowsOnDemand));
        Assert.Equal(TribeState.TaskOrderSlots, world.Tribes[0].TaskOrders.Count);
    }

    /// <summary>A campaign mission a few ticks in (notices are not raised on its first frame).</summary>
    internal static GameSimulation Campaign()
    {
        GameSimulation game = GameSimulation.StartMission(MatchWorlds.Content, 2, 1);
        for (int tick = 0; tick < 20; tick++)
        {
            game.Step(TickInput.WithOrders(new Core.Commands.AdvanceScene(0)));
        }
        return game;
    }

    /// <summary>Brings <paramref name="count"/> adults of <paramref name="tribe"/> into the world near its camp (as many as fit).</summary>
    internal static List<Unit> SpawnUnits(GameSimulation game, int tribe, int count)
    {
        var born = new List<Unit>();
        GridPoint camp = Camp(game.World, tribe);
        foreach (GridPoint cell in EmptyCells(game.World, camp).Take(count))
        {
            if (game.Rules.UnitSpawning.Spawn(tribe, cell, UnitAges.Adult, UnitTraits.None) is { } unit)
            {
                born.Add(unit);
            }
        }
        return born;
    }

    /// <summary>Places up to <paramref name="count"/> buildings of <paramref name="kind"/> for <paramref name="tribe"/> where they fit.</summary>
    internal static List<Building> PlaceBuildings(GameSimulation game, int tribe, int kind, int count)
    {
        GameWorld world = game.World;
        var built = new List<Building>();
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
        GridRect bounds = world.Map.Bounds;
        for (int y = bounds.Top + 1; y < bounds.Bottom - definition.Height && built.Count < count; y += definition.Height + 1)
        {
            for (int x = bounds.Left + 1; x < bounds.Right - definition.Width && built.Count < count; x += definition.Width + 1)
            {
                var cell = new GridPoint(x, y);
                if (!game.Rules.BuildingPlacement.FootprintFree(kind, cell, inView: false, tribe))
                {
                    continue;
                }
                PlacementResult result = game.Rules.BuildingPlacement.Place(new PlacementRequest(tribe, kind, cell, 1, Immediate: true));
                if (result.Outcome == PlacementOutcome.Placed)
                {
                    built.Add(world.Building(result.Slot));
                }
            }
        }
        return built;
    }

    /// <summary>Puts up to <paramref name="count"/> construction sites of buildings of <paramref name="kind"/> for <paramref name="tribe"/> where they fit.</summary>
    internal static List<Building> PlaceSites(GameSimulation game, int tribe, int kind, int count)
    {
        GameWorld world = game.World;
        var placed = new List<Building>();
        int site = world.Content.Rules.Buildings.Kinds[kind].SiteKind;
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[site];
        GridRect bounds = world.Map.Bounds;
        for (int y = bounds.Top + 1; y < bounds.Bottom - definition.Height && placed.Count < count; y += definition.Height + 1)
        {
            for (int x = bounds.Left + 1; x < bounds.Right - definition.Width && placed.Count < count; x += definition.Width + 1)
            {
                var cell = new GridPoint(x, y);
                if (!game.Rules.BuildingPlacement.FootprintFree(site, cell, inView: false, tribe))
                {
                    continue;
                }
                var request = new PlacementRequest(tribe, site, cell, BuildingPlacement.SiteLevel, Immediate: true) { SiteTarget = kind };
                PlacementResult result = game.Rules.BuildingPlacement.Place(request);
                if (result.Outcome == PlacementOutcome.Placed)
                {
                    placed.Add(world.Building(result.Slot));
                }
            }
        }
        return placed;
    }

    /// <summary>Checks that every tribe's lists hold its own slots in slot order, numbered by their place, and that every slot is listed once.</summary>
    internal static void AssertListsInSlotOrder(GameWorld world)
    {
        var units = new HashSet<int>();
        var buildings = new HashSet<int>();
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            ListView<Unit> tribeUnits = world.UnitsOf(t);
            for (int i = 0; i < tribeUnits.Count; i++)
            {
                Assert.Equal((t, i), (tribeUnits[i].Tribe, tribeUnits[i].SlotInTribe));
                Assert.True(i == 0 || tribeUnits[i - 1].Id < tribeUnits[i].Id);
                Assert.True(units.Add(tribeUnits[i].Id));
            }
            ListView<Building> tribeBuildings = world.BuildingsOf(t);
            for (int i = 0; i < tribeBuildings.Count; i++)
            {
                Assert.Equal((t, i), (tribeBuildings[i].Tribe, tribeBuildings[i].SlotInTribe));
                Assert.True(i == 0 || tribeBuildings[i - 1].Id < tribeBuildings[i].Id);
                Assert.True(buildings.Add(tribeBuildings[i].Id));
            }
        }
        Assert.Equal(world.UnitSlots, units.Count);
        Assert.Equal(world.BuildingSlots, buildings.Count);
    }

    private static Dictionary<int, int> OwnersOfOtherTribes(GameWorld world, int tribe) =>
        Enumerable.Range(1, world.UnitSlots).Where(id => world.TribeOfUnit(id) != tribe).ToDictionary(id => id, world.TribeOfUnit);

    private static GridPoint Camp(GameWorld world, int tribe) =>
        world.Mode == WorldMode.Match ? world.Tribes[tribe].Camp : new GridPoint(world.Unit(world.FirstUnitSlot(tribe)).X, world.Unit(world.FirstUnitSlot(tribe)).Y);

    private static IEnumerable<GridPoint> EmptyCells(GameWorld world, GridPoint centre)
    {
        GridRect bounds = world.Map.Bounds;
        for (int radius = 1; radius < 96; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var cell = new GridPoint(centre.X + dx, centre.Y + dy);
                    if ((System.Math.Abs(dx) == radius || System.Math.Abs(dy) == radius) && bounds.Contains(cell)
                        && world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty)
                    {
                        yield return cell;
                    }
                }
            }
        }
    }
}
