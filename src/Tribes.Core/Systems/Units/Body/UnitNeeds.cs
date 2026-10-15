using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>
/// What a unit's body needs and how it heals: adults get hungry and tired at a pace set by what
/// they do (and slower in the computer's tribe), starving or exhausted units lose hit points, a
/// kitchen worker eats as it cooks, a witch doctor heals itself, others heal slowly unless sick,
/// and the sick waste away. A unit whose hit points run out dies on its cell, or comes out of the
/// building it is in first.
/// </summary>
internal sealed class UnitNeeds
{
    /// <summary>A decay period meaning "never".</summary>
    public const int NeverDecays = 127;

    /// <summary>Highest satiety.</summary>
    private const int FullSatiety = 100;

    /// <summary>A need is lost when frame % period is at most this (the needs are checked every fourth frame).</summary>
    private const int DecayWindow = 3;

    /// <summary>The computer's units get hungry and tired this many times more slowly.</summary>
    private const int ComputerSlowdown = 5;

    /// <summary>Kitchen workers eat one point every this many frames (frame and slot compared on these bits).</summary>
    private const int KitchenMealMask = 63;

    /// <summary>Soldiers called to the rally point with this little food or rest left make the player hear about it.</summary>
    private const int NeedWarningLevel = 5;

    /// <summary>Hints about a hungry and about a tired soldier.</summary>
    private const int HungrySoldierHint = 14, TiredSoldierHint = 15;

    /// <summary>A witch doctor of more than this witchcraft heals itself.</summary>
    private const int SelfHealingWitchcraft = 1;

    /// <summary>A witch doctor heals every (base - step * witchcraft) frames, by this much.</summary>
    private const int SelfHealingPeriodBase = 46, SelfHealingPeriodStep = 3, SelfHealing = 2;

    /// <summary>Others heal one point every 128 frames (frame bits compared with the slot's low bits).</summary>
    private const int HealingMask = 127, HealingSlotMask = 15;

    /// <summary>The sick lose a point every 32 frames.</summary>
    private const int WastingMask = 31;

    /// <summary>The last walking or standing animation; these set their own energy decay.</summary>
    private const int LastOwnEnergyAnimation = 14;

    /// <summary>Hit points and maximum hit points of a unit whose death waits until it is back on its cell.</summary>
    private const int DelayedDeathHitPoints = 1;

    /// <summary>The bit of <see cref="Unit.PairLink"/> marking a delayed death.</summary>
    public const int DelayedDeathBit = 0x80;

    private readonly GameWorld world;
    private readonly UnitLifecycle lifecycle;
    private readonly HintSystem hints;
    private readonly Deaths deaths;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly IReadOnlyList<ActionCategory> categories;
    private readonly IReadOnlyList<AnimationDefinition> animations;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitNeeds(GameWorld world, UnitLifecycle lifecycle, HintSystem hints, Deaths deaths)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
        actions = world.Content.Rules.Actions.Actions;
        categories = world.Content.Rules.Actions.Categories;
        animations = world.Content.Rules.Animations.Animations;
    }

    /// <summary>
    /// One check of <paramref name="unit"/>'s needs (made every fourth frame): an adult loses food
    /// and rest once the mission's needs have started, then the unit heals or wastes away. Returns
    /// false when the unit died.
    /// </summary>
    public bool Tend(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Age >= UnitAges.Adult && world.Frame >= world.Content.Rules.Missions.Missions[world.Mission].NeedsStartFrame)
        {
            Eat(unit);
            Tire(unit);
            if (!SurvivesWounds(unit))
            {
                return false;
            }
        }
        return Heal(unit);
    }

    /// <summary>
    /// Whether <paramref name="unit"/> lives on: a unit without hit points dies when it stands on
    /// its cell; inside a building it gets one hit point and is sent out, and dies once it is back
    /// on its cell.
    /// </summary>
    public bool SurvivesWounds(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.HitPoints > 0)
        {
            return true;
        }
        if (CoupleLinks.IsOnItsCell(world, unit))
        {
            deaths.Die(EntityProperty.UnitHitPoints, new GridPoint(unit.X, unit.Y), unit.Id, updatedLaterThisFrame: false);
            return false;
        }
        unit.MaxHitPoints = DelayedDeathHitPoints;
        unit.HitPoints = DelayedDeathHitPoints;
        unit.OrderMode = OrderModes.Walk;
        unit.PairLink |= unit.DelayedDeathBit;
        return true;
    }

    private void Eat(Unit unit)
    {
        if (TaskCategory(unit) == WorkCategories.Kitchen && (world.Frame & KitchenMealMask) == (unit.Id & KitchenMealMask))
        {
            if (unit.Satiety < FullSatiety)
            {
                unit.Satiety++;
            }
            return;
        }
        if (!Decays(unit, actions[unit.Action].SatietyDecayPeriod))
        {
            return;
        }
        if (unit.Satiety > 0)
        {
            unit.Satiety--;
        }
        else
        {
            lifecycle.ChangeHitPoints(unit, -1);
        }
        WarnAboutSoldier(unit, unit.Satiety, HungrySoldierHint);
    }

    private void Tire(Unit unit)
    {
        int period = unit.AnimationNumber <= LastOwnEnergyAnimation
            ? animations[unit.AnimationNumber].EnergyDecayPeriod ?? NeverDecays
            : actions[unit.Action].EnergyDecayPeriod;
        if (!Decays(unit, period))
        {
            return;
        }
        if (unit.Energy > 0)
        {
            unit.Energy--;
        }
        else
        {
            lifecycle.ChangeHitPoints(unit, -1);
        }
        WarnAboutSoldier(unit, unit.Energy, TiredSoldierHint);
    }

    private bool Heal(Unit unit)
    {
        bool sick = (unit.Status & UnitStatus.Diseased) != 0;
        if (unit.WitchcraftSkill > SelfHealingWitchcraft && TaskCategory(unit) == WorkCategories.Sanctuary)
        {
            if (world.Frame % (SelfHealingPeriodBase - (unit.WitchcraftSkill * SelfHealingPeriodStep)) <= DecayWindow)
            {
                lifecycle.ChangeHitPoints(unit, SelfHealing);
            }
        }
        else if ((world.Frame & HealingMask) == (unit.Id & HealingSlotMask))
        {
            if (!sick)
            {
                lifecycle.ChangeHitPoints(unit, 1);
            }
        }
        else if (sick && (world.Frame & WastingMask) == (unit.Id & HealingSlotMask))
        {
            lifecycle.ChangeHitPoints(unit, -1);
            return SurvivesWounds(unit);
        }
        return true;
    }

    /// <summary>Whether a need with decay <paramref name="period"/> drops a point this frame.</summary>
    private bool Decays(Unit unit, int period) =>
        period < NeverDecays && world.Frame % (period * Slowdown(unit.Tribe)) <= DecayWindow;

    private int Slowdown(int tribe) => TribeRoles.IsPlayer(world, tribe) ? 1 : ComputerSlowdown;

    private int TaskCategory(Unit unit) => actions[unit.AssignedTask].Category;

    private void WarnAboutSoldier(Unit unit, int left, int hint)
    {
        if (left <= NeedWarningLevel && TribeRoles.Army(world, unit.Tribe).Rally && (unit.Status & UnitStatus.Mustered) != 0)
        {
            hints.Offer(hint, unit.Id, unit.Id);
        }
    }
}
