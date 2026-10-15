using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>Behaviour switches of an action.</summary>
[Flags]
public enum ActionTraits
{
    /// <summary>None of the switches.</summary>
    None = 0,

    /// <summary>When the target disappears the unit stops or picks another target.</summary>
    StopsWhenTargetGone = 1,

    /// <summary>
    /// The unit follows a moving target (and a thrower leads a moving target when aiming).
    /// </summary>
    FollowsTarget = 2,

    /// <summary>The unit does not switch to another target on its own (attacks only).</summary>
    KeepsTarget = 4,

    /// <summary>When the action completes the unit checks its needs (food, rest) before going on.</summary>
    ChecksNeedsWhenDone = 8,

    /// <summary>The effect property counts up towards the goal instead of down.</summary>
    EffectCountsUp = 16,

    /// <summary>The unit's rate is added to its rate target instead of subtracted.</summary>
    RateAdds = 32,

    /// <summary>When done the unit goes back to its task site instead of to the storehouse.</summary>
    ReturnsToTaskSite = 64,

    /// <summary>The action is kept when the unit enters a building.</summary>
    KeptOnEnteringBuilding = 128,
}

/// <summary>How an action involves a building.</summary>
public enum BuildingInteraction
{
    /// <summary>The unit acts from outside.</summary>
    Outside = 0,

    /// <summary>The unit walks to the building's entrance, enters and works inside.</summary>
    WorkInside = 1,

    /// <summary>The unit enters, visits briefly and leaves.</summary>
    Visit = 2,
}

/// <summary>An animation an action may play, picked at random.</summary>
/// <param name="Animation">The animation number (0..255).</param>
/// <param name="CumulativePercent">
/// The first choice whose cumulative percent exceeds a roll of 0..99 wins; 0 marks a choice only
/// reachable through a start offset.
/// </param>
public sealed record AnimationChoice(int Animation, int CumulativePercent);

/// <summary>
/// An action: what a unit is doing (<see cref="Unit.Action"/>) or is tasked with
/// (<see cref="Unit.AssignedTask"/>). Each action step the unit works on an effect object: its
/// <see cref="EffectProperty"/> changes by <see cref="EffectDelta"/> until it reaches
/// <see cref="EffectGoal"/>, and the unit's rate (<see cref="RateProperty"/> +
/// <see cref="RateBonus"/>) goes to <see cref="RateTarget"/>.
/// </summary>
/// <param name="Id">Action number 0..111.</param>
/// <param name="TargetAliveProperty">
/// Property of the target (<see cref="Unit.TargetIndex"/>) that is at most 0 once the target is
/// gone; <see cref="EntityProperty.UnitConstantOne"/> for actions whose target never disappears.
/// </param>
/// <param name="Traits">Behaviour switches.</param>
/// <param name="TargetPositionProperty">
/// The target's column property (the row is the next property), or null when the action has no
/// target object and the unit's own destination is used.
/// </param>
/// <param name="StandingTask">
/// The action stored as the unit's long-term task when this action is ordered (also used when the
/// unit leaves a building).
/// </param>
/// <param name="EffectGoal">
/// The action ends when the effect property reaches this value (from above, or from below with
/// <see cref="ActionTraits.EffectCountsUp"/>); a positive effect value is then set to it.
/// </param>
/// <param name="EffectProperty">
/// The property of the effect object that the action changes; <see cref="EntityProperty.UnitActionSubState"/>
/// marks actions without a work cycle.
/// </param>
/// <param name="EffectObjectProperty">
/// The unit property holding the effect object's slot, or null when the effect object is the unit itself.
/// </param>
/// <param name="RateTarget">Where the unit's rate goes each step.</param>
/// <param name="EffectDelta">
/// How much the effect property changes each step (added with <see cref="ActionTraits.EffectCountsUp"/>, else subtracted).
/// </param>
/// <param name="RateProperty">
/// The unit property the rate comes from (a work skill, or <see cref="EntityProperty.UnitConstantZero"/>);
/// when it is a work skill, the player's units within 4 cells of their chief work at 1.5 times the rate.
/// </param>
/// <param name="RateBonus">Added to <see cref="RateProperty"/>'s value.</param>
/// <param name="ActionTicks">Animation frames between two action steps.</param>
/// <param name="Range">Reach in cells (grid distance / 10 must not exceed it).</param>
/// <param name="BuildingInteraction">How the action involves its target building.</param>
/// <param name="CarriedItem">
/// The item kind the unit carries while doing the action (the amount resets when it changes); -1 keeps what it carries.
/// </param>
/// <param name="Category">The action's category (index into <see cref="ActionTables.Categories"/>).</param>
/// <param name="MovePriority">
/// A blocked unit may push into a tribe mate's cell when its priority is higher, or the mate's is 0.
/// </param>
/// <param name="TrainedStat">The stat the action trains (index into <see cref="SkillTables.TrainableStats"/>).</param>
/// <param name="TrainingChancePercent">Chance that one training check raises the stat one step.</param>
/// <param name="TrainingCap">The highest value training raises the stat to.</param>
/// <param name="TrainsEveryStep">Whether training is checked every step (otherwise once per work cycle).</param>
/// <param name="WorkCyclePoints">
/// Work progress that completes one work cycle; also the trigger value of the spell actions' cast animation.
/// </param>
/// <param name="SatietyDecayPeriod">
/// Frames between two points of satiety lost (five times longer for the computer's tribe); 127 never.
/// </param>
/// <param name="HappinessDelta">
/// Added to the unit's happiness at each mood update; a negative value costs 3 more without a home.
/// </param>
/// <param name="EnergyDecayPeriod">
/// Frames between two points of energy lost (five times longer for the computer's tribe); 127
/// never. Animations 0..14 use their own period (<see cref="AnimationDefinition.EnergyDecayPeriod"/>).
/// </param>
/// <param name="NextActionOnComplete">The action switched to when this one completes.</param>
/// <param name="PauseVariant">
/// Which pause variant (<see cref="ActionTables.PauseVariants"/>) an idle unit doing this action
/// takes when it pauses; -1 never pauses.
/// </param>
/// <param name="AnimationChoices">The animations the action plays, picked at random.</param>
/// <param name="AttackTargetCategory">
/// For the basic attacks 0..9: the cell category (<see cref="TargetTables.CellCategories"/>) the
/// attack expects; null for other actions.
/// </param>
/// <param name="ThreatThreshold">
/// For the basic attacks 0..9: a unit doing it turns on a threat whose priority is higher than
/// this (or that is within reach); null for other actions.
/// </param>
/// <param name="ProjectileImpactDrop">
/// For the ranged attacks 5..9: pixels the projectile's height drops during the flight; null for other actions.
/// </param>
public sealed record ActionDefinition(int Id, EntityProperty TargetAliveProperty, ActionTraits Traits,
    EntityProperty? TargetPositionProperty, int StandingTask, int EffectGoal, EntityProperty EffectProperty,
    EntityProperty? EffectObjectProperty, RateTarget RateTarget, ValueSource EffectDelta,
    EntityProperty RateProperty, int RateBonus, int ActionTicks, ValueSource Range,
    BuildingInteraction BuildingInteraction, int CarriedItem, int Category, int MovePriority,
    int TrainedStat, int TrainingChancePercent, int TrainingCap, bool TrainsEveryStep,
    int WorkCyclePoints, int SatietyDecayPeriod, int HappinessDelta, int EnergyDecayPeriod,
    int NextActionOnComplete, int PauseVariant, IReadOnlyList<AnimationChoice> AnimationChoices,
    int? AttackTargetCategory, int? ThreatThreshold, int? ProjectileImpactDrop);

