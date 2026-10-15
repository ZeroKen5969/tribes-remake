using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// A unit stat that actions and buildings can train (speed, fighting skills, maximum hit
/// points, carrying capacity, sight, work skills).
/// </summary>
/// <param name="Id">Stat number, as <see cref="ActionDefinition.TrainedStat"/> and <see cref="TrainingEffect.Stat"/> give it.</param>
/// <param name="Property">
/// The unit property trained. Training <see cref="EntityProperty.UnitMaxHitPoints"/> also heals by
/// the same amount, up to the age limit.
/// </param>
/// <param name="ActionTrainingStep">
/// How much one successful training by an action changes the property (-1 for move delay: smaller is faster).
/// </param>
public sealed record TrainableStat(int Id, EntityProperty Property, int ActionTrainingStep);

/// <summary>A skill as the unit panel and the computer's training choose it.</summary>
/// <param name="Id">Skill number: 0 melee, 1 ranged, 2 scouting, 3..8 the work skills; 9 and 10 are fillers.</param>
/// <param name="Property">The unit property holding the skill.</param>
/// <param name="TaskIcon">The task icon shown for the skill, or null for the fillers.</param>
public sealed record SkillKind(int Id, EntityProperty Property, int? TaskIcon);

/// <summary>Where the computer sends a unit to train a fighting skill.</summary>
/// <param name="Skill">The skill trained (index into <see cref="SkillTables.Skills"/>).</param>
/// <param name="BuildingKind">The building kind that trains it.</param>
/// <param name="Action">The action started there.</param>
public sealed record TrainingTarget(int Skill, int BuildingKind, int Action);

/// <summary>Skill and stat tables.</summary>
/// <param name="TrainableStats">Trainable stats by number.</param>
/// <param name="Skills">Skills by number.</param>
/// <param name="TrainingTargets">Training places for skills 0..2 (melee, ranged, scouting).</param>
public sealed record SkillTables(IReadOnlyList<TrainableStat> TrainableStats,
    IReadOnlyList<SkillKind> Skills, IReadOnlyList<TrainingTarget> TrainingTargets);
