using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>The response to a threat found.</summary>
internal sealed partial class ThreatWatch
{
    /// <summary>
    /// The turn's unit responds to the threat it found. A tribe whose units do not act by themselves,
    /// or (before the story has started) a threat that is a child, an animal or outside the mission's
    /// passive zone: a unit free to act turns on the threat (from a building it pops out, a tower
    /// guard shoots), an unarmed worker runs for cover in the player's first building; one not free
    /// to act inside a building comes out. A sighted enemy is reported to the player, and the unit
    /// calls its tribe mates. A unit of a tribe that waits sees an enemy only to stop what it does.
    /// </summary>
    private void Respond(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        TargetSearch threat = turn.Threat;
        int code = CellCode.At(world.Map, threat.Location);
        bool storyStarted = world.Script.ObjectiveStage >= ScenePlaying;
        if (!world.Script.ActsByItself(unit.Tribe)
            || (!storyStarted && (threat.Priority == AnimalThreat || (threat.Priority == ChildThreat && world.Mission != VillageMission)
                || !InPassiveZone(threat.Location))))
        {
            if (!turn.MayActFreely)
            {
                ComeOutIfInside(unit);
            }
            else
            {
                TurnOn(turn, code);
            }
            ReportAndCall(unit, threat, code);
        }
        else if (threat.Priority == UnitThreat)
        {
            StandDown(unit);
        }
        if (TribeRoles.IsPlayer(world, unit.Tribe) && threat.Priority == UnitThreat && CoupleLinks.IsOnItsCell(world, unit))
        {
            encounters.Meet(unit, code > 0 ? CellCode.UnitOrOriginal(code) : 0, threat.Location);
        }
    }

    private bool InPassiveZone(GridPoint cell) =>
        world.Content.Rules.Missions.Missions[world.Mission].PassiveZone is { } zone && zone.Contains(cell);

    private void ComeOutIfInside(Unit unit)
    {
        if (!CoupleLinks.IsOnItsCell(world, unit) && unit.ActionSubState > 0)
        {
            unit.Action = ActionNumbers.LeaveBuilding;
            unit.OrderMode = OrderModes.Forced;
        }
    }

    private void TurnOn(UnitTurn turn, int code)
    {
        Unit unit = turn.Unit;
        TargetSearch threat = turn.Threat;
        bool fights = unit.Weapon != BareHands || unit.Action <= ActionNumbers.LastBasicAttack
            || !TribeRoles.IsPlayer(world, unit.Tribe) || TribeRoles.Army(world, unit.Tribe).AllOut || threat.Priority == ChildThreat
            || NearShelter(unit);
        bool onCell = CoupleLinks.IsOnItsCell(world, unit);
        if ((!fights && unit.Action == ActionNumbers.StayInside) || (!onCell && threat.Priority == ChildThreat))
        {
            return;
        }
        unit.DestinationX = fights ? threat.Location.X : unit.X;
        unit.DestinationY = fights ? threat.Location.Y : unit.Y;
        if (!fights)
        {
            RunForShelter(unit);
        }
        else if (onCell)
        {
            orders.FinishOrder(unit, OrderModes.Keep);
            turn.GoalDistance = distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured;
            if (turn.GoalCells > routes.ActionRange(unit))
            {
                targets.Approach(turn);
            }
        }
        else
        {
            ShootFromInside(turn, code);
        }
    }

    /// <summary>Whether <paramref name="unit"/> is within reach of its player's first building, where the unarmed take cover.</summary>
    private bool NearShelter(Unit unit)
    {
        int slot = world.FirstBuildingSlot(TribeRoles.PlayerOf(world, unit.Tribe));
        if (slot == 0)
        {
            return false;
        }
        Building shelter = world.Building(slot);
        return System.Math.Abs(unit.X - shelter.X) <= ShelterReach && System.Math.Abs(unit.Y - shelter.Y) <= ShelterReach;
    }

    private void RunForShelter(Unit unit)
    {
        actions.SetAction(unit, ActionNumbers.StayInside);
        unit.Animation = RunForCover;
        unit.AnimationTick = 0;
        unit.CarriedAmount = 0;
        unit.PathLength = 0;
    }

