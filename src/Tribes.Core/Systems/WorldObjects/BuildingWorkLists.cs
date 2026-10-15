using System;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.WorldObjects;

/// <summary>
/// The world's lists of work on buildings: dust clouds over finished or demolished buildings,
/// construction sites waiting for material, and upgrades or repairs in progress.
/// </summary>
internal sealed class BuildingWorkLists
{
    /// <summary>A dust cloud over a building that was completed, upgraded or repaired.</summary>
    public const int CompletionDust = 0;

    /// <summary>A dust cloud over a demolished building; its slot is freed when the dust settles.</summary>
    public const int DemolitionDust = 3;

    /// <summary>Frames a dust cloud lasts.</summary>
    public const int DustFrames = 20;

    private readonly GameWorld world;
    private readonly CoupleLinks couples;

    /// <summary>Keeps the lists of <paramref name="world"/>; <paramref name="couples"/> sends away the couple a hut was given to.</summary>
    public BuildingWorkLists(GameWorld world, CoupleLinks couples)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
    }

    /// <summary>
    /// Starts a dust cloud of <paramref name="kind"/> over <paramref name="building"/> in the first
    /// free slot (in the campaign none when all are taken; a match's table grows).
    /// </summary>
    public void AddDust(int building, int kind)
    {
        if (world.FreeDustCloud() is { } cloud)
        {
            cloud.Frames = 0;
            cloud.Kind = kind;
            cloud.Building = building;
        }
    }

    /// <summary>
    /// One frame of every dust cloud: a cloud that has lasted <see cref="DustFrames"/> frames ends,
    /// and a demolished building's slot is then freed for good by <paramref name="freeSlot"/>.
    /// </summary>
    public void AdvanceDust(Action<Building> freeSlot)
    {
        ArgumentNullException.ThrowIfNull(freeSlot);
        foreach (DustCloud cloud in world.Dust)
        {
            if (cloud.Building == 0)
            {
                continue;
            }
            if (cloud.Frames >= DustFrames)
            {
                Building building = world.Building(cloud.Building);
                cloud.Building = 0;
                if (cloud.Kind == DemolitionDust)
                {
                    freeSlot(building);
                    building.WorkMode = BuildingWorkMode.Freed;
                }
            }
            cloud.Frames++;
        }
    }

    /// <summary>
    /// Lists construction site <paramref name="building"/> as waiting for material; false when the
    /// list is full (only in the campaign: a match's list grows, so no tribe's sites crowd out another's).
    /// </summary>
    public bool AddConstructionSite(int building)
    {
        if (world.FreeConstructionJob() is not { } job)
        {
            return false;
        }
        job.Building = building;
        job.Trips = 0;
        world.Building(building).DispatchedWorkers = 0;
        return true;
    }

    /// <summary>Takes <paramref name="building"/> off the construction list.</summary>
    public void RemoveConstructionSite(int building)
    {
        foreach (ConstructionJob job in world.Construction)
        {
            if (job.Building == building)
            {
                job.Building = 0;
            }
        }
    }

    /// <summary>
    /// Lists an upgrade or repair of <paramref name="building"/>: its operator steps aside (kept to
    /// come back afterwards), no workers are sent yet, and a hut gives up the couple it was given.
    /// False when the list is full (only in the campaign: a match's list grows).
    /// </summary>
    public bool AddUpgrade(int building)
    {
        if (world.FreeUpgradeJob() is not { } job)
        {
            return false;
        }
        job.Building = building;
        job.Done = 0;
        job.Needed = 0;
        Building target = world.Building(building);
        target.Aux = target.OperatorOrBeds;
        target.OperatorOrBeds = 0;
        target.DispatchedWorkers = 0;
        if (target.Kind == BuildingKindNumbers.Hut)
        {
            couples.CancelCoupleAt(target);
        }
        return true;
    }
}
