using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// Who is in which building: homes in huts, work positions inside buildings, a tower's range from
/// its guards, and sending units out of a building.
/// </summary>
internal sealed class Occupancy
{
    /// <summary>Range of a building without guards (low four bits of <see cref="Building.RangeInfo"/>).</summary>
    public const int DefaultRange = 5;

    /// <summary>Work positions a building has at most.</summary>
    public const int MaxPositions = 8;

    /// <summary>Mask of the random draw that picks a work position.</summary>
    private const int PositionRollMask = 0xFFFFFF;

    /// <summary>Added to a guard's sight radius for the tower's range.</summary>
    private const int TowerSightBonus = 3;

    private const int RangeMask = 0x0F;
    private const int ScoutingMask = 0xF0;
    private const int ScoutingShift = 4;

    /// <summary>Kinds whose occupants walk out calmly even when they are thrown out.</summary>
    private static readonly HashSet<int> KindsLeftCalmly = new()
    {
        BuildingKindNumbers.Hut, BuildingKindNumbers.Disco, BuildingKindNumbers.Kitchen, BuildingKindNumbers.Sanctuary,
    };

    private readonly GameWorld world;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly BuildingSlotTables slots;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public Occupancy(GameWorld world, BuildingSlotTables slots)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.slots = slots ?? throw new ArgumentNullException(nameof(slots));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>The units in <paramref name="building"/>'s occupant list, first to last.</summary>
    public IEnumerable<Unit> Occupants(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        for (int id = building.FirstOccupant; id > 0; id = world.Unit(id).NextOccupantOrCellObject)
        {
            yield return world.Unit(id);
        }
    }

