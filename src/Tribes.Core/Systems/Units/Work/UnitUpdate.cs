using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Senses;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The systems of the units' part of a frame.</summary>
/// <param name="FrameStart">The start of the frame (messages, timed supplies).</param>
/// <param name="ComputerPlayer">The computer player's turn, before its units.</param>
/// <param name="HutBirths">Children born in huts, after each tribe's units.</param>
/// <param name="Housekeeping">The start of a unit's turn (dying, ageing, body, mood).</param>
/// <param name="Stealth">Scouts hiding.</param>
/// <param name="Thoughts">The player's adults' thoughts.</param>
/// <param name="AnimationDriver">The animation part of a unit's turn.</param>
/// <param name="MovementDriver">The walking part of a unit's turn.</param>
internal sealed record UnitFrameParts(FrameStart FrameStart, ComputerPlayer ComputerPlayer, HutBirths HutBirths,
    UnitHousekeeping Housekeeping, UnitStealth Stealth, UnitThoughts Thoughts, AnimationDriver AnimationDriver,
    MovementDriver MovementDriver);

/// <summary>
/// The units' part of a frame: the frame's start, then tribe by tribe (the computer player first
/// for its tribe) the workplaces hand out orders, the birth cool-down runs and every unit takes its
/// turn — housekeeping, orders, hiding, thoughts, decisions, its animation, the free choice of a
/// threat or work, walking and its projectile — and the tribe's huts may have children. Then the
/// quiet time grows and the player's raw goods are capped.
/// </summary>
internal sealed class UnitUpdate
{
    /// <summary>The player's birth cool-down in frames; the computer's is its difficulty's interval times this factor.</summary>
    private const int PlayerBirthCooldown = 750, AiBirthIntervalFrames = 50;

    /// <summary>The player's adults think every 15 frames, staggered by slot (eight groups).</summary>
    private const int ThinkPeriod = 15, ThinkGroups = 7;

    /// <summary>Scouts better than this hide.</summary>
    private const int HidingSkill = 3;

    /// <summary>Units doing these actions make no free choice (arming at the store, a story pose).</summary>
    private const int ArmUp = 50, StoryPose = 105;

    /// <summary>The tower guard category; a guard with a thrown weapon may act from inside its tower.</summary>
    private const int TowerGuardCategory = 16;

    /// <summary>Frames of quiet counted at most.</summary>
    private const int QuietFrames = 50;

    /// <summary>The player's raw goods (food .. bones) are capped at this.</summary>
    private const int GoodsCap = 999;

