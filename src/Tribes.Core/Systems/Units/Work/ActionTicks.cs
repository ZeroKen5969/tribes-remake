using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// One frame of a unit's action animation: the frame counters advance, and on the animation's
/// special frames a melee blow lands, a projectile is thrown, a delivery is handed over or the unit
/// changes its long-term task.
/// </summary>
internal sealed class ActionTicks
{
    /// <summary>Loop mode of an animation that plays once and counts as one whole action step.</summary>
    private const int PlaysOnce = -2;

    /// <summary>The action tick counter sits in the high four bits of <see cref="Unit.StepAndActionTicks"/>.</summary>
    private const int ActionTickStep = 16;

    /// <summary>Highest of the melee attacks (0..4).</summary>
    private const int LastMeleeAttack = 4;

    /// <summary>The ranged attacks.</summary>
    private const int FirstRangedAttack = 5, LastRangedAttack = 9;

    /// <summary>The throwing animations; the projectile leaves on their last frame.</summary>
    private const int FirstThrow = 35, LastThrow = 36;

    /// <summary>A tower guard's projectile starts this high and drops this much.</summary>
    private const int TowerShotHeight = 24, TowerShotDrop = 37;

    /// <summary>The unloading animation and the frame the load is handed over.</summary>
    private const int UnloadAnimation = 22, UnloadFrame = 10;

    /// <summary>A unit getting up to go to another workplace gives up its task on this frame.</summary>
    private const int TaskDroppedFrame = 6;

    /// <summary>A unit settling at a workplace takes its action's task on this frame.</summary>
    private const int SettlingAnimation = -109, TaskTakenFrame = 1;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly IReadOnlyList<AnimationDefinition> animations;
    private readonly IReadOnlyList<DirectionStep> directions;
    private readonly MeleeBlows melee;
    private readonly Projectiles projectiles;
    private readonly WorkEffects effects;

    /// <summary>Advances action animations in <paramref name="world"/>.</summary>
    public ActionTicks(GameWorld world, MeleeBlows melee, Projectiles projectiles, WorkEffects effects)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.melee = melee ?? throw new ArgumentNullException(nameof(melee));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.effects = effects ?? throw new ArgumentNullException(nameof(effects));
        actions = world.Content.Rules.Actions.Actions;
        animations = world.Content.Rules.Animations.Animations;
        directions = world.Content.Rules.Movement.Directions;
    }

    /// <summary>Plays one frame of <paramref name="unit"/>'s animation.</summary>
    public void Advance(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        AnimationDefinition animation = animations[unit.AnimationNumber];
        unit.AnimationTick++;
        if (animation.LoopMode != PlaysOnce)
        {
            unit.StepAndActionTicks += ActionTickStep;
        }
        ActionDefinition action = actions[unit.Action];
        if (unit.Action <= LastMeleeAttack && EntitySlots.Read(world, action.TargetAliveProperty, unit.TargetIndex) > 0
            && animation.TriggerFrame == unit.AnimationTick)
        {
            DirectionStep ahead = directions[unit.Facing];
            melee.Strike(unit, new GridPoint(unit.X + ahead.Dx, unit.Y + ahead.Dy), animation.TriggerParameter);
        }
        if (unit.AnimationTick == animation.FrameCount - 1 && unit.Animation is >= FirstThrow and <= LastThrow)
        {
            Throw(unit, action);
        }
        if (unit.Animation == UnloadAnimation && unit.AnimationTick == UnloadFrame && unit.Action <= ActionNumbers.LastDelivery)
        {
            Unload(unit);
        }
        if (unit.AnimationNumber == AnimationNumbers.ChangingWorkplace && unit.AnimationTick == TaskDroppedFrame)
        {
            unit.AssignedTask = ActionNumbers.Idle;
        }
        if (unit.Animation == SettlingAnimation && unit.AnimationTick == TaskTakenFrame)
        {
            unit.AssignedTask = action.StandingTask;
        }
    }

    private void Throw(Unit unit, ActionDefinition action)
    {
        var from = new GridPoint(unit.X, unit.Y);
        if (unit.Action is >= FirstRangedAttack and <= LastRangedAttack)
        {
            projectiles.Throw(unit, from, 0, action.ProjectileImpactDrop ?? 0);
        }
        else if (unit.Action is ActionNumbers.TowerShot or ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard)
        {
            projectiles.Throw(unit, from, TowerShotHeight, TowerShotDrop);
        }
    }

    /// <summary>The load is handed over: it counts as gathered (unless it goes to the armoury) and the delivery step is done.</summary>
    private void Unload(Unit unit)
    {
        if (unit.Action < ActionNumbers.LastDelivery)
        {
            world.Tribes[unit.Tribe][TribeCounter.Gathered] += unit.CarriedAmount;
        }
        effects.Apply(unit);
    }
}
