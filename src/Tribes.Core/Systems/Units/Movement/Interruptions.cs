using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>
/// Whether a unit is too busy to be disturbed (by an order, or by a tribe mate who wants its
/// cell). A unit playing its way out of a work position is; a resting, waiting or sleeping unit is
/// woken (it resumes), and a unit working in another workplace group gets up first. Anything else
/// may be disturbed.
/// </summary>
internal sealed class Interruptions
{
    /// <summary>Sleeping outdoors, and waking up from it.</summary>
    private const int SleepOutdoors = 79, WakeUp = 80;

    /// <summary>Resting, waiting and resuming.</summary>
    private const int RestAction = 83, WaitAction = 84, ResumeAction = 85;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly ActionAnimation animation;
    private readonly WorkPositions positions;
    private readonly IReadOnlyList<ActionCategory> categories;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public Interruptions(GameWorld world, UnitActions actions, ActionAnimation animation, WorkPositions positions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.animation = animation ?? throw new ArgumentNullException(nameof(animation));
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        categories = world.Content.Rules.Actions.Categories;
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> holds on to what it does. A disturbed rester resumes (its new
    /// animation starts at frame <paramref name="frame"/>); with <paramref name="byOrder"/> a unit
    /// whose task belongs to a workplace group gets up from its work first.
    /// </summary>
    public bool HoldsOn(Unit unit, int frame, bool byOrder)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Building? building = unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots ? world.Building(unit.TargetIndex) : null;
        if (unit.BuildingSlot >= 0 && building is not null && IsLeavingPosition(unit, building))
        {
            return true;
        }
        int group = categories[actions.Definition(unit.AssignedTask).Category].ServiceGroup;
        bool changingPlace = unit.AnimationNumber == UnitAnimations.ChangingWorkplace;
        bool disturbable = unit.Action is SleepOutdoors or WakeUp or >= RestAction and <= ResumeAction
            || (byOrder && group != 0)
            || (changingPlace && unit.AnimationTick != animations[UnitAnimations.ChangingWorkplace].FrameCount - 1);
        if (!disturbable)
        {
            return false;
        }
        if (unit.Action is RestAction or WaitAction or SleepOutdoors)
        {
            actions.SetAction(unit, unit.Action == SleepOutdoors ? WakeUp : ResumeAction);
            animation.Start(unit, 0);
            unit.AnimationTick = frame;
            return true;
        }
        if (byOrder && group > 0 && !changingPlace)
        {
            GetUp(unit, building, frame);
        }
        return true;
    }

    private bool IsLeavingPosition(Unit unit, Building building)
    {
        int animationNumber = unit.AnimationNumber;
        return animationNumber == positions.Normal(building.Kind, unit.BuildingSlot)?.ExitAnimation
            || animationNumber == positions.Busy(building.Kind, unit.BuildingSlot)?.ExitAnimation;
    }

    /// <summary>The unit gets up from its work position to leave.</summary>
    private void GetUp(Unit unit, Building? building, int frame)
    {
        unit.PathLength = 0;
        unit.Animation = UnitAnimations.ChangingWorkplace;
        unit.AnimationTick = frame;
        if (!CoupleLinks.IsOnItsCell(world, unit) && building is not null)
        {
            unit.ActionSubState = building.WorkMode != BuildingWorkMode.Normal ? SubStates.InBusyBuilding
                : building.Kind == BuildingKindNumbers.Disco ? SubStates.InDisco : SubStates.FirstPose;
        }
    }
}
