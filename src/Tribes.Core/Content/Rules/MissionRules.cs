using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>What an initial object of a mission is.</summary>
public enum InitialObjectKind
{
    /// <summary>A building of a tribe.</summary>
    Building,

    /// <summary>A unit of a tribe.</summary>
    Unit,

    /// <summary>An animal of kind 1 in a spawn point's habitat.</summary>
    Animal,
}

/// <summary>An object placed when a mission starts; only the members of its kind are set.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Cell">Where it is placed.</param>
/// <param name="Tribe">The owning tribe (buildings and units).</param>
/// <param name="BuildingKind">The building kind (buildings).</param>
/// <param name="Level">The building's level, 1..3 (buildings).</param>
/// <param name="Skill">
/// The unit's skill level; the computer's units add the difficulty (units).
/// </param>
/// <param name="Age">The unit's age (units).</param>
/// <param name="SpawnPoint">The spawn point (index within the mission) whose habitat the animal belongs to (animals).</param>
public sealed record InitialObject(InitialObjectKind Kind, GridPoint Cell, int? Tribe, int? BuildingKind,
    int? Level, int? Skill, int? Age, int? SpawnPoint);

/// <summary>A building level unlocked during a mission.</summary>
/// <param name="TimeUnits">It is unlocked at frame TimeUnits * 100.</param>
/// <param name="BuildingKind">The kind whose highest buildable level goes up by one.</param>
/// <param name="Dialogue">The dialogue shown when it happens.</param>
public sealed record TechUnlock(int TimeUnits, int BuildingKind, int Dialogue);

/// <summary>A place where animals of a mission are born.</summary>
/// <param name="Cell">The habitat's centre; animals are born on a free cell nearby.</param>
/// <param name="KindCumulativePercent">
/// For animal kinds 0..2: a roll of 0..99 gives the first kind whose cumulative percent exceeds it.
/// </param>
public sealed record AnimalSpawnPoint(GridPoint Cell, IReadOnlyList<int> KindCumulativePercent);

/// <summary>A step of a mission's story, taken in order.</summary>
/// <param name="Dialogue">The dialogue shown when the step is triggered.</param>
/// <param name="Trigger">
/// What triggers it: 1 the chief (any adult in some missions) enters <see cref="Area"/>; 3 the enemy
/// population is gone and a short time has passed; 4 and 5 timers; 6 the enemy has fewer than 5
/// buildings; 7 an adult of the player stands in <see cref="Area"/>; 8 the enemy's first building
/// is destroyed; 10 an animal or an enemy unit is killed; 0 never.
/// </param>
/// <param name="Area">The area of area triggers.</param>
public sealed record StoryStep(int Dialogue, int Trigger, CellRect Area);

/// <summary>A place the computer keeps a unit at with a long-term task.</summary>
/// <param name="Action">The task (an action).</param>
/// <param name="Cell">The task site.</param>
/// <param name="Weapon">
/// The weapon given; in hard missions 4 and later weapon 0 becomes 4 and the others 5, except
/// weapon 2 at builder posts.
/// </param>
public sealed record AiPost(int Action, GridPoint Cell, int Weapon);

/// <summary>A building the computer wants at a place.</summary>
/// <param name="BuildingKind">The kind; a site is placed when the place is empty.</param>
/// <param name="TargetLevel">It is upgraded while its level is below this.</param>
/// <param name="Cell">Its top-left cell.</param>
public sealed record AiBuildPlanEntry(int BuildingKind, int TargetLevel, GridPoint Cell);

/// <summary>What the computer orders from its armouries.</summary>
/// <param name="PrimaryWeaponQuantity">
/// Items of the first weapon in <see cref="ArmouryTables.AiWeaponOrderPriority"/> that is in stock.
/// </param>
/// <param name="SecondaryWeaponQuantity">Items of the first of the last two such weapons (thrown) in stock.</param>
/// <param name="TrapQuantities">Net, spring and pit traps, each set in one of the mission's trap areas.</param>
public sealed record AiArmouryOrder(int PrimaryWeaponQuantity, int SecondaryWeaponQuantity,
    IReadOnlyList<int> TrapQuantities);

