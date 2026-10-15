using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// The life of a building after it is placed: destruction (it becomes a ruin under a dust cloud),
/// the slot being freed once the dust settles, and upgrades and repairs starting and ending.
/// </summary>
internal sealed class BuildingLifecycle
{
    /// <summary>The bit of <see cref="Building.AlertFlags"/> asking to cancel an upgrade or repair.</summary>
    public const int CancelUpgradeBit = 0x01;

    /// <summary>The unit a destroyed building does not throw out as its operator (as in the original).</summary>
    private const int SpareOperator = 1;

    /// <summary>Living space a tribe keeps at least after losing a hut.</summary>
    private const int MinimumLivingSpaceAfterLoss = 1;

    private readonly GameWorld world;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly Occupancy occupancy;
    private readonly CoupleLinks couples;
    private readonly BuildingWorkLists workLists;
    private readonly HintSystem hints;
    private readonly Action<GameEvent> raise;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public BuildingLifecycle(GameWorld world, Occupancy occupancy, CoupleLinks couples, BuildingWorkLists workLists,
        HintSystem hints, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.workLists = workLists ?? throw new ArgumentNullException(nameof(workLists));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// <paramref name="building"/> is destroyed: it becomes a ruin under a demolition dust cloud and
    /// throws out its occupants and its operator (unit 1 excepted, as in the original). A hut first
    /// loses the couple heading for it.
    /// </summary>
    public void Destroy(Building building) => Destroy(building, -1);

    /// <summary>
    /// <paramref name="building"/> is destroyed by <paramref name="destroyer"/> (-1 for none; see
    /// <see cref="Destroy(Building)"/>). In a match the destroyer, when an enemy, counts it at once.
    /// </summary>
    public void Destroy(Building building, int destroyer)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (building.Kind == BuildingKindNumbers.Hut)
        {
            couples.CancelCoupleAt(building);
        }
        if (world.Mode == WorldMode.Match && TribeRoles.CreditedFor(world, building.Tribe, destroyer) is >= 0 and int credited)
        {
            world.Tribes[credited][TribeCounter.BuildingsDestroyed]++;
        }
        bool focus = building.SlotInTribe == 0 && TribeRoles.IsPlayer(world, building.Tribe);
        raise(new BuildingDestroyed(building.Id, focus));
        building.HitPoints = -1;
        workLists.AddDust(building.Id, BuildingWorkLists.DemolitionDust);
        foreach (Unit occupant in occupancy.Occupants(building))
        {
            occupancy.LeaveBuilding(occupant, building, forced: true, evictOthers: false);
        }
        int holder = building.OperatorOrBeds;
        bool hasOperator = building.Kind != BuildingKindNumbers.Hut || building.WorkMode != BuildingWorkMode.Normal;
        if (holder > SpareOperator && holder <= world.UnitSlots && hasOperator)
        {
            occupancy.LeaveBuilding(world.Unit(holder), building, forced: true, evictOthers: false);
        }
    }

    /// <summary>
    /// Frees the slot of the ruin <paramref name="building"/> once its dust has settled: the footprint
    /// is cleared, a hut's living space and beds are gone, an armoury's queue and a site's place on
    /// the construction list are given up, and the tribe has one building less.
    /// </summary>
    public void Free(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        BuildingKind kind = kinds[building.Kind];
        if (building.Kind == BuildingKindNumbers.Hut)
        {
            couples.CancelCoupleAt(building);
        }
        building.HitPoints = 0;
        building.WorkMode = BuildingWorkMode.Freed;
        ClearFootprint(building, kind);
        TribeState tribe = world.Tribes[building.Tribe];
        if (building.Kind == BuildingKindNumbers.Hut)
        {
            LoseHut(building, tribe);
        }
        if (!world.Script.Active)
        {
            tribe[TribeCounter.Buildings]--;
            if (world.Mode == WorldMode.Campaign)
            {
                world.Tribes[TribeRoles.CreditedFor(world, building.Tribe, -1)][TribeCounter.BuildingsDestroyed]++;
            }
        }
        if (building.Kind == BuildingKindNumbers.Armoury)
        {
            ReleaseArmouryQueue(building, tribe);
        }
        workLists.RemoveConstructionSite(building.Id);
        if (TribeRoles.IsPlayer(world, building.Tribe))
        {
            hints.Notice(building.Tribe, EntityNotices.BuildingLost, building.X, building.Y, 0);
        }
    }

