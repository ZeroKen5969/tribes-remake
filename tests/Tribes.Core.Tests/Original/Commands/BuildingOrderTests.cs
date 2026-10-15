using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using static Tribes.Core.Tests.Commands.OrderWorld;

namespace Tribes.Core.Tests.Commands;

/// <summary>Edge cases of the orders about buildings.</summary>
public sealed class BuildingOrderTests
{
    private const int NetTrap = 20, SpringTrap = 21, Tomahawk = 5, Spear = 2;

    [Fact]
    public void PlaceBuilding_PlacesTheKindsSite_WhenTheMenuOffersIt()
    {
        var w = new OrderWorld();

        Assert.Equal(OrderOutcome.Applied, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Tower, new GridPoint(4, 4))));
        Building site = w.World.Building(1);
        Assert.Equal(w.World.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Tower].SiteKind, site.Kind);
        Assert.Equal(BuildingKindNumbers.Tower, site.Aux);

        Assert.Equal(OrderOutcome.Blocked, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Tower, new GridPoint(4, 4))));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Tower, new GridPoint(-1, 4))));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.ChiefsHut, new GridPoint(9, 9))));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceBuilding(Player, 12, new GridPoint(9, 9))));
    }

    [Fact]
    public void PlaceBuilding_InTheFirstMissions_OffersWhatTheStoryAllows()
    {
        var w = new OrderWorld(mission: 0);

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Armoury, new GridPoint(4, 4))));
        w.World.Script.SceneId = 18;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Armoury, new GridPoint(4, 4))));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceBuilding(Player, BuildingKindNumbers.Sanctuary, new GridPoint(9, 9))));
    }

    [Fact]
    public void PlaceTrap_OrdersATrapTheArmouryMakes()
    {
        var w = new OrderWorld();
        Building armoury = w.Building(BuildingKindNumbers.Armoury, 2, 2);
        Building kitchen = w.Building(BuildingKindNumbers.Kitchen, 10, 2);
        int queue = w.World.Tribes[Player].Armouries.ToList().FindIndex(q => q.Armoury == armoury.Id);
        var cell = new GridPoint(8, 9);

        Assert.Equal(OrderOutcome.Applied, w.Apply(new PlaceTrap(Player, armoury.Id, NetTrap, cell)));
        Assert.Equal(1, w.World.Tribes[Player].Armouries[queue].Pending(OrderMenus.FirstTrapProduct));
        Assert.Equal(CellContent.PendingTrap, w.World.Map.CellAt(cell).Content);

        Assert.Equal(OrderOutcome.Blocked, w.Apply(new PlaceTrap(Player, armoury.Id, NetTrap, cell)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceTrap(Player, armoury.Id, SpringTrap, new GridPoint(9, 9))));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new PlaceTrap(Player, armoury.Id, BuildingKindNumbers.Tower, new GridPoint(9, 9))));
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new PlaceTrap(Player, kitchen.Id, NetTrap, new GridPoint(9, 9))));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new PlaceTrap(Player, 31, NetTrap, new GridPoint(9, 9))));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new PlaceTrap(Player, armoury.Id, NetTrap, new GridPoint(9, 99))));

        w.World.Tribes[Player].Armouries[queue].Armoury = 0;
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new PlaceTrap(Player, armoury.Id, NetTrap, new GridPoint(9, 9))));
    }

    [Fact]
    public void Demolish_TearsDownAStandingBuildingOfTheTribe()
    {
        var w = new OrderWorld();
        Building hut = w.Building(BuildingKindNumbers.Hut, 2, 2);
        Building enemy = w.Building(BuildingKindNumbers.Hut, 10, 2, Computer);

        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new DemolishBuilding(Player, enemy.Id)));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new DemolishBuilding(Player, 0)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new DemolishBuilding(Player, hut.Id)));
        Assert.Equal(-1, hut.HitPoints);
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new DemolishBuilding(Player, hut.Id)));
    }

    [Fact]
    public void BuildingWork_FollowsWhatTheBuildingsMenuOffers()
    {
        var w = new OrderWorld();
        Building kitchen = w.Building(BuildingKindNumbers.Kitchen, 2, 2);

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.Upgrade)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.Repair)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.StopUpgrade)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.StopRepair)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, (BuildingWork)9)));

        w.World.SetUnlockLevel(BuildingKindNumbers.Kitchen, 2);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.Upgrade)));
        Assert.Equal(BuildingWorkMode.Upgrading, kitchen.WorkMode);
        Assert.Equal(w.World.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Kitchen].MaxHitPoints[1], kitchen.MaxHitPoints);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.Repair)));

        kitchen.HitPoints = 100;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.StopUpgrade)));
        Assert.Equal(w.World.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Kitchen].MaxHitPoints[0], kitchen.MaxHitPoints);
        Assert.Equal(kitchen.MaxHitPoints, kitchen.HitPoints);
        Assert.Equal(1, kitchen.AlertFlags & 1);

        kitchen.Level = 0;
        kitchen.AlertFlags = 0;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new OrderBuildingWork(Player, kitchen.Id, BuildingWork.StopUpgrade)));
        Assert.Equal(1, kitchen.AlertFlags);
    }

    [Fact]
    public void Repairs_StartWhenDamaged_AndCanBeCalledOff()
    {
        var w = new OrderWorld();
        Building hut = w.Building(BuildingKindNumbers.Hut, 2, 2);
        hut.HitPoints--;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new OrderBuildingWork(Player, hut.Id, BuildingWork.Repair)));
        Assert.Equal(BuildingWorkMode.Repairing, hut.WorkMode);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new OrderBuildingWork(Player, hut.Id, BuildingWork.Upgrade)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new OrderBuildingWork(Player, hut.Id, BuildingWork.StopRepair)));
        Assert.Equal(1, hut.AlertFlags & 1);
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new OrderBuildingWork(Player, 40, BuildingWork.Repair)));
    }

    [Fact]
    public void Evict_SendsOutTheOperatorOrAListedOccupant()
    {
        var w = new OrderWorld();
        Building kitchen = w.Building(BuildingKindNumbers.Kitchen, 2, 2);
        Unit cook = w.Unit(3, 20, 20);
        Unit guest = w.Unit(4, 21, 21);
        Unit idler = w.Unit(5, 22, 22);
        kitchen.OperatorOrBeds = cook.Id;
        kitchen.FirstOccupant = guest.Id;
        guest.NextOccupantOrCellObject = idler.Id;
        guest.ActionSubState = 1;

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new EvictUnit(Player, kitchen.Id, idler.Id)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new EvictUnit(Player, kitchen.Id, guest.Id)));
        Assert.NotEqual(OrderModes.Free, guest.OrderMode);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new EvictUnit(Player, kitchen.Id, cook.Id)));
        Assert.Equal(0, kitchen.OperatorOrBeds);
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new EvictUnit(Player, kitchen.Id, 70)));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new EvictUnit(Player, 31, guest.Id)));
    }

    [Fact]
    public void ArmouryOrders_SetTheCountOfAWeaponTheArmouryMakes()
    {
        var w = new OrderWorld();
        Building armoury = w.Building(BuildingKindNumbers.Armoury, 2, 2);
        int queue = w.World.Tribes[Player].Armouries.ToList().FindIndex(q => q.Armoury == armoury.Id);

        Assert.Equal(OrderOutcome.Applied, w.Apply(new SetArmouryOrder(Player, armoury.Id, Spear, OrderMenus.MaxArmouryCount)));
        Assert.Equal(OrderMenus.MaxArmouryCount, w.World.Tribes[Player].Armouries[queue].Pending(Spear));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new SetArmouryOrder(Player, armoury.Id, Spear, OrderMenus.MaxArmouryCount + 1)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new SetArmouryOrder(Player, armoury.Id, Spear, -1)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new SetArmouryOrder(Player, armoury.Id, Tomahawk, 1)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new SetArmouryOrder(Player, armoury.Id, OrderMenus.FirstTrapProduct, 1)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new SetArmouryOrder(Player, armoury.Id, -1, 1)));
        armoury.Level = 3;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new SetArmouryOrder(Player, armoury.Id, Tomahawk, 0)));

        w.World.Tribes[Player].Armouries[queue].Armoury = 0;
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new SetArmouryOrder(Player, armoury.Id, Spear, 1)));
        Building hut = w.Building(BuildingKindNumbers.Hut, 10, 10);
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new SetArmouryOrder(Player, hut.Id, Spear, 1)));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new SetArmouryOrder(Player, 29, Spear, 1)));
    }

    [Fact]
    public void Spells_AreOrderedAsTaskOrdersOfTheSanctuary()
    {
        var w = new OrderWorld();
        Building sanctuary = w.Building(BuildingKindNumbers.Sanctuary, 2, 2);
        Unit target = w.Unit(3, 20, 20);
        ListView<TaskOrder> tasks = w.World.Tribes[Player].TaskOrders;
        tasks[0].Building = 9;
        tasks[1].B = 33;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new CastSpell(Player, sanctuary.Id, 1, target.Id)));
        Assert.Equal((sanctuary.Id, 1, target.Id, 33), (tasks[1].Building, tasks[1].Kind, tasks[1].A, tasks[1].B));

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CastSpell(Player, sanctuary.Id, 2, target.Id)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CastSpellAt(Player, sanctuary.Id, OrderMenus.PlaceSpell, new GridPoint(5, 5))));
        sanctuary.Level = 2;
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CastSpell(Player, sanctuary.Id, OrderMenus.PlaceSpell, target.Id)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CastSpellAt(Player, sanctuary.Id, 0, new GridPoint(5, 5))));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new CastSpellAt(Player, sanctuary.Id, OrderMenus.PlaceSpell, new GridPoint(5, 50))));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new CastSpellAt(Player, sanctuary.Id, OrderMenus.PlaceSpell, new GridPoint(5, 6))));
        Assert.Equal((sanctuary.Id, 3, 5, 6), (tasks[2].Building, tasks[2].Kind, tasks[2].A, tasks[2].B));

        target.Age = 4;
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CastSpell(Player, sanctuary.Id, 0, target.Id)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new CastSpell(Player, sanctuary.Id, 0, 77)));
        Building hut = w.Building(BuildingKindNumbers.Hut, 10, 10);
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new CastSpell(Player, hut.Id, 0, target.Id)));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new CastSpellAt(Player, 50, 3, new GridPoint(5, 6))));
    }

    [Fact]
    public void Spells_WithEveryTaskOrderTaken_ReuseTheLastSlot()
    {
        var w = new OrderWorld();
        Building sanctuary = w.Building(BuildingKindNumbers.Sanctuary, 2, 2);
        Unit target = w.Unit(3, 20, 20);
        ListView<TaskOrder> tasks = w.World.Tribes[Player].TaskOrders;
        foreach (TaskOrder task in tasks)
        {
            task.Building = 7;
        }

        Assert.Equal(OrderOutcome.Applied, w.Apply(new CastSpell(Player, sanctuary.Id, 0, target.Id)));
        Assert.Equal(sanctuary.Id, tasks[^1].Building);
        Assert.Equal(7, tasks[^2].Building);
    }
}
