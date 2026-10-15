using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The end of a work cycle.</summary>
internal sealed partial class WorkSteps
{
    /// <summary>The animation of a spell being cast (as unsigned).</summary>
    private const int CastingAnimation = 140;

    /// <summary>The witch doctor treating the sanctuary's occupants.</summary>
    private const int Treating = 55;

    /// <summary>The construction site kind whose trainings builders of upgrades and repairs get.</summary>
    private const int BuilderTrainingKind = 16;

    /// <summary>Actions whose cycle end has its own meaning.</summary>
    private const int FirstSpell = 94, LastSpell = 97, IntoTheHut = 111, Cooking = 54;
    private const int GatherWood = 11, GatherStones = 14;

    /// <summary>A captor this near its captive (measured: 10 × cells) has it.</summary>
    private const int CaptureDistance = 20;

    /// <summary>A gatherer's effect goal meaning "until the load is full".</summary>
    private const int FullLoadGoal = 99;

    /// <summary>
    /// A work cycle is complete when the unit's work progress reaches the action's cycle points: a
    /// spell is cast, the witch doctor treats its patients, or the cycle's product comes: training,
    /// one more of the effect (one item gathered or made), a node used up a little. Returns whether
    /// the unit went for another target, or whether its action is done now (starting from
    /// <paramref name="done"/>).
    /// </summary>
    private CycleOutcome CompleteCycle(UnitTurn turn, WorkStep step, bool done)
    {
        Unit unit = turn.Unit;
        ActionDefinition action = definitions[unit.Action];
        if (unit.WorkProgress < action.WorkCyclePoints)
        {
            return new CycleOutcome(false, done);
        }
        if (unit.AnimationNumber == CastingAnimation)
        {
            if (unit.Action is >= FirstSpell and <= LastSpell)
            {
                done = spells.Cast(unit);
            }
            else if (unit.Action == Treating)
            {
                places.Heal(unit);
                unit.WorkProgress = 0;
            }
            return new CycleOutcome(false, done);
        }
        if (action.RateTarget.Property != EntityProperty.UnitWorkProgress)
        {
            return new CycleOutcome(false, done);
        }
        done = TrainAtCycleEnd(unit, action, done);
        if (action.EffectProperty != EntityProperty.UnitActionSubState)
        {
            EntitySlots.Add(world, action.EffectProperty, unit.EffectRecipient, step.Direction);
        }
        unit.WorkProgress = 0;
        done = CycleProduct(turn, action, done);
        if (unit.Action is GatherWood or GatherStones && unit.CarriedAmount < unit.CarryCapacity)
        {
            targets.NextTarget(turn);
            return new CycleOutcome(true, done);
        }
        UseUpNode(unit);
        return new CycleOutcome(false, done);
    }

    /// <summary>
    /// Training at the end of a cycle: outside a building the action's own (unless it trains every
    /// step); inside, the building's (for its operator or everyone, unless it trains every step), and
    /// builders of an upgrade or repair train as on a site and count one more piece of work done.
    /// </summary>
    private bool TrainAtCycleEnd(Unit unit, ActionDefinition action, bool done)
    {
        if (unit.BuildingSlot < 0)
        {
            if (!action.TrainsEveryStep)
            {
                Practise(unit, action);
            }
            return done;
        }
        if (WorkBuilding(unit) is not { } building || kinds[building.Kind].Operation is not { } operation)
        {
            return done;
        }
        bool busy = building.WorkMode != BuildingWorkMode.Normal;
        if ((!operation.TrainsOnceAtEnd && (building.OperatorOrBeds == unit.Id || operation.TrainsAllOccupants)) || busy)
        {
            done = places.Train(unit, busy ? BuilderTrainingKind : building.Kind, building.Level - 1);
        }
        if (busy)
        {
            foreach (UpgradeJob job in world.Upgrades)
            {
                if (job.Building == building.Id)
                {
                    job.Done++;
                    break;
                }
            }
        }
        return done;
    }

    /// <summary>What a cycle's product means for the action: a couple's last step, a captive taken, a full load.</summary>
    private bool CycleProduct(UnitTurn turn, ActionDefinition action, bool done)
    {
        Unit unit = turn.Unit;
        if (unit.Action == IntoTheHut)
        {
            done = true;
        }
        if (unit.Action == ActionNumbers.CaptureChild)
        {
            if (turn.GoalDistance < CaptureDistance)
            {
                done = true;
            }
            else if (unit.TargetIndex >= 1 && unit.TargetIndex <= world.UnitSlots)
            {
                world.Unit(unit.TargetIndex).TargetIndex = unit.Id;
            }
        }
        if (action.EffectProperty == EntityProperty.UnitCarriedAmount && action.EffectGoal == FullLoadGoal
            && unit.CarriedAmount >= unit.CarryCapacity)
        {
            done = true;
        }
        return done;
    }

    /// <summary>Gathering from a node uses a little of it up; cooking adds a meal.</summary>
    private void UseUpNode(Unit unit)
    {
        if (unit.Action is ActionNumbers.FirstGathering or ActionNumbers.GatherCarcass or ActionNumbers.Fishing
            or SecondPlantFood or ActionNumbers.GatherBones)
        {
            if (unit.TargetIndex >= 0 && unit.TargetIndex < world.Map.ResourceNodes.Count)
            {
                Maps.ResourceNode node = world.Map.ResourceNodes[unit.TargetIndex];
                node.Amount--;
                if (node.Amount < 0)
                {
                    node.Amount = 0;
                }
            }
        }
        else if (unit.Action == Cooking)
        {
            world.Tribes[unit.Tribe][TribeCounter.Meals]++;
        }
    }

    /// <summary>Gathering the second kind of plant food.</summary>
    private const int SecondPlantFood = 15;
}