    /// <summary>
    /// Starts upgrading (<paramref name="upgrade"/>) or repairing <paramref name="building"/>: it is
    /// listed as busy, everyone inside leaves, and an upgrade's hit point limit becomes the next level's.
    /// </summary>
    public void StartUpgrade(Building building, bool upgrade)
    {
        ArgumentNullException.ThrowIfNull(building);
        workLists.AddUpgrade(building.Id);
        foreach (Unit occupant in occupancy.Occupants(building))
        {
            occupancy.LeaveBuilding(occupant, building, forced: false, evictOthers: false);
        }
        building.WorkMode = upgrade ? BuildingWorkMode.Upgrading : BuildingWorkMode.Repairing;
        if (upgrade)
        {
            building.MaxHitPoints = NextLevelHitPoints(kinds[building.Kind], building.Level);
        }
    }

    /// <summary>
    /// Ends the upgrade or repair of <paramref name="building"/>: unless it was cancelled a completion
    /// dust cloud rises and an upgrade raises the level; the operator kept aside comes back and the
    /// building leaves the list of work in progress.
    /// </summary>
    public void FinishUpgrade(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        if ((building.AlertFlags & CancelUpgradeBit) == 0)
        {
            workLists.AddDust(building.Id, BuildingWorkLists.CompletionDust);
            if (building.WorkMode == BuildingWorkMode.Upgrading)
            {
                building.Level++;
            }
        }
        building.AlertFlags &= ~CancelUpgradeBit;
        building.WorkMode = BuildingWorkMode.Normal;
        building.OperatorOrBeds = building.Aux;
        building.Aux = 0;
        foreach (UpgradeJob job in world.Upgrades)
        {
            if (job.Building == building.Id)
            {
                job.Building = 0;
                return;
            }
        }
    }

    /// <summary>
    /// The hit point limit of the level after <paramref name="level"/>; past the top level the
    /// original reads on into its building table's next row (the build costs).
    /// </summary>
    private static int NextLevelHitPoints(BuildingKind kind, int level) =>
        level < kind.MaxHitPoints.Count ? kind.MaxHitPoints[level] : kind.BuildCost[level - kind.MaxHitPoints.Count];

    private void ClearFootprint(Building building, BuildingKind kind)
    {
        for (int dy = 0; dy < kind.Height; dy++)
        {
            for (int dx = 0; dx < kind.Width; dx++)
            {
                var cell = new GridPoint(building.X + dx, building.Y + dy);
                if (world.Map.Contains(cell))
                {
                    world.Map.SetCell(cell, MapCell.Empty);
                }
            }
        }
    }

    private void LoseHut(Building hut, TribeState tribe)
    {
        tribe[TribeCounter.LivingSpace] -= BuildingPlacement.HutLivingSpace;
        if (tribe[TribeCounter.LivingSpace] < 0)
        {
            tribe[TribeCounter.LivingSpace] = MinimumLivingSpaceAfterLoss;
        }
        foreach (Unit unit in world.UnitsOf(hut.Tribe))
        {
            if (unit.Home == hut.Id)
            {
                unit.Home = 0;
            }
        }
    }

    /// <summary>Gives up the armoury's production queue (the last queue when none is the armoury's, as in the original).</summary>
    private static void ReleaseArmouryQueue(Building armoury, TribeState tribe)
    {
        ListView<ArmouryOrders> queues = tribe.Armouries;
        int index = queues.Count - 1;
        for (int i = 0; i < queues.Count; i++)
        {
            if (queues[i].Armoury == armoury.Id)
            {
                index = i;
                break;
            }
        }
        queues[index].Armoury = 0;
    }
}