/// <summary>The computer player's settings for one mission.</summary>
/// <param name="AttackPeriodBase">
/// Attack period T = (AttackPeriodBase - AttackPeriodHardReduction * difficulty) * 100 frames; the next
/// attack comes T / 4 + random(1.5 T) frames later.
/// </param>
/// <param name="AttackPeriodHardReduction">Subtracted from <see cref="AttackPeriodBase"/> per difficulty level.</param>
/// <param name="RampUpUnits">
/// Ramp-up time R = RampUpUnits * 500 frames: traps are ordered after R / 3, spells cast after
/// R / 2, attack sizes grow until R; custom maps attack only after R / 7.
/// </param>
/// <param name="ActivateAtStoryStep">Alarm and post handling starts at this story step.</param>
/// <param name="Posts">Posts by number.</param>
/// <param name="Home">The rally cell; reinforcements are born there too.</param>
/// <param name="BuildPlanCheckInterval">The build plan is checked every BuildPlanCheckInterval * 10 frames.</param>
/// <param name="BuilderPostCount">Posts below this number may be drafted as builders.</param>
/// <param name="ArmouryOrderInterval">Armoury orders are placed every ArmouryOrderInterval * 50 frames.</param>
/// <param name="TrapOrderChance">Traps are ordered too when a 0..63 roll is below this.</param>
/// <param name="SpellInterval">Spells are cast every SpellInterval * 50 frames.</param>
/// <param name="SpellsPerSanctuary">Disease spells queued per sanctuary.</param>
/// <param name="StoneAxeUnlockTime">
/// At frame StoneAxeUnlockTime * (1000 / (1 + 3 * difficulty)) the computer's stone axe stock is
/// set to 5000; -1 never.
/// </param>
/// <param name="MaceAndTomahawkUnlockTime">The same for bone maces and tomahawks.</param>
/// <param name="AlarmRect">A player adult entering it becomes an intruder and alarms the computer's adults.</param>
/// <param name="TerritoryRect">Idle computer units outside it walk back home.</param>
/// <param name="AttackRoutes">Attack routes (one is picked at random), each a list of waypoints.</param>
/// <param name="BuildPlan">Buildings the computer builds and upgrades.</param>
/// <param name="ArmouryOrder">What it orders from its armouries.</param>
/// <param name="TrapAreas">Areas its traps are set in (one picked at random per trap).</param>
/// <param name="SpellTargetAreas">Areas its spells are cast at.</param>
public sealed record MissionAi(int AttackPeriodBase, int AttackPeriodHardReduction, int RampUpUnits,
    int ActivateAtStoryStep, IReadOnlyList<AiPost> Posts, GridPoint Home, int BuildPlanCheckInterval,
    int BuilderPostCount, int ArmouryOrderInterval, int TrapOrderChance, int SpellInterval,
    int SpellsPerSanctuary, int StoneAxeUnlockTime, int MaceAndTomahawkUnlockTime, CellRect AlarmRect,
    CellRect TerritoryRect, IReadOnlyList<IReadOnlyList<GridPoint>> AttackRoutes,
    IReadOnlyList<AiBuildPlanEntry> BuildPlan, AiArmouryOrder ArmouryOrder, IReadOnlyList<CellRect> TrapAreas,
    IReadOnlyList<CellRect> SpellTargetAreas);

/// <summary>Settings of one mission that differ by difficulty.</summary>
/// <param name="AttackWaveBaseSize">
/// Attackers n = clamp(AttackWaveBaseSize + frame / AttackWaveGrowthDivisor / 1000, 0,
/// AttackWaveMaxSize), then n / 2 + random(n + 1) are sent.
/// </param>
/// <param name="AttackWaveMaxSize">The most attackers.</param>
/// <param name="AttackWaveGrowthDivisor">How slowly attack waves grow.</param>
/// <param name="AttackWaveUnitSkill">
/// Attackers need a fighting or scouting skill of at least half this and fight with skills of
/// this; the computer's newborns get a quarter of it.
/// </param>
/// <param name="AiBirthInterval">A computer hut has a child every AiBirthInterval * 50 frames.</param>
/// <param name="AiPopulationCap">The computer stops having children at this population.</param>
public sealed record MissionDifficulty(int AttackWaveBaseSize, int AttackWaveMaxSize, int AttackWaveGrowthDivisor,
    int AttackWaveUnitSkill, int AiBirthInterval, int AiPopulationCap);

