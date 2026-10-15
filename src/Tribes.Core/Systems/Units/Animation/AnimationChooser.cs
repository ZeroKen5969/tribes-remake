using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Animation;

/// <summary>The animation picked for a unit's action step.</summary>
/// <param name="TurnEnds">Whether picking it took the unit's turn (it started fishing or chatting).</param>
/// <param name="Action">The action whose animations were drawn from: the unit's own, or idling when it has nothing to do.</param>
internal readonly record struct AnimationPick(bool TurnEnds, int Action);

/// <summary>
/// Picks the animation of a unit's next action step from its action's animation choices with a
/// roll of 0..99: a unit without anything to do in reach plays idling animations (adults may chat
/// with a tribe mate), a fisher turns to the water, loaded or hidden units and units at a pause
/// start further down the list (a waiting unit without a pause of its own, as inside a building, starts at
/// the first), and a long roll may switch to a looping variant. Inside a building
/// the pick also sets the unit's pose there.
/// </summary>
internal sealed class AnimationChooser
{
    /// <summary>A roll above this lets a chatting unit's partner chat back.</summary>
    private const int AnswerRoll = 20;

    /// <summary>A roll above this lets an idle adult start chatting.</summary>
    private const int ChatRoll = 98;

    /// <summary>A roll of at least this switches an animation to its looping variant, if it has one.</summary>
    private const int LoopRoll = 85;

    /// <summary>Loop modes whose bits under this mask are all set (-1 and -2) have no looping variant.</summary>
    private const int NoVariantMask = 254;

    /// <summary>Choice offsets: a hidden idler, a loaded hunter, a loaded visitor or one leaving a building, other loaded idlers.</summary>
    private const int HiddenOffset = 6, LoadedHuntOffset = 1, LoadedVisitOffset = 2, LoadedOffset = 4;

    /// <summary>The weapon kind that is a pile of stones (carried like a load).</summary>
    private const int StoneWeapon = 1;

    /// <summary>The actions of a story scene's actors and of a unit resuming its work; they keep their action without a target.</summary>
    private const int StagedAction = 105, ResumeAction = 85;

    /// <summary>The actions whose choices start at the unit's sub-state.</summary>
    private const int RestAction = 83, WaitAction = 84;

    /// <summary>Range training (its choices start by the weapon's projectile), the couple's walk, a visit and crafting a spear.</summary>
    private const int RangeTraining = 72, CourtshipWalk = 109, VisitAction = 51, SpearCrafting = 53, CoupleVisit = 111;

    private readonly GameWorld world;
    private readonly RouteFinder routes;
    private readonly Chatting chatting;
    private readonly WorkPositions positions;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly IReadOnlyList<AnimationDefinition> animations;
    private readonly List<AnimationChoice> choices = new();
    private readonly int[] starts;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public AnimationChooser(GameWorld world, RouteFinder routes, Chatting chatting, WorkPositions positions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.chatting = chatting ?? throw new ArgumentNullException(nameof(chatting));
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        actions = world.Content.Rules.Actions.Actions;
        animations = world.Content.Rules.Animations.Animations;
        starts = new int[actions.Count + 1];
        for (int a = 0; a < actions.Count; a++)
        {
            starts[a] = choices.Count;
            choices.AddRange(actions[a].AnimationChoices);
        }
        starts[actions.Count] = choices.Count;
    }

