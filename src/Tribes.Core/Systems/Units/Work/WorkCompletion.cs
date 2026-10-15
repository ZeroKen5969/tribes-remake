using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The systems an action's completion hands over to.</summary>
/// <param name="Threats">Looking for the next threat.</param>
/// <param name="Targets">The target cycle.</param>
/// <param name="Needs">Needs between tasks.</param>
/// <param name="Gathering">Gathering more of a resource.</param>
/// <param name="Buildings">The nearest building of a kind.</param>
/// <param name="Materials">The stock.</param>
/// <param name="Deliveries">Armoury products.</param>
/// <param name="Courtship">Couples.</param>
internal sealed record CompletionServices(ThreatWatch Threats, TargetCycle Targets, NeedsChoices Needs, NodeGathering Gathering,
    NearestBuildings Buildings, WorkMaterials Materials, ArmouryDeliveries Deliveries, Courtship Courtship);

/// <summary>
/// What a unit does when its action is done: an attacker looks for its next threat, products are
/// delivered, couples go on, a captured child changes tribe, a dead target falls, needs are seen
/// to; then the unit takes up its next action and its next target.
/// </summary>
internal sealed class WorkCompletion
{
    /// <summary>Basic attacks with bare hands use the other variant (0 and 1, 3 and 4).</summary>
    private const int BareHands = 9, FirstUnitAttack = 0, FirstBuildingAttack = 3, VariantBit = 1;

    /// <summary>Actions with their own completion.</summary>
    private const int DeliverProduct = 26, SetTrap = 27, FirstSpell = 94, LastSpell = 97, ArmUp = 50, IntoTheHut = 111;
    private const int MeetingAction = 109, ManWalksToMeeting = 110, CaptureChild = ActionNumbers.CaptureChild;
    private const int SleepOutside = 79, LastMeleeAttack = 4;

    /// <summary>The man carries the couple's gift, and the thought cleared after sleeping.</summary>
    private const int Gift = 15, NoThought = -1;

    /// <summary>A child old enough goes to the nursery to play (action 73).</summary>
    private const int Nursery = 10, PlayInNursery = 73, NurseryAgeLimit = 11;

