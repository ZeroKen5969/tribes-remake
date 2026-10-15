using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// The end of an action step (its action ticks are used up, or its play-once animation ran out):
/// a unit in reach of its target does the step's work and starts its next animation; one that
/// is not where its work is first walks there.
/// </summary>
internal sealed class ActionSteps
{
    /// <summary>Loop mode of an animation that plays once and counts as one whole action step.</summary>
    private const int PlaysOnce = -2;

    /// <summary>Actions with their own step end: waiting for material, resuming, waking up, the armoury worker and the witch doctor.</summary>
    private const int WaitForMaterial = 84, Resume = 85, WakeUp = 80, ArmouryWorker = 58;

    /// <summary>The fetch actions whose material a waiting unit checks for.</summary>
    private const int LastFetch = 49;

    /// <summary>A waiting unit whose material is in stock has progress 100.</summary>
    private const int MaterialReady = 100;

    private readonly GameWorld world;
    private readonly RouteFinder routes;
    private readonly TargetDistances distances;
    private readonly WorkMaterials materials;
    private readonly WorkSteps work;
    private readonly TargetCycle targets;
    private readonly ActionAnimation animation;
    private readonly IReadOnlyList<ActionDefinition> definitions;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>Ends the action steps of <paramref name="world"/>'s units.</summary>
    public ActionSteps(GameWorld world, RouteFinder routes, TargetDistances distances, WorkMaterials materials, WorkSteps work,
        TargetCycle targets, ActionAnimation animation)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.materials = materials ?? throw new ArgumentNullException(nameof(materials));
        this.work = work ?? throw new ArgumentNullException(nameof(work));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.animation = animation ?? throw new ArgumentNullException(nameof(animation));
        definitions = world.Content.Rules.Actions.Actions;
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>
    /// The turn's unit may have come to the end of an action step (<see cref="UnitTurn.StepEnded"/>
    /// when its play-once animation ran out). A waiting unit checks whether its material is in stock.
    /// In reach of its target (or resuming, or waking), it does the step's work; when that ends its
    /// action, a unit that is not at its destination walks there first. When the step ended, the
    /// unit's next animation starts.
    /// </summary>
    public void End(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        bool playsOnce = animations[unit.AnimationNumber].LoopMode == PlaysOnce;
        if ((!playsOnce && unit.ActionTicks >= definitions[unit.Action].ActionTicks) || (playsOnce && turn.StepEnded))
        {
            unit.StepAndActionTicks &= StepTickMask;
            if (unit.Action == WaitForMaterial && WaitsForFetch(unit.SavedAction)
                && materials.InStock(unit, unit.SavedAction - WorkMaterials.FirstFetch))
            {
                unit.WorkProgress = MaterialReady;
            }
            bool inReach = unit.TargetIndex >= 0 && turn.GoalCells <= routes.ActionRange(unit);
            if ((inReach || unit.Action is Resume or WakeUp) && work.Step(turn)
                && (unit.Action != ActionNumbers.Idle || unit.OrderMode < OrderModes.Walk))
            {
                if (unit.DestinationX == unit.X && unit.DestinationY == unit.Y)
                {
                    if (!CoupleLinks.IsOnItsCell(world, unit))
                    {
                        return;
                    }
                }
                else if (WalksThereFirst(turn))
                {
                    return;
                }
                turn.StepEnded = true;
            }
        }
        if (turn.StepEnded)
        {
            animation.Start(unit, turn.GoalDistance);
        }
    }

    /// <summary>
    /// After finishing, a unit not at its destination plans the way there (the armoury worker and the
    /// witch doctor first let the workplaces hand out their orders); returns true when it has a route.
    /// </summary>
    private bool WalksThereFirst(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Action is ArmouryWorker or ActionNumbers.WitchDoctor)
        {
            targets.HandOutOrders(unit.Tribe);
        }
        turn.GoalDistance = distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured;
        targets.Approach(turn);
        return unit.PathLength > 0;
    }

    private static bool WaitsForFetch(int saved) =>
        (saved >= WorkMaterials.FirstFetch && saved <= LastFetch) || saved == WorkMaterials.FetchUpgradeMaterial;

    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickMask = 0x0F;
}