    /// <summary>
    /// Picks <paramref name="unit"/>'s animation with <paramref name="roll"/> (0..99);
    /// <paramref name="goalDistance"/> is how far its goal is (10 * cells + facing).
    /// </summary>
    public AnimationPick Choose(Unit unit, int goalDistance, int roll)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int action = AnimatedAction(unit, goalDistance);
        if (action == ActionNumbers.Fishing && Fish(unit))
        {
            return new AnimationPick(true, action);
        }
        int animation = unit.AnimationNumber;
        bool hidden = (unit.Status & UnitStatus.Hidden) != 0;
        if ((animation is UnitAnimations.Chatting or UnitAnimations.ChattingHolding) && roll > AnswerRoll)
        {
            chatting.Answer(unit);
        }
        else if (action == ActionNumbers.Idle && !hidden && roll > ChatRoll && chatting.StartChat(unit))
        {
            return new AnimationPick(true, action);
        }
        Draw(unit, action, FirstChoice(unit, action, hidden), roll);
        return new AnimationPick(false, action);
    }

    /// <summary>The action animated: the unit's own, or idling when it has no target in reach or no work position.</summary>
    private int AnimatedAction(Unit unit, int goalDistance)
    {
        bool idle = unit.TargetIndex < 0 || GridDistances.Cells(goalDistance) > routes.ActionRange(unit)
            || (actions[unit.Action].BuildingInteraction == BuildingInteraction.WorkInside && unit.BuildingSlot < 0);
        if (idle && unit.Action != StagedAction && unit.Action != ResumeAction)
        {
            return unit.Age >= UnitAges.Adult ? ActionNumbers.Idle : ActionNumbers.ChildIdle;
        }
        unit.PathBlock = 0;
        return unit.Action;
    }

    /// <summary>A fisher starts fishing facing the water, or goes on fishing; returns whether that took its turn.</summary>
    private bool Fish(Unit unit)
    {
        if (unit.AnimationNumber < UnitAnimations.FirstFishingAnimation)
        {
            unit.Animation = UnitAnimations.Fishing;
            var east = new GridPoint(unit.X + 1, unit.Y);
            bool water = world.Map.Contains(east) && world.Map.CellAt(east).Content == CellContent.ResourceNode;
            unit.Facing = water ? FacingNumbers.East : FacingNumbers.West;
            return true;
        }
        return unit.AnimationNumber == UnitAnimations.CastingLine;
    }

    /// <summary>Where in <paramref name="action"/>'s choices the draw starts, relative to its first choice.</summary>
    private int FirstChoice(Unit unit, int action, bool hidden)
    {
        bool loaded = unit.CarriedItem != 0 || unit.Weapon == StoneWeapon;
        switch (action)
        {
            case RangeTraining:
                int kind = world.Content.Rules.Weapons.Weapons[unit.Weapon].ProjectileKind;
                return kind >= 0 ? world.Content.Rules.Weapons.Projectiles[kind].RangeTrainingAnimationOffset : choices.Count;
            case ActionNumbers.Idle when hidden:
                return HiddenOffset;
            case ActionNumbers.Idle or CourtshipWalk or ActionNumbers.HuntingTask or VisitAction or ActionNumbers.LeaveBuilding when loaded:
                return action == ActionNumbers.HuntingTask ? LoadedHuntOffset
                    : action is VisitAction or ActionNumbers.LeaveBuilding ? LoadedVisitOffset : LoadedOffset;
            case RestAction or ResumeAction or StagedAction:
                return unit.ActionSubState;
            case WaitAction:
                IReadOnlyList<int> pauses = world.Content.Rules.Actions.PauseAnimationOffsetBySubState;
                return unit.ActionSubState >= 0 && unit.ActionSubState < pauses.Count ? pauses[unit.ActionSubState] : 0;
            default:
                return 0;
        }
    }

    /// <summary>Draws the first choice from <paramref name="first"/> on whose cumulative percent beats <paramref name="roll"/>.</summary>
    private void Draw(Unit unit, int action, int first, int roll)
    {
        for (int i = starts[action] + first; i < starts[action + 1]; i++)
        {
            if (i < 0 || choices[i].CumulativePercent <= roll)
            {
                continue;
            }
            unit.Animation = choices[i].Animation;
            Building? building = unit.BuildingSlot >= 0 && unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots
                ? world.Building(unit.TargetIndex)
                : null;
            if (building is null || building.Kind != BuildingKindNumbers.Hut || unit.ActionSubState <= 0)
            {
                int loop = animations[unit.AnimationNumber].LoopMode;
                if ((loop & NoVariantMask) != NoVariantMask && roll >= LoopRoll)
                {
                    unit.Animation = loop;
                }
                if (building is not null)
                {
                    unit.ActionSubState = KeepsPose(unit, building) ? i - starts[action] : SubStates.InBusyBuilding;
                }
            }
            return;
        }
    }

    /// <summary>Whether the pose drawn becomes the unit's sub-state inside the building (else it shows as busy).</summary>
    private bool KeepsPose(Unit unit, Building building)
    {
        int? work = positions.Normal(building.Kind, unit.BuildingSlot)?.WorkAction;
        return (unit.Action == work || unit.Action == SpearCrafting || unit.Action == CoupleVisit
                || (unit.BuildingSlot > 0 && building.Kind < BuildingKindNumbers.FirstSite))
            && building.WorkMode == BuildingWorkMode.Normal;
    }
}
