using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Buildings;

/// <summary>
/// Who works at a building: the recount of its occupants (those inside and those about to step
/// in), and sending idle builders of the tribe to construction sites, upgrades and repairs.
/// </summary>
internal sealed class WorkerDispatch
{
    private readonly GameWorld world;

    /// <summary>Dispatches the workers of <paramref name="world"/>.</summary>
    public WorkerDispatch(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Sets the building's occupant count afresh: the <paramref name="listed"/> units in its
    /// occupant list, and the units of its tribe about to step in (heading for it, at no work
    /// position yet, stopping at the next cell centre) other than its operator.
    /// </summary>
    public void RecountOccupants(Building building, int listed)
    {
        ArgumentNullException.ThrowIfNull(building);
        int count = listed;
        bool beds = building.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal;
        foreach (Unit unit in TribeUnits(building.Tribe))
        {
            if (unit.HitPoints > 0 && unit.TargetIndex == building.Id && unit.BuildingSlot < 0
                && unit.ActionSubState == SubStates.StopAtCentre && (unit.Id != building.OperatorOrBeds || beds))
            {
                count++;
            }
        }
        building.OccupantCount = count;
    }

    /// <summary>
    /// Sends builders to the site, upgrade or repair <paramref name="building"/> until as many are
    /// on their way as the work takes. The player's idle builders come unless the whole tribe is
    /// out or they already work on a site or busy building; the computer sends units of its first
    /// posts that are not builders, unless an intruder keeps it busy. A sent unit inside a
    /// building leaves it first.
    /// </summary>
    public void SendBuilders(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        int tribe = building.Tribe;
        bool player = TribeRoles.IsPlayer(world, tribe);
        if ((player && TribeRoles.Army(world, tribe).AllOut) || (TribeRoles.IsComputer(world, tribe) && TribeRoles.Ai(world, tribe).Intruder > 0))
        {
            return;
        }
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        int wanted = building.WorkMode == BuildingWorkMode.Normal ? kind.WorkerSlots[building.Level - 1] : kind.WorkerSlotsWhileBusy;
        if (building.DispatchedWorkers < building.OccupantCount && building.OperatorOrBeds > 0)
        {
            building.DispatchedWorkers = building.OccupantCount;
        }
        foreach (Unit unit in TribeUnits(tribe))
        {
            if (building.DispatchedWorkers >= wanted)
            {
                return;
            }
            if (player ? IsFreePlayerBuilder(unit) : IsSpareComputerUnit(unit))
            {
                Send(unit, building);
            }
        }
    }

    private void Send(Unit unit, Building building)
    {
        unit.TaskSiteX = unit.DestinationX = building.X;
        unit.TaskSiteY = unit.DestinationY = building.Y;
        unit.AssignedTask = unit.Action = WorkActions.SentBuilder;
        unit.OrderMode = OrderModes.Forced;
        building.DispatchedWorkers++;
        if (CellCode.At(world.Map, new GridPoint(unit.X, unit.Y)) != CellCode.OfUnit(unit.Id))
        {
            unit.Action = ActionNumbers.LeaveBuilding;
        }
    }

    /// <summary>
    /// An idle (or idly pausing) free builder of the player whose task site is not already one of
    /// the tribe's sites, busy buildings or buildings of the story.
    /// </summary>
    private bool IsFreePlayerBuilder(Unit unit)
    {
        bool idle = unit.Action == ActionNumbers.Idle || (unit.Action == WorkActions.Pause && unit.SavedAction == ActionNumbers.Idle);
        if (!idle || unit.OrderMode != OrderModes.Free || ServiceGroup(unit.AssignedTask) != ServiceGroups.Construction)
        {
            return false;
        }
        var taskSite = new GridPoint(unit.TaskSiteX, unit.TaskSiteY);
        if (!world.Map.Contains(taskSite))
        {
            return true;
        }
        int slot = CellCode.BuildingOf(CellCode.At(world.Map, taskSite));
        if (slot < 1 || slot > world.BuildingSlots || world.TribeOfBuilding(slot) != unit.Tribe)
        {
            return true;
        }
        Building site = world.Building(slot);
        return site.Kind < BuildingKindNumbers.FirstSite && site.WorkMode == BuildingWorkMode.Normal;
    }

    /// <summary>A computer unit at one of the first posts (the ones that may build) whose task is not building.</summary>
    private bool IsSpareComputerUnit(Unit unit)
    {
        int post = TribeRoles.Ai(world, unit.Tribe).JobSlot(unit.SlotInTribe);
        return post < world.Content.Rules.Missions.Missions[world.Mission].Ai.BuilderPostCount
            && ServiceGroup(unit.AssignedTask) != ServiceGroups.Construction;
    }

    private System.Collections.Generic.IEnumerable<Unit> TribeUnits(int tribe)
    {
        ListView<Unit> units = world.UnitsOf(tribe);
        for (int i = 0; i < units.Count; i++)
        {
            yield return units[i];
        }
    }

    private int ServiceGroup(int action)
    {
        ActionTables tables = world.Content.Rules.Actions;
        return tables.Categories[tables.Actions[action].Category].ServiceGroup;
    }
}
