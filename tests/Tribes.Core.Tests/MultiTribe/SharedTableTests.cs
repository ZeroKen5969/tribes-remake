using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// The world-wide tables (construction sites waiting for material, upgrades and repairs, trap
/// orders, dust clouds, couples) grow in a match, so one tribe's entries never keep another tribe
/// from building, upgrading, setting traps or having children; the campaign keeps the original's.
/// </summary>
public sealed class SharedTableTests
{
    private const int Hut = BuildingKindNumbers.Hut, Store = BuildingKindNumbers.Store;

    /// <summary>
    /// The reported game: two humans and two computers. With the list of construction sites shared
    /// (20 entries for four tribes) the tribes' waiting sites filled it within about 3,700 ticks on
    /// this map, and from then on a human with a handful of buildings was told 「建築已達上限」.
    /// </summary>
    [Fact]
    public void TwoHumansAndTwoComputers_NoTribeIsEverToldItHasNoRoom()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 3, MatchWorlds.Controllers(2, 2)));
        GameWorld world = game.World;
        var refused = new List<NoticeEvent>();
        int mostSites = 0;
        for (int tick = 0; tick < 8000; tick++)
        {
            TickResult result = game.Step(MatchWorlds.Orders(game, game.Tick, 0));
            refused.AddRange(result.Events.OfType<NoticeEvent>().Where(n => n.Kind == EntityNotices.NoBuildingSlot));
            mostSites = System.Math.Max(mostSites, world.Construction.Count(j => j.Building >= 1));
        }
        Assert.Empty(refused);
        Assert.True(mostSites >= GameWorld.ConstructionSlots, $"the tribes had {mostSites} sites waiting at most");
    }

    [Fact]
    public void ComputersSites_DoNotCrowdOutAHumansSite()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 2, MatchWorlds.Controllers(2, 2)));
        GameWorld world = game.World;
        int site = world.Content.Rules.Buildings.Kinds[Store].SiteKind;
        List<Building> computers = SlotGrowthTests.PlaceSites(game, 2, Store, 15).Concat(SlotGrowthTests.PlaceSites(game, 3, Store, 15)).ToList();
        Assert.Equal(30, computers.Count);
        Assert.All(computers, b => Assert.Equal(site, b.Kind));

        List<Building> human = SlotGrowthTests.PlaceSites(game, 1, Store, 3);

        Assert.Equal(3, human.Count);
        Assert.True(world.Construction.Count >= 33);
        Assert.All(computers.Concat(human), b => Assert.Contains(world.Construction, job => job.Building == b.Id));
        Assert.DoesNotContain(game.Rules.PendingEvents.OfType<NoticeEvent>(), n => n.Kind == EntityNotices.NoBuildingSlot);
    }

    [Fact]
    public void UpgradesTrapsAndDust_HaveRoomForEveryTribe()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 2, MatchWorlds.Controllers(2, 2)));
        GameWorld world = game.World;
        List<Building> huts = SlotGrowthTests.PlaceBuildings(game, 2, Hut, 26);
        foreach (Building hut in huts)
        {
            game.Rules.StartBuildingWork(hut.Id, upgrade: true);
        }
        Assert.Equal(26, world.Upgrades.Count(j => j.Building >= 1));

        int trapOrders = world.Traps.Count + 5;
        int[] slots = Enumerable.Range(0, trapOrders).Select(_ =>
        {
            int slot = world.FreeTrapOrderSlot();
            world.Traps[slot].OwnerBuilding = huts[0].Id;
            return slot;
        }).ToArray();
        Assert.Equal(trapOrders, slots.Distinct().Count());

        List<Building> doomed = SlotGrowthTests.PlaceBuildings(game, 3, Hut, GameWorld.DustSlots + 6);
        foreach (Building building in doomed)
        {
            game.Rules.Demolish(building.Id);
        }
        Assert.Equal(doomed.Count, world.Dust.Count(d => d.Building != 0));
        for (int frame = 0; frame <= Core.Systems.WorldObjects.BuildingWorkLists.DustFrames; frame++)
        {
            game.Rules.BuildingWorkLists.AdvanceDust(game.Rules.BuildingLifecycle.Free);
        }
        Assert.All(doomed, b => Assert.Equal(0, b.HitPoints));
    }

    [Fact]
    public void Couples_GrowPastTheOriginalLinks_InAMatch()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 2, MatchWorlds.Controllers(1, 1)));
        GameWorld world = game.World;
        int[] slots = Enumerable.Range(0, GameWorld.MostCoupleSlots + 10).Select(_ =>
        {
            int slot = world.FreeCoupleSlot();
            world.Couples[slot].State = 1;
            return slot;
        }).ToArray();
        Unit unit = world.UnitsOf(0).First(u => u.HitPoints > 0);

        unit.PairLink = (slots[^1] + 1) << 1;

        Assert.Equal(slots.Length, slots.Distinct().Count());
        Assert.Equal(slots[^1], CoupleLinks.CoupleSlot(unit));
    }

    [Fact]
    public void Campaign_KeepsTheOriginalTables()
    {
        GameSimulation game = SlotGrowthTests.Campaign();
        GameWorld world = game.World;
        Assert.Equal((GameWorld.DustSlots, GameWorld.TrapSlots, GameWorld.ConstructionSlots, GameWorld.UpgradeSlots, GameWorld.CoupleSlots),
            (world.Dust.Count, world.Traps.Count, world.Construction.Count, world.Upgrades.Count, world.Couples.Count));
        int waiting = world.Construction.Count(j => j.Building >= 1);

        List<Building> sites = SlotGrowthTests.PlaceSites(game, 0, Store, GameWorld.ConstructionSlots + 3);

        Assert.Equal(GameWorld.ConstructionSlots - waiting, sites.Count);
        Assert.Equal(GameWorld.ConstructionSlots, world.Construction.Count);
        Assert.Contains(game.Rules.PendingEvents.OfType<NoticeEvent>(), n => n.Kind == EntityNotices.NoBuildingSlot);
        foreach (Couple couple in world.Couples)
        {
            couple.State = 1;
        }
        Assert.Equal(-1, world.FreeCoupleSlot());
        Assert.Equal(GameWorld.CoupleSlots, world.Couples.Count);
    }
}
