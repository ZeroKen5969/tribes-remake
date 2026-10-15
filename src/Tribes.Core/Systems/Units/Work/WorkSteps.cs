using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// One action step of a unit and what it leads to: the work step, training, a finished work cycle
/// (a product, a spell, a gathered item), and the end of the action when its effect reaches the goal.
/// </summary>
internal sealed partial class WorkSteps
{
    /// <summary>Standing animations; a unit standing does no work (unless it hunts or leaves).</summary>
    private const int FirstStanding = 8, LastStanding = 14;

    /// <summary>The entertainer's dancing animation; each step of it cheers the others up.</summary>
    private const int DancingAnimation = 117;

    /// <summary>A unit waiting for material whose check passed has progress 100.</summary>
    private const int WaitForMaterial = 84, MaterialReady = 100;

    /// <summary>The eating actions, and how they use up meals: one per (100 / ((max hit points + 12) / 12)) satiety.</summary>
    private const int FirstEating = 70, LastEating = 71, AppetiteBase = 12, AppetiteStep = 12, Percent = 100, BiteWindow = 4;

    /// <summary>Fishing: the catch animation numbers below this cast the line first.</summary>
    private const int CastLine = -110, FirstCatchAnimation = 145, FishingProgress = 99;

    private readonly GameWorld world;
    private readonly WorkEffects effects;
    private readonly WorkplaceEffects places;
    private readonly UnitLifecycle lifecycle;
    private readonly WorkMaterials materials;
    private readonly ActionPauses pauses;
    private readonly TargetCycle targets;
    private readonly Spells spells;
    private readonly WorkCompletion completion;
    private readonly IReadOnlyList<ActionDefinition> definitions;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Runs the work cycles of <paramref name="world"/>'s units.</summary>
    public WorkSteps(GameWorld world, WorkEffects effects, WorkplaceEffects places, UnitLifecycle lifecycle,
        WorkMaterials materials, ActionPauses pauses, TargetCycle targets, Spells spells, WorkCompletion completion)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
        this.places = places ?? throw new ArgumentNullException(nameof(places));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.materials = materials ?? throw new ArgumentNullException(nameof(materials));
        this.pauses = pauses ?? throw new ArgumentNullException(nameof(pauses));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.spells = spells ?? throw new ArgumentNullException(nameof(spells));
        this.completion = completion ?? throw new ArgumentNullException(nameof(completion));
        definitions = world.Content.Rules.Actions.Actions;
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// One action step of the turn's unit. A fetch takes its goods (or the unit pauses and looks for
    /// another target when the stock is short); other actions do their work step. When the action is
    /// done the unit finishes it and takes up the next one. Returns whether that happened.
    /// </summary>
    public bool Step(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        bool standing = unit.AnimationNumber is >= FirstStanding and <= LastStanding;
        if ((standing && unit.Action is not (ActionNumbers.HuntingTask or ActionNumbers.LeaveBuilding))
            || (unit.Action > ActionNumbers.LastBasicAttack && !PlaysOwnAnimation(unit)))
        {
            return false;
        }
        bool done;
        if (IsFetch(unit.Action))
        {
            done = materials.Fetch(unit);
            if (!done)
            {
                pauses.Pause(unit);
                targets.NextTarget(turn);
            }
        }
        else
        {
            CycleOutcome outcome = WorkStep(turn);
            if (outcome.Redirected)
            {
                return true;
            }
            done = outcome.Done;
        }
        return done && Finish(turn);
    }

    private CycleOutcome WorkStep(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        ActionDefinition action = definitions[unit.Action];
        WorkStep step = effects.Apply(unit);
        if (unit.AnimationNumber == DancingAnimation)
        {
            places.Entertain(unit);
        }
        bool done = TrainEveryStep(unit, action);
        CycleOutcome cycle = CompleteCycle(turn, step, done);
        if (cycle.Redirected)
        {
            return cycle;
        }
        done = cycle.Done;
        if (unit.Action == WaitForMaterial && unit.WorkProgress == MaterialReady)
        {
            done = true;
        }
        if (unit.Action is >= FirstEating and <= LastEating)
        {
            int appetite = (unit.MaxHitPoints + AppetiteBase) / AppetiteStep;
            if (unit.Satiety % (Percent / appetite) <= BiteWindow)
            {
                world.Tribes[unit.Tribe][TribeCounter.Meals]--;
            }
        }
        if (step.Reached(action.EffectGoal))
        {
            if ((int)action.EffectProperty > 0 && unit.Action > ActionNumbers.LastBasicAttack)
            {
                EntitySlots.Write(world, action.EffectProperty, unit.EffectRecipient, action.EffectGoal);
            }
            done = true;
        }
        return new CycleOutcome(false, done);
    }

    /// <summary>The training of a work step: a building's (for its operator, or everyone) while the unit works at a position; the action's own otherwise.</summary>
    private bool TrainEveryStep(Unit unit, ActionDefinition action)
    {
        if (unit.BuildingSlot >= 0)
        {
            if (WorkBuilding(unit) is not { } building || kinds[building.Kind].Operation is not { } operation)
            {
                return false;
            }
            bool trains = operation.TrainsOnceAtEnd && building.WorkMode == BuildingWorkMode.Normal
                && (building.OperatorOrBeds == unit.Id || operation.TrainsAllOccupants) && unit.ActionSubState > 0;
            return trains && places.Train(unit, building.Kind, building.Level - 1);
        }
        if (action.TrainsEveryStep)
        {
            Practise(unit, action);
        }
        return false;
    }

    /// <summary>The action is done: a fisher first casts its line; otherwise the completion runs and the next action starts.</summary>
    private bool Finish(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Action == ActionNumbers.Fishing && unit.AnimationNumber < FirstCatchAnimation)
        {
            unit.Animation = CastLine;
            unit.CarriedAmount--;
            unit.WorkProgress = FishingProgress;
            return false;
        }
        if (!completion.Finish(turn))
        {
            completion.TakeUpNext(turn);
        }
        return true;
    }

    /// <summary>Whether <paramref name="unit"/> plays one of its action's own animations.</summary>
    private bool PlaysOwnAnimation(Unit unit)
    {
        IReadOnlyList<AnimationChoice> choices = definitions[unit.Action].AnimationChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i].Animation == unit.AnimationNumber)
            {
                return true;
            }
        }
        return false;
    }

    private void Practise(Unit unit, ActionDefinition action) =>
        lifecycle.Practise(unit, action.TrainedStat, action.TrainingChancePercent, action.TrainingCap,
            world.Content.Rules.Skills.TrainableStats[action.TrainedStat].ActionTrainingStep);

    private Building? WorkBuilding(Unit unit) =>
        unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots ? world.Building(unit.TargetIndex) : null;

    private static bool IsFetch(int action) =>
        (action >= WorkMaterials.FirstFetch && action <= LastFetch) || action == WorkMaterials.FetchUpgradeMaterial;

    /// <summary>The last fetch action of the stock recipes.</summary>
    private const int LastFetch = 49;

    /// <summary>What a work step led to: the unit went for another target, or whether its action is done.</summary>
    private readonly record struct CycleOutcome(bool Redirected, bool Done);
}
