using System.Linq;
using System.Numerics;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>The clock, the selection, the camera limits and the tap rules.</summary>
public sealed class LogicTests
{
    [Fact]
    public void Clock_TicksAtTheOriginalRate_AndScalesWithSpeed()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0, clock.Advance(0.1));
        Assert.Equal(1, clock.Advance(0.03));
        Assert.Equal(8, clock.Advance(0.12 * 8 + 0.001));
        clock.Speed = 2;
        Assert.Equal(1, clock.Advance(0.061));
        Assert.Equal(0.06, clock.RealTickSeconds, 6);
    }

    [Fact]
    public void Clock_DropsALongStall_AndStandsStillWhenPaused()
    {
        var clock = new FixedStepClock(0.12, maxTicksPerUpdate: 4);
        Assert.Equal(4, clock.Advance(10));
        Assert.Equal(0, clock.Alpha, 6);
        clock.Paused = true;
        Assert.Equal(0, clock.Advance(1));
        clock.Paused = false;
        Assert.Equal(1, clock.Advance(0.13));
        Assert.Equal(0, clock.Advance(double.NaN));
    }

    [Fact]
    public void Selection_KeepsPickOrder_AndTogglesUnits()
    {
        var selection = new UnitSelection();
        selection.SetUnits(new[] { 5, 3, 5, 9 });
        Assert.Equal(new[] { 5, 3, 9 }, selection.Units);
        Assert.Equal(UnitSet.Of(5, 3, 9), selection.ToUnitSet());
        selection.ToggleUnit(3);
        Assert.Equal(new[] { 5, 9 }, selection.Units);
        selection.ToggleUnit(7);
        Assert.Equal(new[] { 5, 9, 7 }, selection.Units);
        selection.SelectBuilding(12);
        Assert.Equal(SelectionKind.Building, selection.Kind);
        Assert.Empty(selection.Units);
        selection.ToggleUnit(4);
        Assert.Equal(SelectionKind.Units, selection.Kind);
        Assert.Equal(0, selection.Building);
    }

    [Fact]
    public void Selection_ForgetsGoneUnits_AndGroupsRecallOnlyLivingOnes()
    {
        var selection = new UnitSelection();
        selection.SetUnits(new[] { 1, 2, 3 });
        selection.StoreGroup(0);
        int version = selection.Version;
        selection.Forget(2);
        Assert.Equal(new[] { 1, 3 }, selection.Units);
        Assert.True(selection.Version > version);
        selection.Clear();
        Assert.True(selection.RecallGroup(0, unit => unit != 3));
        Assert.Equal(new[] { 1 }, selection.Units);
        Assert.False(selection.RecallGroup(1, _ => true));
        selection.Prune(_ => false, _ => true, _ => true);
        Assert.Equal(SelectionKind.None, selection.Kind);
    }

    [Fact]
    public void Selection_PrunesAGoneBuildingAndOther()
    {
        var selection = new UnitSelection();
        selection.SelectBuilding(4);
        selection.Prune(_ => true, b => b != 4, _ => true);
        Assert.Equal(SelectionKind.None, selection.Kind);
        selection.ShowOther(105);
        selection.Prune(_ => true, _ => true, code => code != 105);
        Assert.Equal(SelectionKind.None, selection.Kind);
    }

    // The selection panel's cross, Esc and the back key all deselect with UnitSelection.Clear.
    [Fact]
    public void Selection_ClearLetsGoOfUnitsABuildingOrAnOtherThing_AndKeepsTheGroups()
    {
        var selection = new UnitSelection();
        selection.SetUnits(new[] { 4, 6 });
        selection.StoreGroup(1);
        int version = selection.Version;
        selection.Clear();
        Assert.Equal(SelectionKind.None, selection.Kind);
        Assert.Empty(selection.Units);
        Assert.False(selection.HasUnits);
        Assert.True(selection.Version > version);
        Assert.True(selection.RecallGroup(1, _ => true));
        Assert.Equal(new[] { 4, 6 }, selection.Units);

        selection.SelectBuilding(9);
        selection.Clear();
        Assert.Equal(SelectionKind.None, selection.Kind);
        Assert.Equal(0, selection.Building);

        selection.ShowOther(105);
        selection.Clear();
        Assert.Equal(SelectionKind.None, selection.Kind);
        Assert.Equal(0, selection.OtherCode);

        // Nothing selected: nothing changes, so the panel is not rebuilt.
        version = selection.Version;
        selection.Clear();
        Assert.Equal(version, selection.Version);
    }

    [Fact]
    public void Camera_BringsEveryMapEdgeOutFromUnderTheInterface()
    {
        var map = (Min: Vector2.Zero, Max: new Vector2(1000, 800));
        var insets = new ScreenInsets(0, 20, 120, 0);
        var view = new Vector2(400, 300);
        Vector2 topLeft = CameraLimits.Clamp(new Vector2(-500, -500), 1, view, map.Min, map.Max, insets);
        // The map's top-left corner sits right at the free area's top-left corner.
        Assert.Equal(200, topLeft.X, 3);
        Assert.Equal(150 - 20, topLeft.Y, 3);
        Vector2 bottomRight = CameraLimits.Clamp(new Vector2(5000, 5000), 2, view, map.Min, map.Max, insets);
        // Right edge 1000 + 120 / 2 covered, half view 100.
        Assert.Equal(1000 + 60 - 100, bottomRight.X, 3);
        Assert.Equal(800 - 75, bottomRight.Y, 3);
    }

    [Fact]
    public void Camera_CentresASmallMapInTheFreeArea()
    {
        var insets = new ScreenInsets(100, 0, 0, 0);
        Vector2 centre = CameraLimits.Clamp(new Vector2(0, 0), 1, new Vector2(800, 600), Vector2.Zero, new Vector2(200, 100), insets);
        // Free area spans screen x 100..800, its middle is screen x 450 = camera centre + 50.
        Assert.Equal(100 - 50, centre.X, 3);
        Assert.Equal(50, centre.Y, 3);
        Vector2 shown = CameraLimits.CentreFor(new Vector2(100, 50), 1, insets);
        Assert.Equal(centre, shown);
    }

    [Fact]
    public void Camera_PixelPerfectZoomsStepCleanly()
    {
        var zooms = CameraLimits.PixelPerfectZooms(3, 0.5f, 6f);
        Assert.Equal(2f / 3, zooms[0], 4);
        Assert.Equal(6f, zooms[^1], 4);
        Assert.Equal(4f / 3, CameraLimits.Step(zooms, 1f, 1), 4);
        Assert.Equal(2f / 3, CameraLimits.Step(zooms, 1f, -1), 4);
        Assert.Equal(1f, CameraLimits.Step(zooms, 0.9f, 1), 4);
        Assert.Equal(6f, CameraLimits.Step(zooms, 6f, 3), 4);
        Assert.Single(CameraLimits.PixelPerfectZooms(1, 5.5f, 5.6f));
    }

    [Theory]
    [InlineData(TapMode.Auto, false, PickKind.OwnUnit, 0, TapAction.SelectUnit)]
    [InlineData(TapMode.Auto, true, PickKind.OwnUnit, 0, TapAction.SelectUnit)]
    [InlineData(TapMode.Auto, true, PickKind.Ground, 0, TapAction.Move)]
    [InlineData(TapMode.Auto, true, PickKind.OtherUnit, 0, TapAction.Move)]
    [InlineData(TapMode.Auto, true, PickKind.OwnBuilding, RuleNumbers.Store, TapAction.StoreErrand)]
    [InlineData(TapMode.Auto, true, PickKind.OwnBuilding, RuleNumbers.Hut, TapAction.Move)]
    [InlineData(TapMode.Auto, false, PickKind.OwnBuilding, RuleNumbers.Hut, TapAction.SelectBuilding)]
    [InlineData(TapMode.Auto, false, PickKind.Animal, 0, TapAction.ShowOther)]
    [InlineData(TapMode.Auto, false, PickKind.Ground, 0, TapAction.Deselect)]
    [InlineData(TapMode.Auto, true, PickKind.Outside, 0, TapAction.Deselect)]
    [InlineData(TapMode.Select, true, PickKind.Ground, 0, TapAction.Deselect)]
    [InlineData(TapMode.Select, true, PickKind.OwnBuilding, RuleNumbers.Store, TapAction.SelectBuilding)]
    [InlineData(TapMode.Toggle, true, PickKind.OwnUnit, 0, TapAction.ToggleUnit)]
    [InlineData(TapMode.Toggle, true, PickKind.Node, 0, TapAction.Move)]
    [InlineData(TapMode.Order, false, PickKind.Ground, 0, TapAction.None)]
    [InlineData(TapMode.Order, true, PickKind.OwnUnit, 0, TapAction.Move)]
    [InlineData(TapMode.Order, true, PickKind.Outside, 0, TapAction.None)]
    public void Taps_DoWhatTheModeAndSelectionSay(TapMode mode, bool units, PickKind kind, int buildingKind, TapAction expected)
    {
        var pick = new MapPick(kind, new GridPoint(3, 4), 7, -7);
        Assert.Equal(expected, TapRules.Resolve(mode, units, pick, buildingKind));
    }

    [Fact]
    public void Taps_MoveTheSelectedUnitsToThePickedTarget()
    {
        var selection = new UnitSelection();
        selection.SetUnits(new[] { 4, 2 });
        MoveUnits order = TapRules.Move(1, selection, new MapPick(PickKind.Animal, new GridPoint(8, 9), 3, 104), hurry: true);
        Assert.Equal(new MoveUnits(1, UnitSet.Of(4, 2), new GridPoint(8, 9), 104, true), order);
        Assert.True(Enumerable.SequenceEqual(order.Units, new[] { 4, 2 }));
    }

    private sealed record FakeSave(string Mode, string Slot, System.DateTime SavedAt);

    [Fact]
    public void SaveSlots_ListAutoThenFive_WithTheNewestSaveOfTheKindInEach()
    {
        var old = new FakeSave("campaign", "2", new System.DateTime(2026, 1, 1));
        var newer = new FakeSave("campaign", "2", new System.DateTime(2026, 2, 1));
        var saves = new[] { new FakeSave("campaign", "auto", System.DateTime.Now), old, newer, new FakeSave("custom", "1", System.DateTime.Now) };
        var slots = SaveSlots.Arrange("campaign", saves, s => s.Mode, s => s.Slot, s => s.SavedAt);
        Assert.Equal(new[] { "auto", "1", "2", "3", "4", "5" }, slots.Select(s => s.Slot));
        Assert.NotNull(slots[0].Save);
        Assert.Null(slots[1].Save);
        Assert.Same(newer, slots[2].Save);
        Assert.False(SaveSlots.CanSaveInto(SaveSlots.Auto));
        Assert.True(SaveSlots.CanSaveInto("5"));
        Assert.False(SaveSlots.CanSaveInto("6"));
        Assert.Equal("campaign-3", SaveSlots.Stem("campaign", "3"));
    }
}
