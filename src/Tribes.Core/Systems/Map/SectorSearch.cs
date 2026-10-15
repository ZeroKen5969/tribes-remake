using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>A search for the most important target near a cell, kept across the object groups searched.</summary>
internal sealed class TargetSearch
{
    /// <summary>First sector column searched.</summary>
    public int MinSectorX { get; set; }

    /// <summary>Last sector column searched.</summary>
    public int MaxSectorX { get; set; }

    /// <summary>First sector row searched.</summary>
    public int MinSectorY { get; set; }

    /// <summary>Last sector row searched.</summary>
    public int MaxSectorY { get; set; }

    /// <summary>Threat priority of the best target so far (its object group's, 1 for a child); 0 none.</summary>
    public int Priority { get; set; }

    /// <summary>Distance in cells of the best target so far.</summary>
    public int Distance { get; set; }

    /// <summary>Where the best target is (a building's centre cell).</summary>
    public GridPoint Location { get; set; }

    /// <summary>Slot of the best target.</summary>
    public int Slot { get; set; }

    /// <summary>Starts the search again over sectors <paramref name="minX"/>..<paramref name="maxX"/>, <paramref name="minY"/>..<paramref name="maxY"/>, with nothing found.</summary>
    public void Restart(int minX, int maxX, int minY, int maxY)
    {
        (MinSectorX, MaxSectorX, MinSectorY, MaxSectorY) = (minX, maxX, minY, maxY);
        (Priority, Distance, Location, Slot) = (0, 0, default, 0);
    }
}

/// <summary>
/// The sector index (<see cref="SectorIndex"/>): rebuilt every few frames, and searched for targets
/// and for tribe mates to alarm.
/// </summary>
internal sealed class SectorSearch
{
    /// <summary>A coordinate shifted right by this gives its sector (sectors are 16 cells).</summary>
    private const int SectorShift = 4;

    /// <summary>The chain end and empty sector marker.</summary>
    private const int NoSlot = -1;

    /// <summary>An action whose units are left out of alarms (the data does not tell what the action is for).</summary>
    private const int UnalarmableAction = 50;

    /// <summary>Order mode of a unit answering an alarm.</summary>
    private const int AlarmOrderMode = 4;

    /// <summary>Threat priority of an enemy child.</summary>
    private const int ChildPriority = 1;

    private readonly GameWorld world;
    private readonly GridDistances distances;
    private readonly ObjectGroupTable groups;

    /// <summary>Searches <paramref name="world"/>.</summary>
    public SectorSearch(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        groups = new ObjectGroupTable(world);
    }

    /// <summary>The object groups searched (the sector index's categories).</summary>
    public ObjectGroupTable Groups => groups;