/// <summary>
/// A category of actions. 0 free or unassigned; 1..7 gathering and delivering one carried item
/// each; 8 armoury work; 9 kitchen (satiety rises instead of falling); 10 disco; 11 sanctuary
/// (a witch doctor heals slowly); 12 construction and upgrades; 13 pia center; 14 gym; 15 range;
/// 16 tower guard.
/// </summary>
/// <param name="Id">Category number.</param>
/// <param name="ServiceGroup">
/// Workplace group: 0 none, 1 kitchen, 2 construction, 4 sanctuary, 5 disco. A unit whose task's
/// group differs from its action's (non-zero) group leaves the building; group 2 units can be
/// drafted as builders.
/// </param>
/// <param name="CallToArmsWeight">
/// Weight in a call to arms: units whose action and task weights add up to more than 1 are
/// mustered (1 idle, 2 training, 0 busy).
/// </param>
public sealed record ActionCategory(int Id, int ServiceGroup, int CallToArmsWeight);

/// <summary>A pause variant: the range a pausing unit's sub-state is rolled in.</summary>
/// <param name="Id">Variant number.</param>
/// <param name="MinSubState">Smallest sub-state.</param>
/// <param name="MaxSubState">Largest sub-state.</param>
public sealed record PauseVariantRange(int Id, int MinSubState, int MaxSubState);

/// <summary>The action table and the tables indexed by action category.</summary>
/// <param name="Actions">Actions by number.</param>
/// <param name="Categories">Action categories by number.</param>
/// <param name="PauseVariants">Pause variants by number.</param>
/// <param name="PauseAnimationOffsetBySubState">
/// For the waiting action 84: the first animation choice used, by the unit's sub-state.
/// </param>
public sealed record ActionTables(IReadOnlyList<ActionDefinition> Actions, IReadOnlyList<ActionCategory> Categories,
    IReadOnlyList<PauseVariantRange> PauseVariants, IReadOnlyList<int> PauseAnimationOffsetBySubState);
