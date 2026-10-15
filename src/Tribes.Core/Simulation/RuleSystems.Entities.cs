using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;

namespace Tribes.Core.Simulation;

/// <summary>The entity systems: unit actions, occupancy of buildings, unit and building lifecycles.</summary>
public sealed partial class RuleSystems
{
    private UnitActions? unitActions;
    private CoupleLinks? coupleLinks;
    private BuildingSlotTables? buildingSlots;
    private Occupancy? occupancy;
    private UnitSpawning? unitSpawning;
    private UnitLifecycle? unitLifecycle;
    private BuildingPlacement? buildingPlacement;
    private BuildingLifecycle? buildingLifecycle;
    private TargetActionTable? targetActions;
    private UnitOrders? unitOrders;

    /// <summary>Switching unit actions, carried items, walking and standing.</summary>
    internal UnitActions UnitActions => unitActions ??= new UnitActions(World);

    /// <summary>Dissolving couples.</summary>
    internal CoupleLinks CoupleLinks => coupleLinks ??= new CoupleLinks(World, UnitActions);

    /// <summary>The work position tables of all building kinds.</summary>
    internal BuildingSlotTables BuildingSlots => buildingSlots ??= new BuildingSlotTables(World.Content.Rules.Buildings.Kinds);

    /// <summary>Homes, work positions, tower ranges and leaving buildings.</summary>
    internal Occupancy Occupancy => occupancy ??= new Occupancy(World, BuildingSlots);

    /// <summary>New units, portraits and names.</summary>
    internal UnitSpawning UnitSpawning => unitSpawning ??= new UnitSpawning(World, Hints, Sight);

    /// <summary>Hit points, training, death and changing tribe.</summary>
    internal UnitLifecycle UnitLifecycle =>
        unitLifecycle ??= new UnitLifecycle(World, CoupleLinks, Occupancy, UnitSpawning, Hints, Raise);

    /// <summary>Placing buildings, construction sites and traps.</summary>
    internal BuildingPlacement BuildingPlacement =>
        buildingPlacement ??= new BuildingPlacement(World, BuildingWorkLists, CoupleLinks, Passability, Sight, Hints);

    /// <summary>Destroying buildings, freeing their slots, upgrades and repairs.</summary>
    internal BuildingLifecycle BuildingLifecycle =>
        buildingLifecycle ??= new BuildingLifecycle(World, Occupancy, CoupleLinks, BuildingWorkLists, Hints, Raise);

    /// <summary>The action for each cell category and sub-kind.</summary>
    internal TargetActionTable TargetActions => targetActions ??= new TargetActionTable(World.Content.Rules);

    /// <summary>Orders ending: picking what to do at the destination, settling inside buildings.</summary>
    internal UnitOrders UnitOrders => unitOrders ??= new UnitOrders(World, UnitActions, CoupleLinks, Occupancy, BuildingSlots,
        TargetActions, new OrderServices(CellClassifier, TargetChecks, TargetDistances, Passability, Hints));

    /// <summary>
    /// The player of <paramref name="tribe"/> orders a building of <paramref name="kind"/> (0..11) with
    /// its top-left cell at <paramref name="cell"/>: the kind's construction site is placed if its
    /// footprint is free and in view. Returns the site's slot, or 0 when it could not be placed.
    /// </summary>
    public int OrderConstruction(int tribe, int kind, GridPoint cell)
    {
        bool alliance = World.Endless != null && Systems.Endless.AllianceBuilding.Is(kind);
        if ((kind < 0 || kind >= BuildingKindNumbers.PlannedKinds) && !alliance)
        {
            throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "only building kinds 0..11 (and the alliance building in an endless world) are built from sites");
        }
        int site = World.Content.Rules.Buildings.Kinds[kind].SiteKind;
        var request = new PlacementRequest(tribe, site, cell, PlacementRequest.OrderedLevel, Immediate: false) { SiteTarget = kind };
        PlacementResult result = BuildingPlacement.Place(request);
        return result.Outcome == PlacementOutcome.Placed ? result.Slot : 0;
    }

    /// <summary>
    /// The player of <paramref name="tribe"/> orders a trap of <paramref name="trapKind"/> at
    /// <paramref name="cell"/> from <paramref name="armoury"/>, whose production queue
    /// <paramref name="queue"/> makes it. Returns whether the order was taken.
    /// </summary>
    public bool OrderTrap(int tribe, int trapKind, GridPoint cell, int armoury, int queue) =>
        BuildingPlacement.Place(new PlacementRequest(tribe, trapKind, cell, PlacementRequest.OrderedLevel, Immediate: false)
        {
            TrapOwner = armoury, TrapQueue = queue,
        })
            .Outcome == PlacementOutcome.Placed;

    /// <summary>Demolishes <paramref name="building"/>: it becomes a ruin and its slot is freed when the dust settles.</summary>
    public void Demolish(int building) => BuildingLifecycle.Destroy(World.Building(building));

    /// <summary>Starts upgrading (<paramref name="upgrade"/>) or repairing <paramref name="building"/>.</summary>
    public void StartBuildingWork(int building, bool upgrade) => BuildingLifecycle.StartUpgrade(World.Building(building), upgrade);
}
