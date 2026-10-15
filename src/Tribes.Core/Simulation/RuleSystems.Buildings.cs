using Tribes.Core.Systems.Buildings;

namespace Tribes.Core.Simulation;

/// <summary>The buildings' part of the frame: work inside buildings, builders, finished sites and upgrades.</summary>
public sealed partial class RuleSystems
{
    private StoreKeeping? storeKeeping;
    private OccupantWork? occupantWork;
    private WorkerDispatch? workerDispatch;
    private BuildingUpdate? buildingUpdate;

    /// <summary>Goods taken from the tribes' stocks.</summary>
    internal StoreKeeping StoreKeeping => storeKeeping ??= new StoreKeeping(World, Hints);

    /// <summary>The work of the units inside buildings.</summary>
    internal OccupantWork OccupantWork => occupantWork ??= new OccupantWork(World, UnitActions, StoreKeeping);

    /// <summary>Occupant counts and builders sent out.</summary>
    internal WorkerDispatch WorkerDispatch => workerDispatch ??= new WorkerDispatch(World);

    /// <summary>The buildings' part of each frame.</summary>
    internal BuildingUpdate BuildingUpdate =>
        buildingUpdate ??= new BuildingUpdate(World, SectorSearch, Hints, OccupantWork, WorkerDispatch, Occupancy,
            BuildingLifecycle, BuildingPlacement, BuildingWorkLists);
}
