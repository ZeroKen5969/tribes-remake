using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using static Tribes.Core.Tests.Commands.OrderWorld;

namespace Tribes.Core.Tests.Commands;

/// <summary>Edge cases of the orders given to units.</summary>
public sealed class UnitOrderTests
{
    private static readonly GridPoint Far = new(20, 20);

    [Fact]
    public void MoveUnits_SetsDestinationTargetAndWalkMode()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        unit.Status = UnitStatus.RallyPending | UnitStatus.Hidden;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 77, false)));

        Assert.Equal((Far.X, Far.Y), (unit.DestinationX, unit.DestinationY));
        Assert.Equal(CellCode.Empty, unit.TargetCellCode);
        Assert.Equal(OrderModes.Walk, unit.OrderMode);
        Assert.Equal(UnitStatus.Hidden, unit.Status);
    }

    [Fact]
    public void MoveUnits_KeepsALivingCreatureAsTarget()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        w.Unit(60, 30, 30);
        w.World.Animal(2).HitPoints = 5;

        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 60, false));
        Assert.Equal(60, unit.TargetCellCode);
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, CellCode.FirstAnimal + 2, false));
        Assert.Equal(CellCode.FirstAnimal + 2, unit.TargetCellCode);
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(30, 30), CellCode.FirstAnimal + 3, false));
        Assert.Equal(60, unit.TargetCellCode);
    }

    [Fact]
    public void MoveUnits_RefusesForeignUnitsEmptySetsAndCellsOffTheMap()
    {
        var w = new OrderWorld();
        w.Unit(3, 5, 5);
        w.Unit(60, 6, 6);

        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new MoveUnits(Player, UnitSet.Of(3, 60), Far, 0, false)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new MoveUnits(Player, UnitSet.Of(), Far, 0, false)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new MoveUnits(Player, UnitSet.Of(0), Far, 0, false)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new MoveUnits(Player, UnitSet.Of(101), Far, 0, false)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new MoveUnits(Player, null!, Far, 0, false)));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(40, 0), 0, false)));
        Assert.Equal(OrderOutcome.UnknownTribe, w.Apply(new MoveUnits(5, UnitSet.Of(3), Far, 0, false)));
        Assert.Equal(OrderOutcome.UnknownTribe, w.Apply(new MoveUnits(-1, UnitSet.Of(3), Far, 0, false)));
        Assert.Equal((5, 5), (w.World.Unit(3).DestinationX, w.World.Unit(3).DestinationY));
    }

    [Fact]
    public void MoveUnits_SkipsDeadUnits()
    {
        var w = new OrderWorld();
        Unit alive = w.Unit(3, 5, 5);
        Unit dead = w.Unit(4, 6, 6);
        dead.HitPoints = 0;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new MoveUnits(Player, UnitSet.Of(4, 3), Far, 0, false)));
        Assert.Equal(Far.X, alive.DestinationX);
        Assert.Equal(6, dead.DestinationX);
    }

    [Fact]
    public void MoveUnits_UnhappyUnitsRefuseUnlessCalledToArms()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        unit.Happiness = 9;

        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 0, false));
        Assert.Equal(5, unit.DestinationX);
        Assert.NotEqual(HintState.Free, w.World.Hints.Cooldown(w.World.Content.Rules.Story.Hints[12].Channel));

        w.World.Orders.Rally = true;
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 0, false));
        Assert.Equal(5, unit.DestinationX);
        unit.Status = UnitStatus.Mustered;
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 0, false));
        Assert.Equal(Far.X, unit.DestinationX);

        w.World.Orders.Rally = false;
        w.World.Orders.AllOut = true;
        unit.Status = UnitStatus.None;
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(9, 9), 0, false));
        Assert.Equal(9, unit.DestinationX);
    }

    [Fact]
    public void MoveUnits_TheComputersUnitsIgnoreThePlayersCalls()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(60, 5, 5);
        unit.Happiness = 0;
        w.World.Orders.AllOut = true;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new MoveUnits(Computer, UnitSet.Of(60), Far, 0, false)));
        Assert.Equal(5, unit.DestinationX);
    }

    [Fact]
    public void MoveUnits_ToTheTribesOwnStoreDoesNothing()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        Building store = w.Building(BuildingKindNumbers.Store, 10, 10);

        Assert.Equal(OrderOutcome.Applied, w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(11, 11), -store.Id, false)));
        Assert.Equal(5, unit.DestinationX);
        Building enemyStore = w.Building(BuildingKindNumbers.Store, 20, 10, Computer);
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(21, 11), -enemyStore.Id, false));
        Assert.Equal((21, -enemyStore.Id), (unit.DestinationX, unit.TargetCellCode));
    }

    [Fact]
    public void MoveUnits_RunsWhenHurriedOrSentTwiceAndWalksOtherwise()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        SpeedGait gait = w.World.Content.Rules.Movement.SpeedClasses[unit.MoveDelay].Straight;
        unit.StepAndActionTicks = 0x32;

        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 0, true));
        Assert.Equal(3, unit.RunBonus);
        Assert.Equal(0x30 + gait.RunStartTicks[2], unit.StepAndActionTicks);

        unit.StepAndActionTicks = 0x31;
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(9, 9), 0, false));
        Assert.Equal(0, unit.RunBonus);
        Assert.Equal(0x30 + gait.RunStopTicks[1], unit.StepAndActionTicks);

        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(9, 9), 0, false));
        Assert.Equal(3, unit.RunBonus);
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(9, 9), 0, true));
        Assert.Equal(3, unit.RunBonus);
    }

    [Fact]
    public void MoveUnits_ChildrenAndCarriersDoNotRun_AndOddSpeedsKeepTheirTicks()
    {
        var w = new OrderWorld();
        Unit child = w.Unit(3, 5, 5);
        child.Age = 5;
        w.Apply(new MoveUnits(Player, UnitSet.Of(3), Far, 0, true));
        Assert.Equal(0, child.RunBonus);

        Unit odd = w.Unit(4, 6, 6);
        odd.MoveDelay = 2;
        odd.StepAndActionTicks = 0x23;
        w.Apply(new MoveUnits(Player, UnitSet.Of(4), Far, 0, true));
        Assert.Equal((3, 0x23), (odd.RunBonus, odd.StepAndActionTicks));
        odd.MoveDelay = 40;
        w.Apply(new MoveUnits(Player, UnitSet.Of(4), new GridPoint(1, 1), 0, false));
        Assert.Equal((0, 0x23), (odd.RunBonus, odd.StepAndActionTicks));
    }

    [Fact]
    public void FetchWeapon_SendsUnitsToTheStore_WhenTheWeaponIsInStock()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);
        Building store = w.Building(BuildingKindNumbers.Store, 10, 10);
        var cell = new GridPoint(12, 12);

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 2, cell)));
        w.World.Tribes[Player][TribeCounter.Spears] = 1;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 2, cell)));
        Assert.Equal((12, 12, 46, OrderModes.Forced), (unit.DestinationX, unit.DestinationY, unit.Action, unit.OrderMode));

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 1, cell)));
        w.World.Tribes[Player][TribeCounter.Stones] = 5;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 1, cell)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 6, cell)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, -1, cell)));
    }

    [Fact]
    public void StoreErrands_CheckUnitsStoreAndCell()
    {
        var w = new OrderWorld();
        w.Unit(3, 5, 5);
        w.World.Tribes[Player][TribeCounter.Clubs] = 3;
        Building store = w.Building(BuildingKindNumbers.Store, 10, 10);
        Building kitchen = w.Building(BuildingKindNumbers.Kitchen, 20, 20);

        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new FetchWeapon(Player, UnitSet.Of(70), store.Id, 0, new GridPoint(10, 10))));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), 0, 0, new GridPoint(10, 10))));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), 61, 0, new GridPoint(10, 10))));
        Assert.Equal(OrderOutcome.WrongBuilding, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), kitchen.Id, 0, new GridPoint(20, 20))));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new FetchWeapon(Player, UnitSet.Of(3), store.Id, 0, new GridPoint(13, 10))));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new DeliverToStore(Player, UnitSet.Of(3), store.Id, new GridPoint(10, 9))));
        Assert.Equal(OrderOutcome.UnknownBuilding, w.Apply(new DeliverToStore(Player, UnitSet.Of(3), 30, new GridPoint(10, 10))));
    }

    [Fact]
    public void DeliverToStore_NeedsSomeoneCarryingAndSendsAll()
    {
        var w = new OrderWorld();
        Unit carrier = w.Unit(3, 5, 5);
        Unit empty = w.Unit(4, 6, 6);
        Building store = w.Building(BuildingKindNumbers.Store, 10, 10);
        var cell = new GridPoint(10, 11);

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new DeliverToStore(Player, UnitSet.Of(3, 4), store.Id, cell)));
        carrier.CarriedAmount = 4;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new DeliverToStore(Player, UnitSet.Of(3, 4), store.Id, cell)));
        Assert.All(new[] { carrier, empty }, u => Assert.Equal((10, 11, 73, OrderModes.Walk), (u.DestinationX, u.DestinationY, u.Action, u.OrderMode)));
    }

    [Fact]
    public void TendNeed_HeadsForTheNearestKitchenOrHut()
    {
        var w = new OrderWorld();
        Unit unit = w.Unit(3, 5, 5);

        Assert.Equal(OrderOutcome.Applied, w.Apply(new TendNeed(Player, 3, UnitNeed.Food)));
        Assert.Equal(OrderModes.Free, unit.OrderMode);
        w.Building(BuildingKindNumbers.Hut, 10, 3);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new TendNeed(Player, 3, UnitNeed.Rest)));
        Assert.Equal((OrderModes.Walk, 10, 4), (unit.OrderMode, unit.DestinationX, unit.DestinationY));

        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new TendNeed(Player, 3, (UnitNeed)7)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new TendNeed(Player, 4, UnitNeed.Food)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new TendNeed(Computer, 3, UnitNeed.Food)));
    }

    [Fact]
    public void CallOutUnit_BringsAUnitOutOfItsBuilding()
    {
        var w = new OrderWorld();
        Unit outside = w.Unit(3, 5, 5);
        Unit inside = w.Unit(4, 6, 6);
        Building hut = w.Building(BuildingKindNumbers.Kitchen, 10, 10);
        w.World.Map.SetCell(new GridPoint(6, 6), MapCell.Empty);
        inside.TargetIndex = hut.Id;
        inside.Action = ActionNumbers.Idle;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new CallOutUnit(Player, 3)));
        Assert.Equal(ActionNumbers.Idle, outside.Action);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new CallOutUnit(Player, 4)));
        Assert.NotEqual(OrderModes.Free, inside.OrderMode);

        inside.TargetIndex = 99;
        inside.OrderMode = OrderModes.Free;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new CallOutUnit(Player, 4)));
        Assert.Equal(OrderModes.Free, inside.OrderMode);
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new CallOutUnit(Player, 5)));
    }

    [Fact]
    public void AppointChief_OnlyWhileANewChiefIsAwaited_AndOnlyAnAdult()
    {
        var w = new OrderWorld();
        Unit adult = w.Unit(3, 5, 5);
        Unit child = w.Unit(4, 6, 6);
        child.Age = 3;
        adult.Home = 0;

        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new AppointChief(Player, 3)));
        w.World.Script.AwaitingNewChief = true;
        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new AppointChief(Computer, 60)));
        Assert.Equal(OrderOutcome.UnknownUnit, w.Apply(new AppointChief(Player, 60)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new AppointChief(Player, 4)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new AppointChief(Player, 3)));
        Assert.Equal(3, w.World.Tribes[Player][TribeCounter.Chief]);
        Assert.False(w.World.Script.AwaitingNewChief);
    }
}
