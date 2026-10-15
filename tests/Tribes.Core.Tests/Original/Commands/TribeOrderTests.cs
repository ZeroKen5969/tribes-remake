using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using static Tribes.Core.Tests.Commands.OrderWorld;

namespace Tribes.Core.Tests.Commands;

/// <summary>Edge cases of the tribe-wide orders, the processor and the menus.</summary>
public sealed class TribeOrderTests
{
    private static readonly GridPoint Point = new(12, 13);

    [Fact]
    public void Rally_MustersTheFighters_AndCanBeCalledOff()
    {
        var w = new OrderWorld();
        Unit fighter = w.Unit(3, 5, 5);
        fighter.Weapon = WeaponNumbers.Club;
        fighter.MeleeSkill = 6;
        Unit idle = w.Unit(4, 6, 6);
        idle.OrderMode = OrderModes.Free;
        idle.Status = UnitStatus.Mustered;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new CallArmy(Player, ArmyCall.Rally, Point)));
        ArmyOrders army = w.World.Orders;
        Assert.True(army.Rally);
        Assert.Equal((12, 13), (army.PointX, army.PointY));
        Assert.Equal(UnitStatus.RallyPending | UnitStatus.Mustered, fighter.Status);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CallArmy(Player, ArmyCall.Rally, Point)));

        idle.DestinationX = 30;
        Assert.Equal(OrderOutcome.Applied, w.Apply(new CancelArmyCall(Player, ArmyCall.Rally)));
        Assert.False(army.Rally);
        Assert.Equal(UnitStatus.None, fighter.Status);
        Assert.Equal(UnitStatus.None, idle.Status);
        Assert.Equal(6, idle.DestinationX);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CancelArmyCall(Player, ArmyCall.Rally)));
    }

    [Fact]
    public void AllOut_ReplacesARally_AndOnlyItsCancelIsOffered()
    {
        var w = new OrderWorld();
        Unit anyone = w.Unit(3, 5, 5);
        w.World.Orders.Rally = true;

        Assert.Equal(OrderOutcome.Applied, w.Apply(new CallArmy(Player, ArmyCall.AllOut, Point)));
        Assert.True(w.World.Orders.AllOut);
        Assert.False(w.World.Orders.Rally);
        Assert.Equal(UnitStatus.RallyPending | UnitStatus.Mustered, anyone.Status);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CallArmy(Player, ArmyCall.AllOut, Point)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CallArmy(Player, ArmyCall.Rally, Point)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CancelArmyCall(Player, ArmyCall.Rally)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new CancelArmyCall(Player, ArmyCall.AllOut)));
        Assert.False(w.World.Orders.AllOut);
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CancelArmyCall(Player, ArmyCall.AllOut)));
    }

    [Fact]
    public void ArmyCalls_CheckTribePointAndCall()
    {
        var w = new OrderWorld();

        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new CallArmy(Computer, ArmyCall.Rally, Point)));
        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new CancelArmyCall(Computer, ArmyCall.Rally)));
        Assert.Equal(OrderOutcome.OutOfMap, w.Apply(new CallArmy(Player, ArmyCall.AllOut, new GridPoint(0, 32))));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CallArmy(Player, (ArmyCall)5, Point)));
        Assert.Equal(OrderOutcome.NotOffered, w.Apply(new CancelArmyCall(Player, (ArmyCall)5)));
        Assert.False(w.World.Orders.AllOut);
    }

    [Fact]
    public void SetHints_SwitchesThePlayersHints()
    {
        var w = new OrderWorld();

        Assert.Equal(OrderOutcome.Applied, w.Apply(new SetHints(Player, false)));
        Assert.False(w.World.Hints.Enabled);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new SetHints(Player, true)));
        Assert.True(w.World.Hints.Enabled);
        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new SetHints(Computer, false)));
        Assert.True(w.World.Hints.Enabled);
    }

    [Fact]
    public void ViewedHelp_TriggersATutorialStepWaitingForIt()
    {
        var w = new OrderWorld(mission: 0);
        int step = w.World.Content.Rules.Story.TutorialSteps.ToList().FindIndex(s => s.Trigger == 3);
        w.World.Script.TutorialObjective = step;

        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new ViewedHelp(Computer)));
        Assert.Equal(0, w.World.Script.TutorialTimer);
        Assert.Equal(OrderOutcome.Applied, w.Apply(new ViewedHelp(Player)));
        Assert.NotEqual(0, w.World.Script.TutorialTimer);
    }

    [Fact]
    public void AdvanceScene_NeedsAScene()
    {
        var w = new OrderWorld();

        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new AdvanceScene(Player)));
        w.World.Script.Active = true;
        w.World.Script.StepCount = 3;
        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new AdvanceScene(Computer)));
        Assert.Equal(OrderOutcome.Applied, w.Apply(new AdvanceScene(Player)));
        Assert.Equal(1, w.World.Script.Step);
    }

    [Fact]
    public void Processor_RefusesNullAndUnknownOrders_AndDecodesPayloads()
    {
        var w = new OrderWorld();
        Building hut = w.Building(BuildingKindNumbers.Hut, 2, 2);

        Assert.Throws<ArgumentNullException>(() => w.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => new OrderProcessor(null!));
        Assert.Equal(OrderOutcome.NotAvailable, w.Apply(new OtherOrder(Player)));
        Assert.Equal(OrderOutcome.Malformed, w.Systems.ApplyOrder(Player, new byte[] { 1, 99 }));
        Assert.Equal(OrderOutcome.UnknownTribe, w.Systems.ApplyOrder(7, GameOrderCodec.Encode(new DemolishBuilding(0, hut.Id))));
        Assert.Equal(OrderOutcome.Applied, w.Systems.ApplyOrder(Player, GameOrderCodec.Encode(new DemolishBuilding(0, hut.Id))));
        Assert.Equal(-1, hut.HitPoints);
        Assert.Same(w.Systems.PlayerOrders, w.Systems.PlayerOrders);
    }

    [Fact]
    public void Menus_GrowWithMissionAndStory()
    {
        var w = new OrderWorld(mission: 1);

        Assert.Equal(new[] { BuildingKindNumbers.Hut, BuildingKindNumbers.Store, BuildingKindNumbers.Kitchen }, OrderMenus.BuildableKinds(w.World));
        w.World.Script.SceneId = 18;
        Assert.Equal(6, OrderMenus.BuildableKinds(w.World).Count);
        w.World.Mission = 7;
        Assert.Equal(11, OrderMenus.BuildableKinds(w.World).Count);
        Assert.Empty(OrderMenus.ArmouryMakes(0));
        Assert.Empty(OrderMenus.ArmouryMakes(4));
        Assert.Equal(8, OrderMenus.ArmouryMakes(3).Count);
        Assert.Equal(0, OrderMenus.SanctuarySpells(w.World, -1));
        Assert.Equal(4, OrderMenus.SanctuarySpells(w.World, 3));
        Assert.False(OrderMenus.InStock(w.World, Player, 0));
        Assert.Throws<ArgumentNullException>(() => OrderMenus.BuildableKinds(null!));
        Assert.Throws<ArgumentNullException>(() => OrderMenus.SanctuarySpells(null!, 1));
        Assert.Throws<ArgumentNullException>(() => OrderMenus.InStock(null!, 0, 0));
    }

    [Fact]
    public void UnitSets_CompareByContent_AndRefuseRepeats()
    {
        UnitSet set = UnitSet.Of(4, 2, 9);

        Assert.Equal(UnitSet.Of(4, 2, 9), set);
        Assert.Equal(UnitSet.Of(4, 2, 9).GetHashCode(), set.GetHashCode());
        Assert.NotEqual(UnitSet.Of(2, 4, 9), set);
        Assert.False(set.Equals(null));
        Assert.False(set.Equals((object)"x"));
        Assert.True(set.Equals((object)UnitSet.Of(4, 2, 9)));
        Assert.Equal("[4, 2, 9]", set.ToString());
        Assert.Equal(new[] { 4, 2, 9 }, set.ToArray());
        Assert.Equal(2, set[1]);
        Assert.Equal(3, ((System.Collections.IEnumerable)set).Cast<int>().Count());
        Assert.Throws<ArgumentException>(() => UnitSet.Of(1, 1));
        Assert.Throws<ArgumentException>(() => new UnitSet(Enumerable.Range(1, UnitSet.MaxCount + 1)));
        Assert.Throws<ArgumentNullException>(() => new UnitSet(null!));
        Assert.Throws<ArgumentNullException>(() => UnitSet.IsValid(null!));
        Assert.False(UnitSet.IsValid(new[] { 3, 3 }));
        Assert.True(UnitSet.IsValid(Array.Empty<int>()));
    }

    private sealed record OtherOrder(int Tribe) : GameOrder(Tribe);
}
