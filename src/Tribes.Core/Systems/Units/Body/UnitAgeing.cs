using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>
/// A unit growing older, checked every fourth frame: a year passes when its age counter is full
/// (the computer's units age faster), the maximum hit points follow the age table with a little
/// chance, a strong young body walks a bit faster, children come of age at 12. Then the unit's
/// needs are tended and the protection spell wears off.
/// </summary>
internal sealed class UnitAgeing
{
    /// <summary>Age ticks of a year for the player's units and for the computer's.</summary>
    private const int PlayerYear = 125, ComputerYear = 50;

    /// <summary>The yearly change of maximum hit points varies by a roll of 0..4 minus 2.</summary>
    private const int YearRollMask = 255, YearRollRange = 5, YearRollCentre = 2;

    /// <summary>Gender 1: the yearly change is one point closer to 0.</summary>
    private const int MilderGender = 1;

    /// <summary>A unit whose maximum hit points grow past this walks one class faster, down to the fastest grown-up class.</summary>
    private const int StrongBody = 55, FastestGrownUpMoveDelay = 6;

    /// <summary>A child of this age inside a building stays there until it is grown up.</summary>
    private const int LastChildAge = 11;

    /// <summary>Animation frame a unit coming of age continues at (the child's animation ends soon).</summary>
    private const int ComingOfAgeAnimationTick = 20;

    /// <summary>The protection spell lasts this many checks.</summary>
    private const int ProtectionChecks = 120;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitSpawning spawning;
    private readonly UnitNeeds needs;
    private readonly IReadOnlyList<AgeStep> ageSteps;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitAgeing(GameWorld world, UnitActions actions, UnitSpawning spawning, UnitNeeds needs)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.needs = needs ?? throw new ArgumentNullException(nameof(needs));
        ageSteps = world.Content.Rules.Units.AgeSteps;
    }

    /// <summary>
    /// The body's update of <paramref name="unit"/> (every fourth frame): ageing, an eleven-year-old
    /// kept inside, the needs, the protection spell. Returns false when the unit died.
    /// </summary>
    public bool Update(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.AgeTicks >= YearLength(unit.Tribe) && !GrowOlder(unit))
        {
            return false;
        }
        if (unit.Age == LastChildAge && !CoupleLinks.IsOnItsCell(world, unit) && unit.ActionSubState >= 0)
        {
            unit.OrderMode = OrderModes.Forced;
            actions.SetAction(unit, ActionNumbers.StayInside);
        }
        if (!needs.Tend(unit))
        {
            return false;
        }
        if ((unit.Status & UnitStatus.Protected) != 0)
        {
            unit.ProtectionTicks++;
            if (unit.ProtectionTicks >= ProtectionChecks)
            {
                unit.ProtectionTicks = 0;
                unit.Status &= ~UnitStatus.Protected;
            }
        }
        return true;
    }

    private int YearLength(int tribe) => TribeRoles.IsPlayer(world, tribe) ? PlayerYear : ComputerYear;

    /// <summary>A year passes for <paramref name="unit"/>; returns false when it died of it.</summary>
    private bool GrowOlder(Unit unit)
    {
        unit.AgeTicks = 0;
        unit.Age++;
        AgeStep step = ageSteps[Math.Min(unit.Age, ageSteps.Count - 1)];
        int change = step.MaxHitPointsDelta + world.Random.NextMasked(YearRollMask, YearRollRange) - YearRollCentre;
        if (unit.Gender == MilderGender)
        {
            change += change > 0 ? -1 : 1;
        }
        unit.MaxHitPoints = GridMath.Clamp(unit.MaxHitPoints + change, 0, step.MaxHitPointsCap);
        if (change > 0)
        {
            unit.HitPoints += change;
            if (unit.MaxHitPoints >= StrongBody && unit.MaxHitPoints - change < StrongBody
                && unit.MoveDelay > FastestGrownUpMoveDelay && unit.IsCentred)
            {
                unit.MoveDelay--;
            }
        }
        unit.HitPoints = GridMath.Clamp(unit.HitPoints, 0, unit.MaxHitPoints);
        if (!needs.SurvivesWounds(unit))
        {
            return false;
        }
        if (unit.Age == UnitAges.Adult)
        {
            ComeOfAge(unit);
        }
        if (unit.Age == UnitAges.Adult || unit.Age == UnitAges.Elder)
        {
            spawning.PickPortrait(unit);
        }
        return true;
    }

    /// <summary>A child becomes an adult: it idles where it stands, which becomes its task site.</summary>
    private void ComeOfAge(Unit unit)
    {
        actions.SetAction(unit, ActionNumbers.Idle);
        UnitSkillLimits.ClampRanges(unit);
        unit.AssignedTask = ActionNumbers.Idle;
        unit.AnimationTick = ComingOfAgeAnimationTick;
        unit.TaskSiteX = unit.X;
        unit.DestinationX = unit.X;
        unit.TaskSiteY = unit.Y;
        unit.DestinationY = unit.Y;
    }
}
