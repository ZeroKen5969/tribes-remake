using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The systems a unit's decision uses.</summary>
/// <param name="Targets">The target cycle.</param>
/// <param name="Threats">Looking for threats.</param>
/// <param name="Needs">Needs between tasks.</param>
/// <param name="Gathering">Gathering more of a resource.</param>
/// <param name="Buildings">The nearest building of a kind.</param>
/// <param name="Spells">The witch doctor's spells.</param>
/// <param name="Courtship">Couples.</param>
/// <param name="Animation">Starting the next action step.</param>
internal sealed record DecisionServices(TargetCycle Targets, ThreatWatch Threats, NeedsChoices Needs, NodeGathering Gathering,
    NearestBuildings Buildings, Spells Spells, Courtship Courtship, ActionAnimation Animation);

/// <summary>
/// A unit's decisions while it stands on a cell centre: every few frames it reconsiders (the army's
/// call, its needs, going back to its task), it keeps after its target, and it plans a route when it
/// is out of reach of its goal.
/// </summary>
internal sealed partial class UnitDecisions
{
    /// <summary>The player's adults keep their ranges in line every 32 frames, staggered by slot.</summary>
    private const int RangeCheckMask = 31;

    /// <summary>A unit far from a target it follows (further than its sight + this) needs a route first.</summary>
    private const int FollowSlack = 2;

    /// <summary>Gatherer actions whose idle reconsideration has a meaning of their own.</summary>
    private const int GatherMeat = ActionNumbers.GatherCarcass, GatherBones = ActionNumbers.GatherBones;

    /// <summary>Waiting and settling: a waiting unit that will be idle afterwards reconsiders like an idle one.</summary>
    private const int Waiting = 84, SettlingAnimation = -109, MeetingAction = 109;

    /// <summary>Categories of the trainings (pia center, gym, range), whose units go back to their task.</summary>
    private const int FirstTrainingCategory = 13, LastTrainingCategory = 15;

    /// <summary>Units at least this happy go back to work at their task site.</summary>
    private const int ContentEnough = 30;

    /// <summary>A unit with this target index looks for something to gather nearby every 64 frames, staggered by slot.</summary>
    private const int PointTarget = -1, SearchMask = 63;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly RouteFinder routes;
    private readonly TargetDistances distances;
    private readonly Movement.WorkPositions positions;
    private readonly DecisionServices services;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>Lets the units of <paramref name="world"/> decide.</summary>
    public UnitDecisions(GameWorld world, UnitActions actions, UnitOrders orders, RouteFinder routes, TargetDistances distances,
        Movement.WorkPositions positions, DecisionServices services)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// The decisions of the turn's unit; returns true when its turn ends (it answered a call to arms).
    /// A unit walking, or under an order still to be carried out, does not decide.
    /// </summary>
    public bool Decide(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (((world.Frame & RangeCheckMask) == (unit.Id & RangeCheckMask) || world.Frame == 0)
            && TribeRoles.IsPlayer(world, unit.Tribe) && unit.Age >= UnitAges.Adult)
        {
            UnitSkillLimits.ClampRanges(unit);
        }
        if (!unit.IsCentred || unit.OrderMode >= OrderModes.Walk)
        {
            return false;
        }
        if (unit.Action == MeetingAction)
        {
            services.Courtship.Advance(turn);
        }
        if (unit.IdleTicks >= unit.MoveDelay - unit.RunBonus && (unit.AnimationNumber <= AnimationNumbers.LastWalking || unit.AnimationTick == 0))
        {
            if (unit.Age >= UnitAges.Adult && AnswersCall(unit))
            {
                unit.CarriedAmount = 0;
                return true;
            }
            Reconsider(turn);
            unit.IdleTicks = 0;
        }
        bool onCell = CoupleLinks.IsOnItsCell(world, unit);
        if (onCell || unit.Action is ActionNumbers.TowerShot or ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard)
        {
            KeepAtTarget(turn);
        }
        turn.GoalDistance = distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured;
        if (unit.IdleTicks == 0 && unit.PathLength <= unit.PathIndex && turn.GoalCells > routes.ActionRange(unit)
            && CoupleLinks.IsOnItsCell(world, unit))
        {
            services.Targets.Approach(turn);
        }
        return false;
    }

