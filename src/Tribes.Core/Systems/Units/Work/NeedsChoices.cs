using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// An adult looking after itself between tasks: a couple goes on, a happy adult may find a
/// partner, a tired one goes to sleep, a hungry one to the kitchen, an unhappy one to the disco,
/// a hurt or sick one to the sanctuary.
/// </summary>
internal sealed class NeedsChoices
{
    /// <summary>The mission in which units have no needs.</summary>
    private const int NoNeedsMission = 3;

    /// <summary>A decay period meaning "never".</summary>
    private const int NeverDecays = 127;

    /// <summary>Energy or satiety at or below this is a need; much lower for a happy unit.</summary>
    private const int NeedWhenUnhappy = 50, NeedWhenHappy = 20, HappyAbove = 5;

    /// <summary>Unhappy units go to the disco; the very unhappy stop working.</summary>
    private const int Unhappy = 30, Miserable = 5;

    /// <summary>A unit is hurt below two thirds of its maximum hit points.</summary>
    private const int HurtNumerator = 2, HurtDenominator = 3;

    /// <summary>The workplaces of needs, and the actions of the units working there.</summary>
    private const int Kitchen = BuildingKindNumbers.Kitchen, Disco = BuildingKindNumbers.Disco, Sanctuary = BuildingKindNumbers.Sanctuary;
    private const int Cook = 66, Entertainer = 86;

    /// <summary>Hints when the building of a need is missing, and the hint of a homeless unit.</summary>
    private const int NoKitchenHint = 0, NoDiscoHint = 2, NoSanctuaryHint = 3, HomelessHint = 1;

    /// <summary>A sleeper picks the disco instead of sleeping outside when a 0..2 roll gives this.</summary>
    private const int DiscoRoll = 1, RollMask = 0xFF, RollRange = 3;

    /// <summary>Sleeping outside, and its animation.</summary>
    private const int SleepOutside = 79, SleepingAnimation = 40;

    /// <summary>A hut in this birth stage or later is taken by a couple.</summary>
    private const int HutTaken = 2;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly NearestBuildings buildings;
    private readonly HintSystem hints;
    private readonly Courtship courtship;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>Lets the adults of <paramref name="world"/> look after themselves.</summary>
    public NeedsChoices(GameWorld world, UnitActions actions, UnitOrders orders, NearestBuildings buildings, HintSystem hints,
        Courtship courtship)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.courtship = courtship ?? throw new ArgumentNullException(nameof(courtship));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// The unit of <paramref name="turn"/> sees to its most pressing need, if any; returns whether it went for
    /// one. Nobody has needs in the mission without them, while the army is out, in an army called
    /// to the rally point, or during actions whose needs never change.
    /// </summary>
    public bool SeeToNeeds(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        bool mustered = (unit.Status & UnitStatus.Mustered) != 0;
        ArmyOrders army = TribeRoles.Army(world, unit.Tribe);
        bool rallied = (army.Rally || TribeRoles.IsComputer(world, unit.Tribe)) && mustered;
        if (world.Mission == NoNeedsMission || army.AllOut || rallied || unit.Age < UnitAges.Adult)
        {
            return false;
        }
        if (TribeRoles.IsPlayer(world, unit.Tribe) && (courtship.Advance(turn) || courtship.SeekPartner(turn, false, 0)))
        {
            return true;
        }
        ActionDefinition action = definitions[unit.Action];
        if (action.SatietyDecayPeriod == NeverDecays || action.EnergyDecayPeriod == NeverDecays)
        {
            return false;
        }
        int needLevel = unit.Happiness > HappyAbove ? NeedWhenHappy : NeedWhenUnhappy;
        if (unit.Energy <= needLevel)
        {
            Sleep(unit);
            return true;
        }
        if (unit.Satiety <= needLevel && unit.AssignedTask != Cook && unit.Action != Cook && buildings.GoFor(unit, Kitchen, NoKitchenHint))
        {
            return true;
        }
        if (unit.Happiness <= Unhappy && unit.AssignedTask != Entertainer && unit.Action != Entertainer)
        {
            if (buildings.GoFor(unit, Disco, NoDiscoHint))
            {
                return true;
            }
            if (unit.Happiness <= Miserable && action.Category != 0)
            {
                if (!CoupleLinks.IsOnItsCell(world, unit))
                {
                    actions.SetAction(unit, ActionNumbers.LeaveBuilding);
                    unit.OrderMode = OrderModes.Forced;
                    return true;
                }
                actions.Stop(unit);
            }
        }
        bool hurt = unit.HitPoints < unit.MaxHitPoints * HurtNumerator / HurtDenominator || (unit.Status & UnitStatus.Diseased) != 0;
        return hurt && unit.AssignedTask != ActionNumbers.WitchDoctor && unit.Action != ActionNumbers.WitchDoctor
            && buildings.GoFor(unit, Sanctuary, NoSanctuaryHint);
    }

    /// <summary>A tired unit goes home, sometimes to the disco, or sleeps where it is; a homeless one is pitied.</summary>
    private void Sleep(Unit unit)
    {
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            actions.SetAction(unit, ActionNumbers.LeaveBuilding);
            orders.FinishOrder(unit, OrderModes.Keep);
            return;
        }
        int roll = world.Random.NextMasked(RollMask, RollRange);
        Building? home = unit.Home > 0 && unit.Home <= world.BuildingSlots ? world.Building(unit.Home) : null;
        if (home is not null && home.WorkMode == BuildingWorkMode.Normal && home.Aux < HutTaken)
        {
            unit.DestinationX = home.X;
            unit.DestinationY = home.Y;
            orders.FinishOrder(unit, OrderModes.Keep);
        }
        else if (roll == DiscoRoll && buildings.HeadFor(unit, Disco))
        {
            orders.FinishOrder(unit, OrderModes.Keep);
        }
        else
        {
            unit.DestinationX = unit.X;
            unit.DestinationY = unit.Y;
            actions.SetAction(unit, SleepOutside);
            unit.Animation = SleepingAnimation;
            unit.AnimationTick = 0;
        }
        if (unit.Home <= 0)
        {
            hints.Offer(HomelessHint, unit.Id, unit.Id);
        }
    }
}