    /// <summary>
    /// Rebuilds the index: every existing entity of each group is chained into the sector of its
    /// position; units only while they stand on their cell (or guard from a tower).
    /// </summary>
    public void Rebuild()
    {
        SectorIndex index = world.Sectors;
        int groupCount = groups.Count;
        index.EmptyHeads();
        for (int groupIndex = 0; groupIndex < groupCount && !index.IsChunked; groupIndex++)
        {
            for (int sy = 0; sy < SectorIndex.SectorsPerSide; sy++)
            {
                for (int sx = 0; sx < SectorIndex.SectorsPerSide; sx++)
                {
                    index.SetHead(groupIndex, sx, sy, NoSlot);
                }
            }
        }
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            if (ObjectGroupTable.TribeOf(groupIndex) >= world.Tribes.Count)
            {
                continue;
            }
            ObjectGroup group = groups[groupIndex];
            GroupSlots slots = groups.Slots(groupIndex);
            for (int entry = 0; entry < slots.Count; entry++)
            {
                int slot = slots[entry];
                if (IsIndexed(groupIndex, group, slot))
                {
                    GridPoint at = PositionOf(group, slot);
                    int sx = at.X >> SectorShift, sy = at.Y >> SectorShift;
                    index.SetNext(groupIndex, entry, index.Head(groupIndex, sx, sy));
                    index.SetHead(groupIndex, sx, sy, entry);
                }
            }
        }
    }

    /// <summary>Starts a search within <paramref name="radius"/> cells' worth of sectors around <paramref name="centre"/> on the world's map.</summary>
    public TargetSearch Around(GridPoint centre, int radius)
    {
        var search = new TargetSearch();
        Restart(search, centre, radius);
        return search;
    }

    /// <summary>Starts <paramref name="search"/> again, as <see cref="Around"/> would start a new one.</summary>
    public void Restart(TargetSearch search, GridPoint centre, int radius)
    {
        ArgumentNullException.ThrowIfNull(search);
        SectorIndex index = world.Sectors;
        search.Restart(index.SectorOf(centre.X - radius), index.SectorOf(centre.X + radius),
            index.SectorOf(centre.Y - radius), index.SectorOf(centre.Y + radius));
    }

    /// <summary>Starts a search within <paramref name="radius"/> cells' worth of sectors around <paramref name="centre"/> on a fixed map.</summary>
    public static TargetSearch BeginSearch(GridPoint centre, int radius) => new()
    {
        MinSectorX = SectorOf(centre.X - radius),
        MaxSectorX = SectorOf(centre.X + radius),
        MinSectorY = SectorOf(centre.Y - radius),
        MaxSectorY = SectorOf(centre.Y + radius),
    };

    /// <summary>
    /// Looks through object group <paramref name="group"/> for a better target for
    /// <paramref name="searcher"/> standing at <paramref name="from"/>: existing, not hidden from it,
    /// animals only when they hunt someone or the searcher hunts, and within its sight (its throwing
    /// range when it is not on the grid). Higher priority wins, then the nearer one.
    /// </summary>
    public void SearchGroup(TargetSearch search, int group, Unit searcher, GridPoint from)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(searcher);
        if (!world.Sectors.MayHold(group, search.MinSectorX, search.MaxSectorX, search.MinSectorY, search.MaxSectorY))
        {
            return;
        }
        int reach = CellCode.At(world.Map, from) == CellCode.OfUnit(searcher.Id) ? searcher.SightRadius : searcher.ThrowRange;
        SectorIndex index = world.Sectors;
        GroupSlots slots = groups.Slots(group);
        for (int sx = search.MinSectorX; sx <= search.MaxSectorX; sx++)
        {
            for (int sy = search.MinSectorY; sy <= search.MaxSectorY; sy++)
            {
                for (int entry = index.Head(group, sx, sy); entry >= 0; entry = index.Next(group, entry))
                {
                    int slot = slots[entry];
                    GridPoint at = CentreOf(group, slot);
                    int distance = GridDistances.Cells(distances.MeasureAtEdge(at.X - from.X, at.Y - from.Y));
                    Consider(search, group, slot, at, distance, searcher, reach);
                }
            }
        }
    }

    /// <summary>Whether the target at <paramref name="slot"/> of <paramref name="group"/> beats the best one of <paramref name="search"/> (then it is the best).</summary>
    private void Consider(TargetSearch search, int group, int slot, GridPoint at, int distance, Unit searcher, int reach)
    {
        if (!IsTargetFor(group, slot, searcher) || distance > reach)
        {
            return;
        }
        int priority = ObjectGroupTable.KindOf(group) == ObjectGroupKind.Units && world.Unit(slot).Age < UnitAges.Adult
            ? ChildPriority : groups[group].ThreatPriority;
        if (search.Priority < priority || (search.Priority == priority && distance < search.Distance))
        {
            search.Priority = priority;
            search.Distance = distance;
            search.Location = at;
            search.Slot = slot;
        }
    }

    /// <summary>
    /// Alarms (through the object group of the tribe's units) the free adults of <paramref name="tribe"/> standing within <paramref name="radius"/>
    /// of <paramref name="centre"/>: they chase the content of cell code <paramref name="alarmCode"/>.
    /// </summary>
    public void RaiseAlarm(int tribe, GridPoint centre, int radius, int alarmCode)
    {
        TargetSearch area = Around(centre, radius);
        Visit(area, ObjectGroupTable.Units(tribe), centre, (slot, _, distance) =>
        {
            Unit unit = world.Unit(slot);
            if (distance <= radius && CanBeAlarmed(unit))
            {
                unit.OrderMode = AlarmOrderMode;
                unit.TargetCellCode = alarmCode;
            }
        });
    }

    private bool CanBeAlarmed(Unit unit) =>
        CellCode.At(world.Map, new GridPoint(unit.X, unit.Y)) == CellCode.OfUnit(unit.Id)
        && unit.HitPoints > 0 && unit.Age >= UnitAges.Adult && unit.OrderMode == OrderModes.Free
        && unit.Action > ActionNumbers.LastBasicAttack && unit.Action != ActionNumbers.StayInside
        && unit.Action != ActionNumbers.LeaveBuilding && unit.Action != UnalarmableAction;

    private bool IsTargetFor(int group, int slot, Unit searcher)
    {
        if (EntityProperties.Get(world, groups[group].HitPointsProperty, slot) <= 0)
        {
            return false;
        }
        ObjectGroupKind kind = ObjectGroupTable.KindOf(group);
        if (kind == ObjectGroupKind.Units && (world.Unit(slot).Status & UnitStatus.Undetected) != 0)
        {
            return false;
        }
        return kind != ObjectGroupKind.Animals || searcher.Action == ActionNumbers.HuntingTask || world.Animal(slot).Target != 0;
    }

    private void Visit(TargetSearch area, int group, GridPoint from, Action<int, GridPoint, int> visit)
    {
        SectorIndex index = world.Sectors;
        GroupSlots slots = groups.Slots(group);
        for (int sx = area.MinSectorX; sx <= area.MaxSectorX; sx++)
        {
            for (int sy = area.MinSectorY; sy <= area.MaxSectorY; sy++)
            {
                for (int entry = index.Head(group, sx, sy); entry >= 0; entry = index.Next(group, entry))
                {
                    int slot = slots[entry];
                    GridPoint at = CentreOf(group, slot);
                    int distance = GridDistances.Cells(distances.MeasureAtEdge(at.X - from.X, at.Y - from.Y));
                    visit(slot, at, distance);
                }
            }
        }
    }

    private bool IsIndexed(int groupIndex, ObjectGroup group, int slot)
    {
        if (EntityProperties.Get(world, group.HitPointsProperty, slot) <= 0)
        {
            return false;
        }
        if (ObjectGroupTable.KindOf(groupIndex) != ObjectGroupKind.Units)
        {
            return true;
        }
        Unit unit = world.Unit(slot);
        return CellCode.At(world.Map, new GridPoint(unit.X, unit.Y)) == CellCode.OfUnit(slot)
            || unit.Action is ActionNumbers.TowerShot or ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard;
    }

    private GridPoint PositionOf(ObjectGroup group, int slot) => new(
        EntityProperties.Get(world, group.PositionProperty, slot),
        EntityProperties.Get(world, group.PositionProperty + 1, slot));

    private GridPoint CentreOf(int group, int slot)
    {
        GridPoint at = PositionOf(groups[group], slot);
        if (ObjectGroupTable.KindOf(group) != ObjectGroupKind.Buildings)
        {
            return at;
        }
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[world.Building(slot).Kind];
        return new GridPoint(at.X + (kind.Width / 2), at.Y + (kind.Height / 2));
    }

    private static int SectorOf(int coordinate) => GridMath.Clamp(coordinate >> SectorShift, 0, SectorIndex.SectorsPerSide - 1);
}
