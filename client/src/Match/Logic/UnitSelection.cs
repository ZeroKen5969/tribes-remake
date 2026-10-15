using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Maps;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>What the player has selected: some of their units, or one building, or nothing.</summary>
public enum SelectionKind
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>One or more of the player's units.</summary>
    Units,

    /// <summary>One of the player's buildings.</summary>
    Building,

    /// <summary>Something that is not the player's (shown, but takes no orders): a unit, animal, building or node.</summary>
    Other,
}

/// <summary>
/// The player's selection and their selection groups. Units keep the order they were picked in
/// (the rules go through an order's units in that order). Selecting units clears a selected
/// building and the other way round. Every change bumps <see cref="Version"/>.
/// </summary>
public sealed class UnitSelection
{
    /// <summary>Selection groups the player can store (keys 1..4).</summary>
    public const int GroupCount = 4;

    private readonly List<int> units = new();
    private readonly List<int>[] groups = Enumerable.Range(0, GroupCount).Select(_ => new List<int>()).ToArray();

    /// <summary>What is selected.</summary>
    public SelectionKind Kind { get; private set; }

    /// <summary>The selected units in pick order (empty unless <see cref="Kind"/> is <see cref="SelectionKind.Units"/>).</summary>
    public IReadOnlyList<int> Units => units;

    /// <summary>The selected building slot, 0 for none.</summary>
    public int Building { get; private set; }

    /// <summary>For <see cref="SelectionKind.Other"/>: the cell code of what is shown (a unit, an animal, a building or a node).</summary>
    public int OtherCode { get; private set; }

    /// <summary>Goes up by one on every change.</summary>
    public int Version { get; private set; }

    /// <summary>Whether any unit is selected.</summary>
    public bool HasUnits => units.Count > 0;

    /// <summary>The selected units as an order's unit set.</summary>
    public UnitSet ToUnitSet() => new(units.Take(UnitSet.MaxCount));

    /// <summary>Selects only <paramref name="unit"/>.</summary>
    public void SelectUnit(int unit) => SetUnits(new[] { unit });

    /// <summary>Selects <paramref name="picked"/> (in their order, repeats dropped); none selects nothing.</summary>
    public void SetUnits(IEnumerable<int> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        units.Clear();
        AddDistinct(picked);
        Building = 0;
        OtherCode = 0;
        Kind = units.Count > 0 ? SelectionKind.Units : SelectionKind.None;
        Version++;
    }

    /// <summary>Adds <paramref name="picked"/> to the selected units (a selected building is let go).</summary>
    public void AddUnits(IEnumerable<int> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        if (Kind != SelectionKind.Units)
        {
            units.Clear();
        }
        AddDistinct(picked);
        Building = 0;
        OtherCode = 0;
        Kind = units.Count > 0 ? SelectionKind.Units : SelectionKind.None;
        Version++;
    }

    /// <summary>Adds <paramref name="unit"/> to the selection, or takes it out when it is in already.</summary>
    public void ToggleUnit(int unit)
    {
        if (Kind == SelectionKind.Units && units.Remove(unit))
        {
            Kind = units.Count > 0 ? SelectionKind.Units : SelectionKind.None;
            Version++;
            return;
        }
        AddUnits(new[] { unit });
    }

    /// <summary>Selects building <paramref name="building"/>.</summary>
    public void SelectBuilding(int building)
    {
        units.Clear();
        Building = building;
        OtherCode = 0;
        Kind = building > 0 ? SelectionKind.Building : SelectionKind.None;
        Version++;
    }

    /// <summary>Shows something that is not the player's (cell code <paramref name="code"/>).</summary>
    public void ShowOther(int code)
    {
        units.Clear();
        Building = 0;
        OtherCode = code;
        Kind = SelectionKind.Other;
        Version++;
    }

    /// <summary>Selects nothing.</summary>
    public void Clear()
    {
        if (Kind == SelectionKind.None)
        {
            return;
        }
        units.Clear();
        Building = 0;
        OtherCode = 0;
        Kind = SelectionKind.None;
        Version++;
    }

    /// <summary>Takes <paramref name="unit"/> out of the selection and every group (it died, went inside...).</summary>
    public void Forget(int unit)
    {
        bool changed = units.Remove(unit);
        foreach (List<int> group in groups)
        {
            group.Remove(unit);
        }
        if (Kind == SelectionKind.Other && CellCode.UnitOf(OtherCode) == unit)
        {
            OtherCode = 0;
            Kind = SelectionKind.None;
            changed = true;
        }
        if (changed)
        {
            if (Kind == SelectionKind.Units && units.Count == 0)
            {
                Kind = SelectionKind.None;
            }
            Version++;
        }
    }

    /// <summary>Drops what the checks say is gone: units no longer selectable, the building, or what is shown.</summary>
    public void Prune(Func<int, bool> unitSelectable, Func<int, bool> buildingSelectable, Func<int, bool> otherShown)
    {
        ArgumentNullException.ThrowIfNull(unitSelectable);
        ArgumentNullException.ThrowIfNull(buildingSelectable);
        ArgumentNullException.ThrowIfNull(otherShown);
        bool changed = false;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (!unitSelectable(units[i]))
            {
                units.RemoveAt(i);
                changed = true;
            }
        }
        if (Kind == SelectionKind.Units && units.Count == 0)
        {
            Kind = SelectionKind.None;
            changed = true;
        }
        if (Kind == SelectionKind.Building && !buildingSelectable(Building))
        {
            Building = 0;
            Kind = SelectionKind.None;
            changed = true;
        }
        if (Kind == SelectionKind.Other && !otherShown(OtherCode))
        {
            OtherCode = 0;
            Kind = SelectionKind.None;
            changed = true;
        }
        if (changed)
        {
            Version++;
        }
    }

    /// <summary>Stores the selected units as group <paramref name="group"/> (0-based).</summary>
    public void StoreGroup(int group)
    {
        List<int> target = groups[CheckGroup(group)];
        target.Clear();
        target.AddRange(units);
    }

    /// <summary>The units of group <paramref name="group"/>.</summary>
    public IReadOnlyList<int> Group(int group) => groups[CheckGroup(group)];

    /// <summary>Selects group <paramref name="group"/>'s units that are still <paramref name="selectable"/>; false when none are.</summary>
    public bool RecallGroup(int group, Func<int, bool> selectable)
    {
        ArgumentNullException.ThrowIfNull(selectable);
        List<int> members = groups[CheckGroup(group)];
        members.RemoveAll(unit => !selectable(unit));
        if (members.Count == 0)
        {
            return false;
        }
        SetUnits(members.ToArray());
        return true;
    }

    private void AddDistinct(IEnumerable<int> picked)
    {
        foreach (int unit in picked)
        {
            if (unit > 0 && !units.Contains(unit) && units.Count < UnitSet.MaxCount)
            {
                units.Add(unit);
            }
        }
    }

    private static int CheckGroup(int group) =>
        group >= 0 && group < GroupCount ? group : throw new ArgumentOutOfRangeException(nameof(group), group, "groups are 0..3");
}