/// <summary>Everything a mission sets up and the rules read for it.</summary>
/// <param name="Id">Mission number (as <see cref="MissionDefinition.Id"/>).</param>
/// <param name="EndConditions">
/// Bit 0: won when the enemy has no buildings left; bit 1: lost when the player's first building
/// is destroyed (the game is always lost without population). Other bits are not read.
/// </param>
/// <param name="NeedsStartFrame">Adults' needs (food, rest, mood) start changing at this frame.</param>
/// <param name="PassiveZone">
/// Missions 0..6: until the story has started, the passive tribe ignores threats inside it; null for the others.
/// </param>
/// <param name="StartUnlockLevels">Highest buildable level of building kinds 0..11 at the start.</param>
/// <param name="DefaultBuildingLevels">Level of building kinds 0..11 placed without a given level.</param>
/// <param name="TechUnlocks">Levels unlocked over time, in order.</param>
/// <param name="AnimalSpawnPoints">
/// The mission's animal spawn points; only the first <see cref="ActiveAnimalSpawnPoints"/> produce animals.
/// </param>
/// <param name="ActiveAnimalSpawnPoints">How many spawn points produce animals.</param>
/// <param name="AnimalWanderRadius">Animals roam at most this far from their habitat.</param>
/// <param name="AnimalSpawnInterval">An animal is born when frame % (AnimalSpawnInterval * 40) == 10.</param>
/// <param name="AnimalsPerSpawnPoint">Most living animals per spawn point.</param>
/// <param name="Ai">The computer player.</param>
/// <param name="Difficulties">Difficulty-dependent settings: 0 easy, 1 hard.</param>
/// <param name="InitialObjects">Objects placed at the start, in order.</param>
/// <param name="StartStock">
/// Counters 0..10 of tribes 0 and 1 at the start (missions 1 and 6 keep the previous stock).
/// </param>
/// <param name="StorySteps">The story steps, in order.</param>
/// <param name="OpeningDialogue">Missions 0..7: the dialogue shown when the mission starts; null for the others.</param>
public sealed record MissionRules(int Id, int EndConditions, int NeedsStartFrame, CellRect? PassiveZone,
    IReadOnlyList<int> StartUnlockLevels, IReadOnlyList<int> DefaultBuildingLevels,
    IReadOnlyList<TechUnlock> TechUnlocks, IReadOnlyList<AnimalSpawnPoint> AnimalSpawnPoints,
    int ActiveAnimalSpawnPoints, int AnimalWanderRadius, int AnimalSpawnInterval, int AnimalsPerSpawnPoint,
    MissionAi Ai, IReadOnlyList<MissionDifficulty> Difficulties, IReadOnlyList<InitialObject> InitialObjects,
    IReadOnlyList<IReadOnlyList<CounterAmount>> StartStock, IReadOnlyList<StoryStep> StorySteps,
    int? OpeningDialogue);

/// <summary>A tribe statistic that adds to the score.</summary>
/// <param name="Counter">The statistic's counter.</param>
/// <param name="WeightTenths">Score added per unit of the statistic, in tenths.</param>
public sealed record ScoreWeight(World.TribeCounter Counter, int WeightTenths);

/// <summary>The per-mission rules.</summary>
/// <param name="Missions">Mission rules by mission number.</param>
/// <param name="MenuMissions">The mission each entry of the mission menu starts, in menu order.</param>
/// <param name="ScoreWeights">How the statistics add up to the score.</param>
public sealed record MissionRuleTables(IReadOnlyList<MissionRules> Missions, IReadOnlyList<int> MenuMissions,
    IReadOnlyList<ScoreWeight> ScoreWeights);