    /// <summary>Gives <paramref name="unit"/> up its bed: the hut has one reservation less.</summary>
    public void FreeHome(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Home <= 0)
        {
            return;
        }
        Building home = world.Building(unit.Home);
        if (home.WorkMode == BuildingWorkMode.Normal)
        {
            home.OperatorOrBeds--;
        }
        else
        {
            home.Aux--;
        }
        unit.Home = 0;
    }

    /// <summary>
    /// Finds <paramref name="unit"/> a home: the chief lives in the chief's hut (the tribe's first
    /// building), others take a bed in the first working hut of their tribe that has one free.
    /// </summary>
    public void AssignHome(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Id == world.Tribes[unit.Tribe][TribeCounter.Chief])
        {
            unit.Home = world.FirstBuildingSlot(unit.Tribe);
            return;
        }
        foreach (Building hut in world.BuildingsOf(unit.Tribe))
        {
            if (hut.Kind == BuildingKindNumbers.Hut && hut.WorkMode == BuildingWorkMode.Normal
                && hut.OperatorOrBeds < kinds[hut.Kind].WorkerSlots[hut.Level - 1])
            {
                unit.Home = hut.Id;
                hut.OperatorOrBeds++;
                return;
            }
        }
    }

    /// <summary>
    /// Gives <paramref name="unit"/>, entering the building it targets, a work position. The
    /// operator's position goes to the first unit while nobody holds it; others draw one of the free
    /// positions at random (a hut's first comer takes the first bed). While the building is upgraded
    /// or repaired the positions are the builders' places.
    /// </summary>
    public void PickWorkPosition(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Building building = world.Building(unit.TargetIndex);
        BuildingKind kind = kinds[building.Kind];
        bool normal = building.WorkMode == BuildingWorkMode.Normal;
        int manager = normal ? kind.ManagerSlot : 0;
        int occupied = building.OccupantCount;
        int[] taken = TakenPositions(building, normal);
        if (manager >= 0 && taken[0] == 0)
        {
            int holder = building.OperatorOrBeds;
            if (holder <= 0 || holder == unit.Id)
            {
                if (holder == unit.Id)
                {
                    building.OccupantCount++;
                }
                unit.BuildingSlot = manager;
                building.OperatorOrBeds = unit.Id;
                return;
            }
            taken[manager] = holder;
            Unit operatorUnit = world.Unit(holder);
            if (operatorUnit.BuildingSlot < 0 || operatorUnit.TargetIndex != unit.TargetIndex)
            {
                occupied++;
            }
        }
        int places = normal ? kind.WorkerSlots[building.Level - 1] : kind.WorkerSlotsWhileBusy;
        unit.BuildingSlot = RollPosition(places - occupied + 1) + 1;
        if (building.Kind == BuildingKindNumbers.Hut && taken[0] == 0)
        {
            unit.BuildingSlot = 0;
            return;
        }
        TakeDrawnFreePosition(unit, taken, places);
        if (!normal)
        {
            unit.BuildingSlot = slots.BusySlotOfPlace(building.Kind, unit.BuildingSlot) ?? unit.BuildingSlot;
        }
    }

    /// <summary>
    /// Sets the range of <paramref name="building"/> again after a unit went in or out: the default,
    /// or for a tower of the player's tribe the best guard's sight plus three (low four bits) and
    /// the best guard's scouting skill (high four bits).
    /// </summary>
    public void UpdateRange(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        building.RangeInfo = DefaultRange;
        if (!TribeRoles.IsPlayer(world, building.Tribe) || building.Kind != BuildingKindNumbers.Tower)
        {
            return;
        }
        foreach (Unit guard in Occupants(building))
        {
            if (guard.SightRadius + TowerSightBonus > (building.RangeInfo & RangeMask))
            {
                building.RangeInfo = (building.RangeInfo & ScoutingMask) + guard.SightRadius + TowerSightBonus;
            }
            if (guard.ScoutingSkill > building.RangeInfo >> ScoutingShift)
            {
                building.RangeInfo = (building.RangeInfo & RangeMask) + (guard.ScoutingSkill << ScoutingShift);
            }
        }
    }

    /// <summary>
    /// Sends <paramref name="unit"/> out of <paramref name="building"/>. Without <paramref name="forced"/>
    /// it leaves when its work allows (tower guards are always forced out); a forced unit that is not
    /// the operator walks off at once unless the building is one people stay calm in. The operator
    /// gives up the building when forced. With <paramref name="evictOthers"/> the rest are sent out too.
    /// </summary>
    public void LeaveBuilding(Unit unit, Building building, bool forced, bool evictOthers)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(building);
        forced |= unit.Action is ActionNumbers.TowerShot or ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard;
        bool hutBeds = building.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal;
        if (building.OperatorOrBeds != unit.Id || hutBeds)
        {
            if (!forced || (building.WorkMode == BuildingWorkMode.Normal && KindsLeftCalmly.Contains(building.Kind)))
            {
                unit.Action = ActionNumbers.LeaveBuilding;
                unit.OrderMode = OrderModes.Forced;
                return;
            }
            unit.OrderMode = OrderModes.Walk;
            return;
        }
        if (forced)
        {
            building.OperatorOrBeds = 0;
        }
        OperatorLeaves(unit, forced);
        if (evictOthers)
        {
            Evict(building, all: false);
        }
    }

    /// <summary>
    /// Sends the occupants of <paramref name="building"/> out: those at a work position other than
    /// the first (all of them with <paramref name="all"/>), except a tower's throwers, units already
    /// playing their way out and units getting up to change workplace.
    /// </summary>
    public void Evict(Building building, bool all)
    {
        ArgumentNullException.ThrowIfNull(building);
        IReadOnlyList<WeaponKind> weapons = world.Content.Rules.Weapons.Weapons;
        foreach (Unit occupant in Occupants(building))
        {
            bool keepsThrower = building.Kind == BuildingKindNumbers.Tower && building.WorkMode == BuildingWorkMode.Normal
                && weapons[occupant.Weapon].RangedActionOffset != 0;
            if ((occupant.BuildingSlot != 0 || all) && !keepsThrower
                && occupant.AnimationNumber != AnimationNumbers.ChangingWorkplace
                && occupant.AnimationNumber != slots.ExitAnimation(building.Kind, occupant.BuildingSlot)
                && occupant.AnimationNumber != slots.BusyExitAnimation(building.Kind, occupant.BuildingSlot))
            {
                LeaveBuilding(occupant, building, forced: false, evictOthers: false);
            }
        }
    }

    /// <summary>
    /// <paramref name="unit"/> stops running buildings of its tribe: the first one it operates lets
    /// its occupants go and has neither operator nor workers on the way; an upgrade or repair that
    /// kept it as the operator to come back forgets it.
    /// </summary>
    public void ReleaseWorkplaces(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        foreach (Building building in world.BuildingsOf(unit.Tribe))
        {
            if (building.OperatorOrBeds == unit.Id
                && (building.Kind != BuildingKindNumbers.Hut || building.WorkMode != BuildingWorkMode.Normal))
            {
                Evict(building, all: false);
                building.OperatorOrBeds = 0;
                building.DispatchedWorkers = 0;
                return;
            }
            if (building.WorkMode is BuildingWorkMode.Repairing or BuildingWorkMode.Upgrading && building.Aux == unit.Id)
            {
                building.Aux = 0;
            }
        }
    }

    /// <summary>The occupant at each position (0 for a free one); positions outside the building's range are left out.</summary>
    private int[] TakenPositions(Building building, bool normal)
    {
        var taken = new int[MaxPositions];
        foreach (Unit occupant in Occupants(building))
        {
            int? place = normal ? occupant.BuildingSlot : slots.PlaceOfBusySlot(building.Kind, occupant.BuildingSlot);
            if (place is >= 0 and < MaxPositions)
            {
                taken[place.Value] = occupant.Id;
            }
        }
        return taken;
    }

    /// <summary>
    /// Replaces the drawn number n in <paramref name="unit"/>'s position by the n-th free position;
    /// a number past the free positions stays as it is.
    /// </summary>
    private static void TakeDrawnFreePosition(Unit unit, int[] taken, int places)
    {
        int free = 0;
        for (int i = 0; i < Math.Min(places, MaxPositions); i++)
        {
            if (taken[i] == 0)
            {
                free++;
            }
            if (unit.BuildingSlot == free)
            {
                unit.BuildingSlot = i;
                return;
            }
        }
    }

    /// <summary>A random number below <paramref name="count"/> (an overfull building draws from one).</summary>
    private int RollPosition(int count) => world.Random.NextMasked(PositionRollMask, count == 0 ? 1 : count);

    private void OperatorLeaves(Unit unit, bool forced)
    {
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            if (forced)
            {
                unit.OrderMode = OrderModes.Walk;
                return;
            }
            unit.Action = ActionNumbers.LeaveBuilding;
            unit.OrderMode = OrderModes.Forced;
            return;
        }
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        if (forced)
        {
            unit.TaskSiteX = unit.X;
            unit.TaskSiteY = unit.Y;
        }
        unit.Action = ActionNumbers.Idle;
        if (forced)
        {
            unit.OrderMode = OrderModes.Walk;
            unit.TargetCellCode = 0;
            return;
        }
        unit.OrderMode = OrderModes.Forced;
    }
}
