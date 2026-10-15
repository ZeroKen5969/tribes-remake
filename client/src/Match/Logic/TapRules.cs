using Tribes.Core.Commands;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>How a tap or click is meant.</summary>
public enum TapMode
{
    /// <summary>Touch: selects when nothing is selected, gives orders to selected units.</summary>
    Auto,

    /// <summary>Left click: always selects (or shows) what is under the pointer.</summary>
    Select,

    /// <summary>Shift-click or touch multi-select: adds a unit to the selection or takes it out.</summary>
    Toggle,

    /// <summary>Right click: gives the selected units an order.</summary>
    Order,
}

/// <summary>What a tap does.</summary>
public enum TapAction
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>Select the picked unit alone.</summary>
    SelectUnit,

    /// <summary>Add the picked unit to the selection or take it out.</summary>
    ToggleUnit,

    /// <summary>Select the picked building.</summary>
    SelectBuilding,

    /// <summary>Show what was picked (not the player's).</summary>
    ShowOther,

    /// <summary>Select nothing.</summary>
    Deselect,

    /// <summary>Send the selected units there (MoveUnits).</summary>
    Move,

    /// <summary>The selected units were sent to the tribe's own store: open the store's menu for them.</summary>
    StoreErrand,
}

/// <summary>Decides what a tap or click on the map does, from what is selected and what was hit.</summary>
public static class TapRules
{
    /// <summary>The action for a tap of <paramref name="mode"/> on <paramref name="pick"/>.</summary>
    public static TapAction Resolve(TapMode mode, bool unitsSelected, MapPick pick, int pickedBuildingKind)
    {
        if (pick.Kind == PickKind.Outside)
        {
            return mode == TapMode.Order ? TapAction.None : TapAction.Deselect;
        }
        switch (mode)
        {
            case TapMode.Toggle:
                return pick.Kind == PickKind.OwnUnit ? TapAction.ToggleUnit : Resolve(TapMode.Auto, unitsSelected, pick, pickedBuildingKind);
            case TapMode.Select:
                return SelectAction(pick);
            case TapMode.Order:
                return unitsSelected ? OrderAction(pick, pickedBuildingKind) : TapAction.None;
            default:
                if (pick.Kind == PickKind.OwnUnit || !unitsSelected)
                {
                    return SelectAction(pick);
                }
                return OrderAction(pick, pickedBuildingKind);
        }
    }

    /// <summary>
    /// Whether the selection already shows the creature <paramref name="pick"/> is (that one unit
    /// alone, or that other creature): a tap on it again goes to the building drawn behind it.
    /// </summary>
    public static bool ShowsAlready(UnitSelection selection, MapPick pick)
    {
        System.ArgumentNullException.ThrowIfNull(selection);
        return pick.Kind == PickKind.OwnUnit
            ? selection.Kind == SelectionKind.Units && selection.Units.Count == 1 && selection.Units[0] == pick.Id
            : pick.IsCreature && selection.Kind == SelectionKind.Other && selection.OtherCode == pick.TargetCode;
    }

    /// <summary>The order for the selected units when the action is <see cref="TapAction.Move"/>.</summary>
    public static MoveUnits Move(int tribe, UnitSelection selection, MapPick pick, bool hurry)
    {
        System.ArgumentNullException.ThrowIfNull(selection);
        return new MoveUnits(tribe, selection.ToUnitSet(), pick.Cell, pick.TargetCode, hurry);
    }

    private static TapAction SelectAction(MapPick pick) => pick.Kind switch
    {
        PickKind.OwnUnit => TapAction.SelectUnit,
        PickKind.OwnBuilding => TapAction.SelectBuilding,
        PickKind.OtherUnit or PickKind.Animal or PickKind.OtherBuilding or PickKind.Node => TapAction.ShowOther,
        _ => TapAction.Deselect,
    };

    private static TapAction OrderAction(MapPick pick, int pickedBuildingKind) =>
        pick.Kind == PickKind.OwnBuilding && pickedBuildingKind == RuleNumbers.Store ? TapAction.StoreErrand : TapAction.Move;
}