    /// <summary>What a unit reconsiders every few frames: its needs (or gathering on), then going back to its task.</summary>
    private void Reconsider(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        ActionDefinition action = definitions[unit.Action];
        bool noWorkEffect = action.EffectProperty == EntityProperty.UnitActionSubState
            && action.RateTarget.Property == EntityProperty.UnitActionSubState;
        bool training = action.Category is >= FirstTrainingCategory and <= LastTrainingCategory;
        bool waitingOutside = (action.BuildingInteraction != BuildingInteraction.WorkInside || !CoupleLinks.IsOnItsCell(world, unit))
            && unit.Animation != SettlingAnimation && unit.DestinationX == unit.X && unit.DestinationY == unit.Y;
        if (((noWorkEffect || training) && waitingOutside) || unit.TargetIndex == PointTarget)
        {
            if (unit.TargetIndex == PointTarget && unit.Action is >= ActionNumbers.FirstGathering and <= ActionNumbers.LastGathering
                && unit.CarriedAmount > 0)
            {
                services.Gathering.GoOn(turn);
            }
            else
            {
                services.Needs.SeeToNeeds(turn);
            }
        }
        if (ReturnsToTask(unit))
        {
            BackToTask(turn);
        }
    }

    /// <summary>Whether an idle adult (not hurrying, without orders) away from its task site goes back to it.</summary>
    private bool ReturnsToTask(Unit unit)
    {
        ArmyOrders army = TribeRoles.Army(world, unit.Tribe);
        bool calledUp = TribeRoles.IsPlayer(world, unit.Tribe)
            ? army.AllOut || (army.Rally && (unit.Status & UnitStatus.Mustered) != 0)
            : TribeRoles.Ai(world, unit.Tribe).Intruder >= 0;
        bool idle = unit.Action == ActionNumbers.Idle || (unit.Action == Waiting && unit.SavedAction == ActionNumbers.Idle);
        return !calledUp && unit.Age >= UnitAges.Adult && idle && unit.RunBonus == 0 && unit.OrderMode == OrderModes.Free
            && (unit.DestinationX != unit.TaskSiteX || unit.DestinationY != unit.TaskSiteY);
    }

    /// <summary>
    /// An idle unit goes back to its task: if what is at its task site is of another kind of work it
    /// drops the task (a hunter looks for more meat first, and failing that hunts on); otherwise it
    /// heads there, and a content unit takes the work up.
    /// </summary>
    private void BackToTask(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        var site = new GridPoint(unit.TaskSiteX, unit.TaskSiteY);
        OrderChoice choice = orders.PickAction(unit, site, true);
        actions.Stop(unit);
        int choiceCategory = definitions[choice.Action].Category;
        bool sameWork = choiceCategory == definitions[unit.AssignedTask].Category
            || (choice.Action == GatherBones && unit.AssignedTask == GatherMeat);
        if (!sameWork)
        {
            if (unit.AssignedTask is not (GatherMeat or GatherBones))
            {
                unit.TargetCellCode = 0;
            }
            else
            {
                actions.SetAction(unit, GatherMeat);
                if (!services.Gathering.GoToNearbyNode(unit, false))
                {
                    actions.SetAction(unit, ActionNumbers.HuntingTask);
                    unit.AssignedTask = GatherMeat;
                    unit.DestinationX = unit.TaskSiteX;
                    unit.DestinationY = unit.TaskSiteY;
                }
            }
            return;
        }
        unit.DestinationX = unit.TaskSiteX;
        unit.DestinationY = unit.TaskSiteY;
        if (unit.Happiness >= ContentEnough)
        {
            orders.FinishOrder(unit, OrderModes.Keep);
        }
    }

    /// <summary>A unit keeps after its target: it follows it, or (pointed at a cell) looks for more nearby now and then, or looks for a target.</summary>
    private void KeepAtTarget(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.TargetIndex >= 0)
        {
            Follow(turn);
        }
        else if (unit.TargetIndex >= PointTarget)
        {
            if ((world.Frame & SearchMask) == (unit.Id & SearchMask))
            {
                services.Gathering.GoOn(turn);
            }
        }
        else
        {
            services.Targets.NextTarget(turn);
        }
    }
}
