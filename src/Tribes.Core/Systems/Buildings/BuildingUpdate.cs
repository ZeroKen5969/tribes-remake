using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Buildings;

/// <summary>
/// The buildings' part of a frame: the sector index is rebuilt every few frames and the hint
/// cool-downs advance; then every building of every tribe, in slot order, gets its turn. A
/// finished upgrade or repair ends; sites, kitchens, huts and busy buildings set their occupants
/// to work, recount them now and then, finish a complete site and send for builders.
/// </summary>
internal sealed class BuildingUpdate
{
    /// <summary>The sector index is rebuilt on frames that are a multiple of this.</summary>
    public const int SectorRebuildPeriod = 10;

    /// <summary>The "attacked recently" mark and the builder call run on 16-frame cycles.</summary>
    private const int ShortCycleMask = 15;

    /// <summary>Each building recounts its occupants once in this many frames (on the frame matching its slot).</summary>
    private const int RecountCycleMask = 255;

    /// <summary>The level a finished site's building starts at.</summary>
    private const int CompletedLevel = 1;

    private readonly GameWorld world;
    private readonly SectorSearch sectors;
    private readonly HintSystem hints;
    private readonly OccupantWork work;
    private readonly WorkerDispatch dispatch;
    private readonly Occupancy occupancy;
    private readonly BuildingLifecycle lifecycle;
    private readonly BuildingPlacement placement;
    private readonly BuildingWorkLists workLists;

    /// <summary>Updates the buildings of <paramref name="world"/>.</summary>
    public BuildingUpdate(GameWorld world, SectorSearch sectors, HintSystem hints, OccupantWork work, WorkerDispatch dispatch,
        Occupancy occupancy, BuildingLifecycle lifecycle, BuildingPlacement placement, BuildingWorkLists workLists)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.sectors = sectors ?? throw new ArgumentNullException(nameof(sectors));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.work = work ?? throw new ArgumentNullException(nameof(work));
        this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
        this.workLists = workLists ?? throw new ArgumentNullException(nameof(workLists));
    }

    /// <summary>Runs the buildings' part of the current frame.</summary>
    public void RunFrame()
    {
        if (world.Frame % SectorRebuildPeriod == 0)
        {
            sectors.Rebuild();
        }
        hints.UpdateCooldowns();
        foreach (TribeState tribe in world.Tribes)
        {
            ListView<Building> buildings = world.BuildingsOf(tribe.Index);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building.InUse)
                {
                    TakeTurn(building);
                }
            }
        }
    }

    /// <summary>
    /// One building's turn. A site or a busy building missing from its work list is left alone
    /// (the original stops updating buildings for the rest of the frame there).
    /// </summary>
    public void TakeTurn(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        int kind = building.Kind;
        if ((world.Frame & ShortCycleMask) == 0)
        {
            building.AlertFlags &= ~AlertBits.UnderAttack;
        }
        bool siteOrStoryKind = kind >= BuildingKindNumbers.FirstSite;
        bool busy = building.WorkMode != BuildingWorkMode.Normal;
        if (!siteOrStoryKind && kind != BuildingKindNumbers.Kitchen && kind != BuildingKindNumbers.Hut && !busy)
        {
            return;
        }
        ConstructionJob? site = siteOrStoryKind ? ConstructionOf(building) : null;
        UpgradeJob? upgrade = busy ? UpgradeOf(building) : null;
        if ((siteOrStoryKind && site == null) || (busy && upgrade == null))
        {
            return;
        }
        if (upgrade != null && IsFinished(building, upgrade))
        {
            occupancy.Evict(building, all: true);
            lifecycle.FinishUpgrade(building);
            return;
        }
        var turn = new BuildingTurn(building, kind, site, upgrade);
        OccupantScan scan = work.ScanOccupants(turn);
        if ((world.Frame & RecountCycleMask) == building.Id)
        {
            dispatch.RecountOccupants(building, scan.Occupants);
        }
        if (kind == BuildingKindNumbers.Hut && !busy && scan.Sleeper > 0)
        {
            TuckIn(world.Unit(scan.Sleeper), scan.CouplePartners);
        }
        if (site != null && building.HitPoints >= work.CompletedHitPoints(building) && building.OccupantCount == 0)
        {
            Complete(building, site);
        }
        if ((siteOrStoryKind || busy) && (world.Frame & ShortCycleMask) == (building.Id & ShortCycleMask))
        {
            dispatch.SendBuilders(building);
        }
    }

    /// <summary>
    /// Whether an upgrade or repair is over: the upgrade's work is done, the repair has restored
    /// every hit point, or cancelling was asked for.
    /// </summary>
    private static bool IsFinished(Building building, UpgradeJob job) =>
        (building.WorkMode == BuildingWorkMode.Upgrading && job.Needed > 0 && job.Done >= job.Needed)
        || (building.WorkMode == BuildingWorkMode.Repairing && building.HitPoints >= building.MaxHitPoints)
        || (building.AlertFlags & AlertBits.CancelRequested) != 0;

    /// <summary>The first bed of a hut shows whether the couple sharing it is inside together.</summary>
    private static void TuckIn(Unit sleeper, int partners)
    {
        bool together = partners == OccupantWork.BothInside;
        if (sleeper.ActionSubState == SubStates.Together && !together)
        {
            sleeper.ActionSubState = SubStates.Alone;
        }
        if (sleeper.ActionSubState == SubStates.Alone && together)
        {
            sleeper.ActionSubState = SubStates.Together;
        }
    }

    /// <summary>
    /// A complete site becomes its building in the same slot, at level 1, under a dust cloud, and
    /// leaves the construction list. Even when the tribe has no free slot left (the building is
    /// then not placed, as in the original) the site gives up its target and its list entry.
    /// </summary>
    private void Complete(Building site, ConstructionJob job)
    {
        var request = new PlacementRequest(site.Tribe, site.Aux, new GridPoint(site.X, site.Y), CompletedLevel, Immediate: true)
        {
            Slot = site.Id,
        };
        placement.Place(request);
        workLists.AddDust(site.Id, BuildingWorkLists.CompletionDust);
        site.Aux = 0;
        job.Building = 0;
    }

    private ConstructionJob? ConstructionOf(Building building)
    {
        foreach (ConstructionJob job in world.Construction)
        {
            if (job.Building == building.Id)
            {
                return job;
            }
        }
        return null;
    }

    private UpgradeJob? UpgradeOf(Building building)
    {
        foreach (UpgradeJob job in world.Upgrades)
        {
            if (job.Building == building.Id)
            {
                return job;
            }
        }
        return null;
    }
}
