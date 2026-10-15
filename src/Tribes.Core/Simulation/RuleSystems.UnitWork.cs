using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.Systems.Units.Work;

namespace Tribes.Core.Simulation;

/// <summary>Units at work and in combat: decisions, targets, work steps and their results, blows and projectiles.</summary>
public sealed partial class RuleSystems
{
    private Projectiles? projectiles;
    private MeleeBlows? meleeBlows;
    private WorkEffects? workEffects;
    private ActionTicks? actionTicks;
    private UnitGoals? unitGoals;
    private NearestBuildings? nearestBuildings;
    private TargetCycle? targetCycle;
    private ActionPauses? actionPauses;
    private WorkMaterials? workMaterials;
    private WorkplaceEffects? workplaceEffects;
    private ArmouryDeliveries? armouryDeliveries;
    private NodeGathering? nodeGathering;
    private Spells? spells;
    private StoryEncounters? storyEncounters;
    private ThreatWatch? threatWatch;
    private NeedsChoices? needsChoices;
    private WorkCompletion? workCompletion;
    private WorkSteps? workSteps;
    private ActionSteps? actionSteps;
    private UnitDecisions? unitDecisions;
    private OrderFollowing? orderFollowing;
    private UnitUpdate? unitUpdate;

    /// <summary>Thrown projectiles: throwing, flight and landing.</summary>
    internal Projectiles Projectiles => projectiles ??= new Projectiles(World, Routes, GridDistances, CellDamage, Raise);

    /// <summary>Melee blows.</summary>
    internal MeleeBlows MeleeBlows => meleeBlows ??= new MeleeBlows(World, CellDamage, CellClassifier);

    /// <summary>One step of an action's work.</summary>
    internal WorkEffects WorkEffects => workEffects ??= new WorkEffects(World);

    /// <summary>How far units are from their goals.</summary>
    internal UnitGoals UnitGoals => unitGoals ??= new UnitGoals(World, GridDistances);

    /// <summary>One frame of a unit's action animation.</summary>
    internal ActionTicks ActionTicks => actionTicks ??= new ActionTicks(World, MeleeBlows, Projectiles, WorkEffects);

    /// <summary>The nearest usable building of a kind.</summary>
    internal NearestBuildings NearestBuildings =>
        nearestBuildings ??= new NearestBuildings(World, UnitActions, UnitOrders, Hints);

    /// <summary>Routes to goals, next targets and the work orders of armouries and sanctuaries.</summary>
    internal TargetCycle TargetCycle =>
        targetCycle ??= new TargetCycle(World, UnitActions, UnitOrders, Routes, TargetDistances, NearbySearches, NearestBuildings);

    /// <summary>Units pausing their action for a rest.</summary>
    internal ActionPauses ActionPauses => actionPauses ??= new ActionPauses(World, UnitActions);

    /// <summary>Units fetching from the stock and taking weapons.</summary>
    internal WorkMaterials WorkMaterials => workMaterials ??= new WorkMaterials(World, Hints);

    /// <summary>Training, entertainment and healing in buildings.</summary>
    internal WorkplaceEffects WorkplaceEffects => workplaceEffects ??= new WorkplaceEffects(World, UnitLifecycle);

    /// <summary>Armoury products delivered and traps set.</summary>
    internal ArmouryDeliveries ArmouryDeliveries => armouryDeliveries ??= new ArmouryDeliveries(World);

    /// <summary>Gatherers going on with their work.</summary>
    internal NodeGathering NodeGathering =>
        nodeGathering ??= new NodeGathering(World, UnitActions, UnitOrders, GridDistances, NearbySearches, TargetCycle);

    /// <summary>The witch doctor's spells.</summary>
    internal Spells Spells => spells ??= new Spells(World, UnitLifecycle, Courtship);

    /// <summary>Story steps reached by meeting an enemy in an area.</summary>
    internal StoryEncounters StoryEncounters => storyEncounters ??= new StoryEncounters(World, StoryProgress, Council, NearbySearches);

    /// <summary>Units noticing threats and responding to them.</summary>
    internal ThreatWatch ThreatWatch => threatWatch ??= new ThreatWatch(World, SectorSearch, Routes, TargetDistances, UnitActions,
        UnitOrders, Hints, ActionAnimation, StoryEncounters, TargetCycle);

    /// <summary>Adults seeing to their needs.</summary>
    internal NeedsChoices NeedsChoices => needsChoices ??= new NeedsChoices(World, UnitActions, UnitOrders, NearestBuildings, Hints, Courtship);

    /// <summary>What happens when an action is done, and taking up the next one.</summary>
    internal WorkCompletion WorkCompletion => workCompletion ??= new WorkCompletion(World, UnitActions, UnitOrders, CoupleLinks,
        UnitLifecycle, Deaths, new CompletionServices(ThreatWatch, TargetCycle, NeedsChoices, NodeGathering, NearestBuildings,
            WorkMaterials, ArmouryDeliveries, Courtship));

    /// <summary>One action step of a unit and what it leads to.</summary>
    internal WorkSteps WorkSteps => workSteps ??= new WorkSteps(World, WorkEffects, WorkplaceEffects, UnitLifecycle, WorkMaterials,
        ActionPauses, TargetCycle, Spells, WorkCompletion);

    /// <summary>The end of an action step.</summary>
    internal ActionSteps ActionSteps =>
        actionSteps ??= new ActionSteps(World, Routes, TargetDistances, WorkMaterials, WorkSteps, TargetCycle, ActionAnimation);

    /// <summary>Units deciding while they stand on a cell centre.</summary>
    internal UnitDecisions UnitDecisions => unitDecisions ??= new UnitDecisions(World, UnitActions, UnitOrders, Routes,
        TargetDistances, WorkPositions, new DecisionServices(TargetCycle, ThreatWatch, NeedsChoices, NodeGathering,
            NearestBuildings, Spells, Courtship, ActionAnimation));

    /// <summary>Units carrying out orders.</summary>
    internal OrderFollowing OrderFollowing =>
        orderFollowing ??= new OrderFollowing(World, UnitActions, UnitOrders, TargetDistances, Hints, Interruptions);

    /// <summary>The units' part of each frame.</summary>
    internal UnitUpdate UnitUpdate => unitUpdate ??= new UnitUpdate(World, UnitGoals, OrderFollowing, UnitDecisions, ThreatWatch,
        TargetCycle, Projectiles, new UnitFrameParts(FrameStart, ComputerPlayer, HutBirths, UnitHousekeeping, UnitStealth,
            UnitThoughts, AnimationDriver, MovementDriver));
}
