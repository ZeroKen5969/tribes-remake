using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Vision;
using Tribes.Core.Systems.WorldObjects;

namespace Tribes.Core.Simulation;

/// <summary>The world services: cell queries, fog of war, the sector index, routes, world objects, unlocks and hints.</summary>
public sealed partial class RuleSystems
{
    private GridDistances? gridDistances;
    private TargetDistances? targetDistances;
    private Passability? passability;
    private CellClassifier? cellClassifier;
    private TargetChecks? targetChecks;
    private SectorSearch? sectorSearch;
    private SightSystem? sight;
    private RouteFinder? routes;
    private WildlifeAndNodes? wildlifeAndNodes;
    private BuildingWorkLists? buildingWorkLists;
    private UnlockLevels? unlockLevels;
    private HintSystem? hints;

    /// <summary>The movement table's distance measure.</summary>
    internal GridDistances GridDistances => gridDistances ??= new GridDistances(World.Content.Rules.Movement);

    /// <summary>Where units and animals are heading and how far it is.</summary>
    internal TargetDistances TargetDistances => targetDistances ??= new TargetDistances(World, GridDistances);

    /// <summary>What blocks units and animals; what the player knows of a cell.</summary>
    internal Passability Passability => passability ??= new Passability(World, GridDistances);

    /// <summary>Cell categories as orders and attacks see them.</summary>
    internal CellClassifier CellClassifier => cellClassifier ??= new CellClassifier(World);

    /// <summary>Whether a unit accepts an order to a cell.</summary>
    internal TargetChecks TargetChecks => targetChecks ??= new TargetChecks(World);

    /// <summary>The sector index: rebuilding, target searches and alarms.</summary>
    internal SectorSearch SectorSearch => sectorSearch ??= new SectorSearch(World, GridDistances);

    /// <summary>The player's fog of war.</summary>
    internal SightSystem Sight => sight ??= new SightSystem(World, Passability);

    /// <summary>Route planning.</summary>
    internal RouteFinder Routes => routes ??= new RouteFinder(World, Passability, TargetDistances);

    /// <summary>Resource nodes and animals placed and removed by the world.</summary>
    internal WildlifeAndNodes WildlifeAndNodes => wildlifeAndNodes ??= new WildlifeAndNodes(World);

    /// <summary>Dust clouds, construction sites and upgrades in progress.</summary>
    internal BuildingWorkLists BuildingWorkLists => buildingWorkLists ??= new BuildingWorkLists(World, CoupleLinks);

    /// <summary>The buildable level of each building kind.</summary>
    internal UnlockLevels UnlockLevels => unlockLevels ??= new UnlockLevels(World);

    /// <summary>Hints and notices for the player.</summary>
    internal HintSystem Hints => hints ??= new HintSystem(World, Raise);
}
