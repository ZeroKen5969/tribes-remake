using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>Operations of the units' call cycle the exit from a building continues with.</summary>
/// <param name="SeeToNeeds">The unit looks after its needs (food, rest, a partner); returns whether it went off for one.</param>
/// <param name="Respond">The unit decides what to do anew.</param>
/// <param name="Replan">The unit plans its route again.</param>
internal sealed record UnitCalls(Func<UnitTurn, bool> SeeToNeeds, Action<UnitTurn> Respond, Action<UnitTurn> Replan);

/// <summary>
/// A unit leaves the building it is in: it leaves the occupants (taking the sickness sign along
/// unless another sick unit stays), appears on the free cell nearest to the exit, facing out, and
/// goes on: an idler wanders off nearby unless its needs call, others decide anew. The first time
/// a scout leaves a building in the tutorial its scene is shown.
/// </summary>
internal sealed class BuildingExit
{
    /// <summary>Spiral rounds searched for a free cell near the exit, and for a cell to wander to.</summary>
    private const int ExitRounds = 127, WanderRounds = 21;

    /// <summary>An idler wanders to a cell up to this many cells away in each direction (a roll of 0..5 minus 3).</summary>
    private const int WanderRollMask = 255, WanderRollRange = 6, WanderReach = 3;

    /// <summary>Building flag: someone sick is inside.</summary>
    private const int SickOccupantFlag = 0x02;

    /// <summary>The movement tick counter (low four bits of <see cref="Unit.StepAndActionTicks"/>).</summary>
    private const int StepTickBits = 0x0F;

    /// <summary>Tutorial flags: on, and the scout scene played; the scene and the missions it is shown in.</summary>
    private const int TutorialOn = 1, ScoutSceneShown = 2, ScoutScene = 61, LastTutorialMission = 6;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly Occupancy occupancy;
    private readonly SpiralSearch spiral;
    private readonly StoryProgress story;
    private readonly UnitGoals goals;
    private readonly UnitCalls calls;
    private readonly Action<GameEvent> raise;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public BuildingExit(GameWorld world, UnitActions actions, Occupancy occupancy, SpiralSearch spiral, StoryProgress story,
        UnitGoals goals, UnitCalls calls, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        this.goals = goals ?? throw new ArgumentNullException(nameof(goals));
        this.calls = calls ?? throw new ArgumentNullException(nameof(calls));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary>The unit of <paramref name="turn"/> leaves the building it targets.</summary>
    public void Leave(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        Building building = world.Building(unit.TargetIndex);
        building.OccupantCount--;
        Unlink(unit, building);
        TakeSicknessAlong(unit, building);
        unit.BuildingSlot = WorkPositionNumbers.None;
        unit.NextOccupantOrCellObject = 0;
        occupancy.UpdateRange(building);
        BuildingOperation? operation = world.Content.Rules.Buildings.Kinds[building.Kind].Operation;
        turn.ClearThreat();
        var exit = new GridPoint(building.X + (operation?.ExitOrigin.Dx ?? 0), building.Y + (operation?.ExitOrigin.Dy ?? 0));
        if (spiral.FindEmptyCell(exit, ExitRounds) is { } free)
        {
            (unit.DestinationX, unit.DestinationY) = (free.X, free.Y);
        }
        Appear(unit, operation?.ExitFacing ?? 0);
        GoOn(turn);
    }

    private void Unlink(Unit unit, Building building)
    {
        if (building.FirstOccupant == unit.Id)
        {
            building.FirstOccupant = unit.NextOccupantOrCellObject;
            return;
        }
        int id = building.FirstOccupant;
        while (id != 0 && world.Unit(id).NextOccupantOrCellObject != unit.Id)
        {
            id = world.Unit(id).NextOccupantOrCellObject;
        }
        if (id != 0)
        {
            world.Unit(id).NextOccupantOrCellObject = unit.NextOccupantOrCellObject;
        }
    }

    /// <summary>A sick unit takes the building's sickness sign along, unless another sick unit stays inside.</summary>
    private void TakeSicknessAlong(Unit unit, Building building)
    {
        if ((building.Flags & SickOccupantFlag) == 0 || (unit.Status & UnitStatus.Diseased) == 0)
        {
            return;
        }
        building.Flags &= ~SickOccupantFlag;
        for (int id = building.FirstOccupant; id > 0 && world.Unit(id).NextOccupantOrCellObject != 0;
            id = world.Unit(id).NextOccupantOrCellObject)
        {
            if ((world.Unit(id).Status & UnitStatus.Diseased) != 0
                && (building.WorkMode != BuildingWorkMode.Normal || building.Kind != BuildingKindNumbers.Sanctuary))
            {
                building.Flags |= SickOccupantFlag;
                break;
            }
        }
    }

    /// <summary>The unit appears on its destination cell facing out of the building.</summary>
    private void Appear(Unit unit, int facing)
    {
        (unit.X, unit.Y, unit.OffsetX, unit.OffsetY) = (unit.DestinationX, unit.DestinationY, 0, 0);
        unit.Facing = facing;
        world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.UnitAt(unit.Id));
        unit.ActionSubState = SubStates.Normal;
        unit.ThrowRange -= Arrival.InsideThrowBonus;
        if (unit.Action == ActionNumbers.StayInside)
        {
            unit.AssignedTask = ActionNumbers.Idle;
            (unit.TaskSiteX, unit.TaskSiteY) = (unit.X, unit.Y);
        }
        UnitActions.Stand(unit);
        unit.StepAndActionTicks &= ~StepTickBits;
        unit.IdleTicks = unit.MoveDelay;
        raise(new UnitDeselected(unit.Id));
        if (unit.Action >= ActionNumbers.FirstCoupleAction)
        {
            unit.OrderMode = OrderModes.Free;
        }
    }

    /// <summary>What the unit does next: the scout scene, wandering off after a visit, or a new decision.</summary>
    private void GoOn(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        ScriptState script = world.Script;
        if ((script.TutorialFlags & (TutorialOn | ScoutSceneShown)) == TutorialOn
            && unit.ScoutingSkill > Senses.UnitStealth.ScoutingThreshold
            && TribeRoles.IsPlayer(world, unit.Tribe) && world.Mission <= LastTutorialMission)
        {
            story.CastActors(unit.Id, 0, 1);
            story.StartScene(ScoutScene);
            script.TutorialFlags |= ScoutSceneShown;
            return;
        }
        if (actions.Definition(unit.Action).NextActionOnComplete == ActionNumbers.Idle && unit.Action < ActionNumbers.FirstCoupleAction)
        {
            if (!calls.SeeToNeeds(turn))
            {
                Wander(unit, turn);
                actions.Stop(unit);
            }
            calls.Replan(turn);
            turn.GoalDistance = goals.Measure(unit);
            return;
        }
        if (unit.OrderMode < OrderModes.Walk)
        {
            calls.Respond(turn);
        }
    }

    /// <summary>The unit picks a free cell around a random cell nearby as its destination.</summary>
    private void Wander(Unit unit, UnitTurn turn)
    {
        int x = world.Map.ClampColumn(unit.X - WanderReach + world.Random.NextMasked(WanderRollMask, WanderRollRange));
        int y = world.Map.ClampRow(unit.Y - WanderReach + world.Random.NextMasked(WanderRollMask, WanderRollRange));
        turn.ClearThreat();
        if (spiral.FindEmptyCell(new GridPoint(x, y), WanderRounds) is { } free)
        {
            (unit.DestinationX, unit.DestinationY) = (free.X, free.Y);
        }
    }
}