    private readonly GameWorld world;
    private readonly UnitGoals goals;
    private readonly OrderFollowing orders;
    private readonly UnitDecisions decisions;
    private readonly ThreatWatch threats;
    private readonly TargetCycle targets;
    private readonly Projectiles projectiles;
    private readonly UnitFrameParts parts;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>The turn started again for each unit in turn (only the frame's own loop uses it).</summary>
    private UnitTurn? sharedTurn;

    /// <summary>Runs the units' part of <paramref name="world"/>'s frames.</summary>
    public UnitUpdate(GameWorld world, UnitGoals goals, OrderFollowing orders, UnitDecisions decisions, ThreatWatch threats,
        TargetCycle targets, Projectiles projectiles, UnitFrameParts parts)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.goals = goals ?? throw new ArgumentNullException(nameof(goals));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        this.threats = threats ?? throw new ArgumentNullException(nameof(threats));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.parts = parts ?? throw new ArgumentNullException(nameof(parts));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>Runs the units' part of one frame.</summary>
    public void RunFrame()
    {
        parts.FrameStart.Run();
        foreach (TribeState tribe in world.Tribes)
        {
            if (TribeRoles.IsComputer(world, tribe.Index))
            {
                parts.ComputerPlayer.Step(tribe.Index);
            }
            targets.HandOutOrders(tribe.Index);
            CountBirthCooldown(tribe);
            ListView<Unit> units = world.UnitsOf(tribe.Index);
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit.HitPoints != 0 && !parts.Housekeeping.Update(unit))
                {
                    TakeTurn(goals.StartTurn(sharedTurn ??= new UnitTurn(unit, 0), unit));
                }
            }
            parts.HutBirths.Update(tribe.Index);
        }
        if (world.FramesSinceCombat < QuietFrames)
        {
            world.FramesSinceCombat++;
        }
        if (world.Mode == WorldMode.Match)
        {
            foreach (TribeState tribe in world.Tribes)
            {
                if (tribe.FramesSinceCombat < TribeState.QuietAfterFrames)
                {
                    tribe.FramesSinceCombat++;
                }
            }
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled)
            {
                CapGoods(tribe);
            }
        }
    }

    /// <summary>The turn of one unit after its housekeeping.</summary>
    public void TakeTurn(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (!StepMotion.IsCaught(unit) && Decides(turn))
        {
            return;
        }
        parts.AnimationDriver.Update(turn);
        turn.RegularDecision = false;
        if (unit.IdleTicks == 0 && unit.Age >= UnitAges.Adult && unit.Action is not (ArmUp or StoryPose))
        {
            turn.MayActFreely = MayActFreely(turn);
            turn.RegularDecision = true;
            threats.Look(turn);
        }
        parts.MovementDriver.Update(turn);
        if (unit.ProjectileFlightSteps > 0)
        {
            projectiles.Fly(unit);
        }
    }

    /// <summary>Orders, hiding, thoughts and decisions; returns true when the unit's turn ends there.</summary>
    private bool Decides(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        orders.Follow(unit);
        if (unit.ScoutingSkill > HidingSkill && parts.Stealth.Update(turn))
        {
            return true;
        }
        if (world.Frame % ThinkPeriod == (unit.Id & ThinkGroups) && TribeRoles.IsPlayer(world, unit.Tribe) && unit.Age >= UnitAges.Adult)
        {
            parts.Thoughts.Think(unit);
        }
        return decisions.Decide(turn);
    }

    /// <summary>
    /// Whether the unit may take up work or a threat by itself this frame: free of orders (or its
    /// tribe acts by itself, or a kept order got it nowhere), on its cell (or guarding a tower with
    /// a thrown weapon), not hidden, and not an attacker that keeps its target.
    /// </summary>
    private bool MayActFreely(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        ActionDefinition action = definitions[unit.Action];
        bool stuckOrder = unit.OrderMode == OrderModes.Keep && unit.PathLength <= unit.PathIndex
            && turn.GoalCells > RangeOf(unit) && unit.AnimationNumber > AnimationNumbers.LastWalking;
        bool free = unit.OrderMode == OrderModes.Free || world.Script.ActsByItself(unit.Tribe) || stuckOrder;
        bool placed = CoupleLinks.IsOnItsCell(world, unit)
            || (action.Category == TowerGuardCategory && world.Content.Rules.Weapons.Weapons[unit.Weapon].RangedActionOffset > 0);
        return free && placed && (unit.Status & UnitStatus.Hidden) == 0
            && (unit.Action > ActionNumbers.LastBasicAttack || (action.Traits & ActionTraits.KeepsTarget) == 0);
    }

    private void CountBirthCooldown(TribeState tribe)
    {
        if (tribe[TribeCounter.BirthCooldown] >= 0)
        {
            tribe[TribeCounter.BirthCooldown]++;
        }
        int limit = TribeRoles.IsPlayer(world, tribe.Index)
            ? PlayerBirthCooldown
            : world.Content.Rules.Missions.Missions[world.Mission].Difficulties[TribeRoles.Difficulty(world, tribe.Index)].AiBirthInterval
                * AiBirthIntervalFrames;
        if (tribe[TribeCounter.BirthCooldown] >= limit)
        {
            tribe[TribeCounter.BirthCooldown] = -1;
        }
    }

    private int RangeOf(Unit unit)
    {
        ValueSource range = definitions[unit.Action].Range;
        return range.Constant ?? EntityProperties.Get(world, range.Property!.Value, unit.Id);
    }

    private static void CapGoods(TribeState player)
    {
        for (TribeCounter goods = TribeCounter.Food; goods <= TribeCounter.Bones; goods++)
        {
            if (player[goods] > GoodsCap)
            {
                player[goods] = GoodsCap;
            }
        }
    }
}
