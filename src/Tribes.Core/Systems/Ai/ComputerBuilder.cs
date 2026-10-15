using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The computer's building plan: buildings that stand at their planned place are repaired when
/// badly damaged and upgraded to their planned level; an empty planned place is remembered and a
/// construction site is put there. In a match a planned place is only taken up while the
/// building fits there now (the plan was laid onto a map it was not made for).
/// </summary>
internal sealed class ComputerBuilder
{
    /// <summary>Repairs are checked on frames where these bits are 0; upgrades on the plan's own interval.</summary>
    private const int RepairMask = 31;

    /// <summary>The build plan interval is given in tens of frames.</summary>
    private const int IntervalUnit = 10;

    /// <summary>A building is repaired below this fraction of its maximum hit points (1 / RepairDivisor).</summary>
    private const int RepairDivisor = 4;

    /// <summary>No plan entry is waiting to be rebuilt.</summary>
    public const int NoRebuild = -1;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;
    private readonly BuildingLifecycle lifecycle;
    private readonly BuildingPlacement placement;

    /// <summary>Builds for the computer tribes in <paramref name="world"/>.</summary>
    public ComputerBuilder(GameWorld world, ComputerPlans plans, BuildingLifecycle lifecycle, BuildingPlacement placement)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
    }

    /// <summary>
    /// The number of the first plan entry of the current mission: plan entries are numbered across
    /// the build plans of all missions in order (the waiting entry is kept by that number).
    /// </summary>
    public int FirstEntryNumber()
    {
        int first = 0;
        IReadOnlyList<MissionRules> missions = world.Content.Rules.Missions.Missions;
        for (int m = 0; m < world.Mission; m++)
        {
            first += missions[m].Ai.BuildPlan.Count;
        }
        return first;
    }

    /// <summary>Checks the build plan of <paramref name="tribe"/> (every 32 frames for repairs, and at the plan's interval).</summary>
    public void CheckBuildPlan(int tribe)
    {
        MissionAi ai = plans.For(tribe);
        AiState state = TribeRoles.Ai(world, tribe);
        int clock = Endless.EndlessPacing.Clock(world, tribe);
        bool repairFrame = (clock & RepairMask) == 0;
        if (!repairFrame && clock % (ai.BuildPlanCheckInterval * IntervalUnit) != 0)
        {
            return;
        }
        int firstNumber = FirstEntryNumber();
        for (int i = 0; i < ai.BuildPlan.Count; i++)
        {
            CheckEntry(tribe, state, ai.BuildPlan[i], firstNumber + i, repairFrame);
        }
        int waiting = state.RebuildSlot & 0xFF;
        if (state.RebuildSlot == NoRebuild || waiting - firstNumber < 0 || waiting - firstNumber >= ai.BuildPlan.Count)
        {
            return;
        }
        AiBuildPlanEntry entry = ai.BuildPlan[waiting - firstNumber];
        int site = world.Content.Rules.Buildings.Kinds[entry.BuildingKind].SiteKind;
        var request = new PlacementRequest(tribe, site, entry.Cell, BuildingPlacement.SiteLevel, Immediate: true)
        {
            SiteTarget = entry.BuildingKind,
        };
        if (placement.Place(request).Outcome == PlacementOutcome.Placed || world.Mode == WorldMode.Match)
        {
            state.RebuildSlot = NoRebuild;
        }
    }

    private void CheckEntry(int tribe, AiState state, AiBuildPlanEntry entry, int number, bool repairFrame)
    {
        MapCell cell = world.Map.CellAt(entry.Cell);
        if (cell.Content == CellContent.Building && world.TribeOfBuilding(cell.Index) == tribe)
        {
            Building building = world.Building(cell.Index);
            if (building.Kind != entry.BuildingKind || building.HitPoints <= 0 || building.WorkMode != BuildingWorkMode.Normal)
            {
                return;
            }
            if (repairFrame)
            {
                if (building.HitPoints < building.MaxHitPoints / RepairDivisor)
                {
                    lifecycle.StartUpgrade(building, upgrade: false);
                }
            }
            else if (building.Level < entry.TargetLevel)
            {
                lifecycle.StartUpgrade(building, upgrade: true);
            }
        }
        else if (cell.IsEmpty && !repairFrame && state.RebuildSlot == NoRebuild && FitsNow(tribe, entry))
        {
            state.RebuildSlot = number;
        }
    }

    /// <summary>Whether the site of <paramref name="entry"/> can be put at its place now (always so in the campaign, as there).</summary>
    private bool FitsNow(int tribe, AiBuildPlanEntry entry)
    {
        if (world.Mode == WorldMode.Campaign)
        {
            return true;
        }
        int site = world.Content.Rules.Buildings.Kinds[entry.BuildingKind].SiteKind;
        return placement.FootprintFree(site, entry.Cell, inView: false, tribe);
    }
}