    /// <summary>Resuming after a pause; the weapon fetch actions whose weapon is carried back.</summary>
    private const int Resume = 85, FirstWeaponFetch = 44, LastWeaponFetch = 48, FirstWeaponItem = 8;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly CoupleLinks couples;
    private readonly UnitLifecycle lifecycle;
    private readonly Deaths deaths;
    private readonly CompletionServices services;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>Completes the actions of <paramref name="world"/>'s units.</summary>
    public WorkCompletion(GameWorld world, UnitActions actions, UnitOrders orders, CoupleLinks couples, UnitLifecycle lifecycle,
        Deaths deaths, CompletionServices services)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// The turn's unit has done its action; returns true when it went for something else (a threat,
    /// a need, more game, a nursery) instead of taking up its next action.
    /// </summary>
    public bool Finish(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (unit.Action <= ActionNumbers.LastBasicAttack && LooksForNextThreat(turn))
        {
            return true;
        }
        HandOver(turn);
        if (unit.Action <= LastMeleeAttack)
        {
            FellTarget(unit);
        }
        if ((definitions[unit.Action].Traits & ActionTraits.ChecksNeedsWhenDone) != 0 && services.Needs.SeeToNeeds(turn))
        {
            return true;
        }
        if (unit.Action == ActionNumbers.HuntingTask)
        {
            actions.SetAction(unit, ActionNumbers.GatherCarcass);
            if (services.Gathering.GoToNearbyNode(unit, true))
            {
                return true;
            }
            actions.SetAction(unit, ActionNumbers.HuntingTask);
        }
        if (unit.Age < NurseryAgeLimit && unit.Action == ActionNumbers.ChildIdle && services.Buildings.HeadFor(unit, Nursery))
        {
            actions.SetAction(unit, PlayInNursery);
            orders.FinishOrder(unit, OrderModes.Keep);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The turn's unit takes up the next action: a resting unit resumes what it paused (unless an
    /// order came), others the action that follows (an idle carrier delivers and stays idle); an
    /// armoury worker picks up its product; then it goes for its next target.
    /// </summary>
    public void TakeUpNext(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (unit.Action >= ActionPauses.Pausing && unit.SavedAction is >= FirstWeaponFetch and <= LastWeaponFetch)
        {
            unit.CarriedItem = unit.SavedAction - FirstWeaponFetch + FirstWeaponItem;
        }
        if (unit.Action == Resume)
        {
            Resumes(unit);
        }
        else
        {
            bool idleDelivery = unit.Action is >= ActionNumbers.FirstDelivery and <= ActionNumbers.LastDelivery
                && unit.AssignedTask == ActionNumbers.Idle;
            actions.SetAction(unit, idleDelivery ? ActionNumbers.Idle : definitions[unit.Action].NextActionOnComplete);
        }
        if (unit.Action is DeliverProduct or SetTrap)
        {
            services.Deliveries.PickUp(unit);
        }
        if (unit.OrderMode < OrderModes.Walk
            && (definitions[unit.Action].BuildingInteraction == BuildingInteraction.Visit || CoupleLinks.IsOnItsCell(world, unit)))
        {
            services.Targets.NextTarget(turn);
        }
        if (unit.Action == ActionNumbers.GatherBones)
        {
            services.Gathering.GoOn(turn);
        }
    }

    private bool LooksForNextThreat(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Weapon == BareHands && unit.Action is FirstUnitAttack or FirstBuildingAttack)
        {
            unit.Action ^= VariantBit;
        }
        turn.MayActFreely = true;
        turn.RegularDecision = false;
        return services.Threats.Look(turn);
    }

    /// <summary>
    /// The results of the action that do not depend on what comes next, in order (a couple step may
    /// change the action, and the later results then follow the new one).
    /// </summary>
    private void HandOver(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Action is DeliverProduct or SetTrap)
        {
            services.Deliveries.Deliver(unit);
        }
        if (unit.Action is >= FirstSpell and <= LastSpell)
        {
            services.Targets.CloseSpellOrder(unit);
        }
        if (unit.Action == ArmUp)
        {
            unit.Status &= ~UnitStatus.Mustered;
            services.Materials.TakeWeapon(unit);
        }
        if (unit.Action == IntoTheHut)
        {
            unit.CarriedAmount = 0;
            services.Courtship.Advance(turn);
        }
        if (unit.Action == MeetingAction)
        {
            couples.Dissolve(unit, 0);
        }
        HandOverOthers(unit);
    }

    private void HandOverOthers(Unit unit)
    {
        if (unit.Action == ManWalksToMeeting)
        {
            unit.CarriedItem = Gift;
        }
        if (unit.Action == CaptureChild && unit.TargetIndex >= 1 && unit.TargetIndex <= world.UnitSlots)
        {
            lifecycle.ChangeTribe(world.Unit(unit.TargetIndex), unit.Tribe);
        }
        if (unit.Action == SleepOutside)
        {
            unit.Thought = NoThought;
        }
    }

    /// <summary>A melee attacker whose target is still in the cell it attacked, at exactly 0 hit points, fells it.</summary>
    private void FellTarget(Unit unit)
    {
        var cell = new GridPoint(unit.DestinationX, unit.DestinationY);
        EntityProperty effect = definitions[unit.Action].EffectProperty;
        if (world.Map.Contains(cell) && CellCode.At(world.Map, cell) == unit.TargetCellCode
            && EntitySlots.Holds(world, effect, unit.TargetIndex) && EntityProperties.Get(world, effect, unit.TargetIndex) == 0)
        {
            deaths.Die(effect, cell, unit.TargetIndex, unit.TargetIndex > unit.Id, unit.Tribe);
        }
    }

    private void Resumes(Unit unit)
    {
        if (unit.OrderMode >= OrderModes.Walk)
        {
            unit.SavedAction = ActionNumbers.Idle;
        }
        actions.SetAction(unit, unit.SavedAction);
        if (unit.Action is >= FirstWeaponFetch and <= LastWeaponFetch)
        {
            unit.CarriedItem = 0;
        }
        unit.SavedAction = ActionNumbers.Idle;
        unit.ActionSubState = 0;
        unit.WorkProgress = 0;
    }
}
