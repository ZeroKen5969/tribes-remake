using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units;

/// <summary>
/// What happens to a unit at the start of its turn before it decides anything: a dying unit plays
/// its death to the end (its last projectile still flies) and frees its slot; a unit whose death
/// was put off dies once it is back on its cell; the unit ages, and at their own intervals its
/// body, mood, home, disease and idle counter are seen to.
/// </summary>
internal sealed class UnitHousekeeping
{
    /// <summary>While the objective stage is this one, the player's skilled fighters are counted in the objective timer.</summary>
    private const int SkilledFightersObjective = 2;

    /// <summary>A fighter is skilled with more than this melee or ranged skill.</summary>
    private const int SkilledFighter = 4;

    /// <summary>Age ticks count every second frame for children, every fourth for the player's adults, every sixteenth for the computer's.</summary>
    private const int ChildAgeMask = 1, PlayerAgeMask = 3, ComputerAgeMask = 15;

    /// <summary>The body is seen to when frame and slot agree on these bits.</summary>
    private const int BodyMask = 3;

    /// <summary>The mood every 32 frames (frame bits against the slot's low four bits).</summary>
    private const int MoodMask = 31, MoodSlotMask = 15;

    /// <summary>Homeless adults look for a home every 32 frames.</summary>
    private const int HomeMask = 31;

    /// <summary>A sick unit infects its neighbours every eight frames.</summary>
    private const int ContagionMask = 7;

    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickBits = 0x0F;

    private readonly GameWorld world;
    private readonly UnitLifecycle lifecycle;
    private readonly Occupancy occupancy;
    private readonly Deaths deaths;
    private readonly Projectiles projectiles;
    private readonly UnitAgeing ageing;
    private readonly UnitMood mood;
    private readonly UnitDisease disease;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitHousekeeping(GameWorld world, UnitLifecycle lifecycle, Occupancy occupancy, Deaths deaths,
        Projectiles projectiles, UnitAgeing ageing, UnitMood mood, UnitDisease disease)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.ageing = ageing ?? throw new ArgumentNullException(nameof(ageing));
        this.mood = mood ?? throw new ArgumentNullException(nameof(mood));
        this.disease = disease ?? throw new ArgumentNullException(nameof(disease));
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>
    /// The start of <paramref name="unit"/>'s turn (its slot is in use); returns true when the turn
    /// ends here (the unit is dying or died).
    /// </summary>
    public bool Update(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.HitPoints < 0)
        {
            PlayDeath(unit);
            return true;
        }
        if (world.Script.ObjectiveStage == SkilledFightersObjective && TribeRoles.IsPlayer(world, unit.Tribe)
            && (unit.MeleeSkill > SkilledFighter || unit.RangedSkill > SkilledFighter))
        {
            world.Script.ObjectiveTimer++;
        }
        bool onCell = CoupleLinks.IsOnItsCell(world, unit);
        if ((unit.PairLink & unit.DelayedDeathBit) != 0 && onCell)
        {
            deaths.Die(EntityProperty.UnitHitPoints, new GridPoint(unit.X, unit.Y), unit.Id, updatedLaterThisFrame: false);
            return true;
        }
        if ((world.Frame & AgeMask(unit)) == 0)
        {
            unit.AgeTicks++;
        }
        if ((world.Frame & BodyMask) == (unit.Id & BodyMask) && !ageing.Update(unit))
        {
            return true;
        }
        SeeToWellbeing(unit);
        if (unit.Action <= ActionNumbers.LastBasicAttack && unit.Action != ActionNumbers.HuntMelee
            && unit.Action != ActionNumbers.HuntRanged)
        {
            MarkFighting(unit);
        }
        unit.IdleTicks++;
        if (unit.IsCentred)
        {
            unit.StepAndActionTicks &= ~StepTickBits;
        }
        if (unit.Thought >= Thoughts.FirstSpell && !UnitThoughts.ShowsSpellIcon(unit.AnimationNumber))
        {
            unit.Thought = Thoughts.None;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="attacker"/> attacks: the game is not quiet (the campaign's one world-wide
    /// count); in a match the attacker's tribe and the tribe of the unit or building it attacks are
    /// not quiet either, and other tribes' quiet goes on.
    /// </summary>
    private void MarkFighting(Unit attacker)
    {
        world.FramesSinceCombat = 0;
        if (world.Mode != WorldMode.Match)
        {
            return;
        }
        world.Tribes[attacker.Tribe].FramesSinceCombat = 0;
        int target = attacker.TargetIndex;
        int? category = world.Content.Rules.Actions.Actions[attacker.Action].AttackTargetCategory;
        int victim = category == Map.CellCategories.Unit && target >= 1 && target <= world.UnitSlots ? world.TribeOfUnit(target)
            : category == Map.CellCategories.Building && target >= 1 && target <= world.BuildingSlots ? world.TribeOfBuilding(target)
            : -1;
        if (victim >= 0)
        {
            world.Tribes[victim].FramesSinceCombat = 0;
        }
    }

    private void PlayDeath(Unit unit)
    {
        unit.AnimationTick++;
        if (unit.AnimationTick >= animations[unit.AnimationNumber].FrameCount)
        {
            lifecycle.FreeSlot(unit);
        }
        if (unit.ProjectileFlightSteps > 0)
        {
            projectiles.Fly(unit);
        }
    }

    private int AgeMask(Unit unit) => unit.Age < UnitAges.Adult ? ChildAgeMask
        : TribeRoles.IsPlayer(world, unit.Tribe) ? PlayerAgeMask : ComputerAgeMask;

    /// <summary>Mood, home and disease, each at its own interval.</summary>
    private void SeeToWellbeing(Unit unit)
    {
        if ((world.Frame & MoodMask) == (unit.Id & MoodSlotMask))
        {
            mood.Update(unit);
        }
        if (unit.Home < 1 && unit.Age >= UnitAges.Adult && (world.Frame & HomeMask) == 0)
        {
            occupancy.AssignHome(unit);
        }
        bool sick = (unit.Status & UnitStatus.Diseased) != 0;
        if (sick && (world.Frame & ContagionMask) == (unit.Id & ContagionMask) && CoupleLinks.IsOnItsCell(world, unit))
        {
            disease.SpreadFrom(unit);
        }
        if ((world.Frame & 1) == 1 && (unit.Status & UnitStatus.Diseased) == 0)
        {
            disease.UpdateExposure(unit);
        }
    }
}
