using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Wildlife;

namespace Tribes.Core.Simulation;

/// <summary>Wild animals, and the spiral search, blows and deaths they share with the units' rules.</summary>
public sealed partial class RuleSystems
{
    private SpiralSearch? spiralSearch;
    private NearbySearches? nearbySearches;
    private StoryProgress? storyProgress;
    private Deaths? deaths;
    private CellDamage? cellDamage;
    private AnimalSenses? animalSenses;
    private AnimalBehaviour? animalBehaviour;
    private AnimalMovement? animalMovement;
    private AnimalSpawning? animalSpawning;
    private WildlifeUpdate? wildlifeUpdate;

    /// <summary>The spiral search around a cell ("the nearest cell that fits").</summary>
    internal SpiralSearch SpiralSearch => spiralSearch ??= new SpiralSearch(World, GridDistances);

    /// <summary>The spiral searches units and scenes make (more work, open ground, a couple's spot, gathered adults).</summary>
    internal NearbySearches NearbySearches =>
        nearbySearches ??= new NearbySearches(World, SpiralSearch, UnitActions, Routes, TargetDistances, UnitOrders);

    /// <summary>Plays the story scenes rules start: always the scene player.</summary>
    internal IStoryDirector StoryDirector => Scenes;

    /// <summary>The mission's story steps and the casting of scenes.</summary>
    internal StoryProgress StoryProgress => storyProgress ??= new StoryProgress(World, StoryDirector);

    /// <summary>Deaths of animals, units and buildings.</summary>
    internal Deaths Deaths =>
        deaths ??= new Deaths(World, WildlifeAndNodes, UnitLifecycle, Hints, StoryProgress, BuildingLifecycle.Destroy);

    /// <summary>Blows struck on cells.</summary>
    internal CellDamage CellDamage =>
        cellDamage ??= new CellDamage(World, GridDistances, CellClassifier, SectorSearch, Occupancy, Hints, Deaths, Raise);

    /// <summary>What animals notice and where they go.</summary>
    internal AnimalSenses AnimalSenses => animalSenses ??= new AnimalSenses(World, SpiralSearch);

    /// <summary>Animals' decisions.</summary>
    internal AnimalBehaviour AnimalBehaviour =>
        animalBehaviour ??= new AnimalBehaviour(World, AnimalSenses, GridDistances, TargetDistances, Routes, CellDamage);

    /// <summary>Animals walking.</summary>
    internal AnimalMovement AnimalMovement => animalMovement ??= new AnimalMovement(World);

    /// <summary>Animals being born.</summary>
    internal AnimalSpawning AnimalSpawning => animalSpawning ??= new AnimalSpawning(World, SpiralSearch, WildlifeAndNodes, EndlessWildlife);

    /// <summary>The wildlife part of each frame.</summary>
    internal WildlifeUpdate WildlifeUpdate =>
        wildlifeUpdate ??= new WildlifeUpdate(World, AnimalSpawning, AnimalBehaviour, AnimalMovement);
}