    /// <summary>A unit inside a building turns on the threat from there (a tower guard shoots at it).</summary>
    private void ShootFromInside(UnitTurn turn, int code)
    {
        Unit unit = turn.Unit;
        TargetSearch threat = turn.Threat;
        if (unit.Action <= LastPopOutAction && EntitySlots.Holds(world, EntityProperty.BuildingX, unit.TargetIndex))
        {
            unit.X = world.Building(unit.TargetIndex).X;
            unit.Y = world.Building(unit.TargetIndex).Y;
        }
        Content.Rules.ThreatResponse response = world.Content.Rules.Targets.ThreatResponses[threat.Priority];
        unit.TargetCellCode = code;
        unit.TargetIndex = CellCode.SlotOf(code, response.CellCodeSign, threat.Priority == AnimalThreat);
        actions.SetAction(unit, response.Action);
        turn.GoalDistance = distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured;
        animation.Start(unit, turn.GoalDistance);
        unit.AnimationTick = 0;
        unit.ActionSubState = 1;
    }

    /// <summary>
    /// The player hears about a sighted enemy (unless the unit is hidden), and a unit that sees an
    /// enemy or an animal it hunts on a one-off order calls its tribe mates nearby.
    /// </summary>
    private void ReportAndCall(Unit unit, TargetSearch threat, int code)
    {
        bool hidden = (unit.Status & UnitStatus.Hidden) != 0;
        if (threat.Priority == UnitThreat && TribeRoles.IsPlayer(world, unit.Tribe))
        {
            if (!hidden)
            {
                hints.Offer(EnemyHint, unit.Id, code);
            }
            HintState state = TribeRoles.Hints(world, unit.Tribe);
            if (state.Cooldown(EnemyNoticeChannel) < 0)
            {
                state.SetCooldown(EnemyNoticeChannel, 0);
                hints.Notice(unit.Tribe, EnemyNotice, threat.Location.X, threat.Location.Y, 0);
            }
        }
        bool calls = threat.Priority == UnitThreat || (threat.Priority == AnimalThreat && (unit.Status & UnitStatus.OneOffHunt) != 0);
        if (calls && !hidden)
        {
            sectors.RaiseAlarm(unit.Tribe, new GridPoint(unit.X, unit.Y), unit.SightRadius, code);
        }
    }

    /// <summary>A unit of a waiting tribe that sees an enemy stops: outside it stands idle, inside it comes out.</summary>
    private void StandDown(Unit unit)
    {
        if (unit.BuildingSlot < 0)
        {
            StopAtNextCell(unit, false);
            actions.SetAction(unit, ActionNumbers.Idle);
        }
        else
        {
            actions.SetAction(unit, ActionNumbers.LeaveBuilding);
            unit.OrderMode = OrderModes.Forced;
        }
    }

    /// <summary>
    /// <paramref name="unit"/> stops at its cell, or at the cell it is stepping into when it is in the
    /// first half of a step; with <paramref name="settle"/> it also makes that cell its task site and idles there.
    /// </summary>
    public void StopAtNextCell(Unit unit, bool settle)
    {
        System.ArgumentNullException.ThrowIfNull(unit);
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        if (unit.AnimationNumber <= AnimationNumbers.LastWalking && unit.StepTicks < HalfStep(unit)
            && unit.ActionSubState >= 0 && unit.PathLength > 0)
        {
            DirectionStep step = world.Content.Rules.Movement.Directions[unit.Facing];
            unit.DestinationX += step.Dx;
            unit.DestinationY += step.Dy;
        }
        unit.PathLength = 0;
        if (settle)
        {
            unit.TaskSiteX = unit.DestinationX;
            unit.TaskSiteY = unit.DestinationY;
            actions.SetAction(unit, ActionNumbers.Idle);
        }
    }

    private int HalfStep(Unit unit)
    {
        SpeedClass speed = world.Content.Rules.Movement.SpeedClasses[unit.MoveDelay - unit.RunBonus];
        return (((unit.Facing & 1) == 0 ? speed.Straight.StepFrames : speed.Diagonal.StepFrames) + 1) / 2;
    }
}
