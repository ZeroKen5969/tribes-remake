using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The computer's attack waves: a wave takes the free fighters (and reinforcements born at home),
/// then walks one of the mission's attack routes waypoint by waypoint until it has walked it all
/// or no attacker is left. In a match the wave goes for the enemy the computer picks
/// (<see cref="ComputerTargets"/>), along the route laid from its camp to that enemy; with no
/// enemy left no wave leaves.
/// </summary>
internal sealed class AttackWaves
{
    /// <summary>No wave is out.</summary>
    public const int NoWave = -1;

    /// <summary>The weapon of the reinforcements before they take one from the stock.</summary>
    private const int ReinforcementWeapon = 0;

    /// <summary>The wave growth divisor is given in thousands of frames.</summary>
    private const int GrowthUnit = 1000;

    /// <summary>The first mission of the custom maps; its waves wait for a seventh of the ramp-up.</summary>
    private const int FirstCustomMission = 7, CustomRampDivisor = 7;

    /// <summary>The tutorial: its waves wait for the tutorial step that schedules them.</summary>
    private const int TutorialMission = 0, AttacksScheduledStep = 11;

    /// <summary>Random masks of the wave's draws.</summary>
    private const int SizeRoll = 0xFFFFFF, ChoiceRoll = 0xFFFF;

    /// <summary>Rolls of 0..9 below this let scouts of any skill join; others must be poor scouts.</summary>
    private const int ScoutRolls = 10, ScoutsJoinBelow = 2, PoorScoutSkill = 3;

    /// <summary>Female units stay home while this flag of the computer's flags is set.</summary>
    private const int WomenStayHome = 0x80;

    /// <summary>The attackers walk on once each has stood at its waypoint; also after this many frames per waypoint.</summary>
    private const int FramesPerWaypoint = 150;

    /// <summary>An attacker farther than this (measured as 10 * cells + facing) from its goal holds the wave back.</summary>
    private const int CloseToGoal = 69;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;
    private readonly ComputerTargets targets;
    private readonly GroupSpawner groups;
    private readonly TargetDistances distances;

    /// <summary>Sends the computer's waves in <paramref name="world"/>.</summary>
    public AttackWaves(GameWorld world, ComputerPlans plans, ComputerTargets targets, GroupSpawner groups, TargetDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.groups = groups ?? throw new ArgumentNullException(nameof(groups));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
    }

    /// <summary>
    /// Sends a wave of <paramref name="tribe"/>: n attackers, n growing with the frame up to the
    /// difficulty's limit and drawn at random around it; during the ramp-up the attackers are
    /// weaker. Units without a post join first; reinforcements make up the rest.
    /// </summary>
    public void Launch(int tribe, int jobCount, int rampUp)
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        int clock = Endless.EndlessPacing.ComputerFrames(world, tribe);
        if ((world.Mission >= FirstCustomMission && clock < rampUp / CustomRampDivisor) || !Endless.EndlessPacing.MaySendWaves(world, tribe))
        {
            return;
        }
        int target = world.Mode == WorldMode.Match ? targets.Choose(tribe) : -1;
        if (world.Mode == WorldMode.Match && target < 0)
        {
            return;
        }
        AiState ai = TribeRoles.Ai(world, tribe);
        int level = TribeRoles.Difficulty(world, tribe);
        MissionDifficulty difficulty = mission.Difficulties[level];
        int size = GridMath.Clamp(difficulty.AttackWaveBaseSize + (clock / difficulty.AttackWaveGrowthDivisor / GrowthUnit),
            0, difficulty.AttackWaveMaxSize);
        size = (size / 2) + world.Random.NextMasked(SizeRoll, size + 1);
        int skill = difficulty.AttackWaveUnitSkill;
        if (clock < rampUp && (world.Mission != TutorialMission || world.Script.TutorialObjective < AttacksScheduledStep))
        {
            if (world.Mission == TutorialMission)
            {
                return;
            }
            skill = (skill / (3 - level)) + (skill * clock / rampUp / 2);
        }
        int scoutRoll = world.Random.NextMasked(ChoiceRoll, ScoutRolls);
        int joined = Muster(tribe, ai, jobCount, rampUp, size, skill, scoutRoll);
        ai.Flags &= ~WomenStayHome;
        groups.Spawn(size - joined, plans.For(tribe).Home, tribe, ReinforcementWeapon, skill);
        ai.WaveStage = world.Random.NextMasked(ChoiceRoll, mission.Ai.AttackRoutes.Count);
        ai.WaveTarget = 0;
        if (world.Mode == WorldMode.Match)
        {
            ai.WaveTribe = target;
            ai.SetRoute(targets.Route(tribe, target, ai.WaveStage));
        }
    }

    /// <summary>
    /// The wave walks on (every 16 frames): attackers inside buildings come out, idle ones head for
    /// the current waypoint; the wave goes on to the next one when all have reached it or its time
    /// is up, and ends after the last waypoint or when no attacker is left.
    /// </summary>
    public void Advance(int tribe)
    {
        AiState ai = TribeRoles.Ai(world, tribe);
        IReadOnlyList<GridPoint> route = world.Mode == WorldMode.Match
            ? ai.Route
            : world.Content.Rules.Missions.Missions[world.Mission].Ai.AttackRoutes[ai.WaveStage];
        if (route.Count == 0)
        {
            ai.WaveTarget = NoWave;
            return;
        }
        bool arrived = true;
        int attackers = 0;
        ListView<Unit> units = world.UnitsOf(tribe);
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit.HitPoints <= 0 || unit.Age < UnitAges.Adult || (unit.Status & UnitStatus.Mustered) == 0)
            {
                continue;
            }
            attackers++;
            arrived &= Lead(unit, route[ai.WaveTarget], arrived);
        }
        if (arrived || ai.WaveStartFrame + ((ai.WaveTarget + 1) * FramesPerWaypoint) < world.Frame)
        {
            ai.WaveTarget++;
        }
        if (attackers == 0 || ai.WaveTarget >= route.Count)
        {
            ai.WaveTarget = NoWave;
        }
    }

    /// <summary>Moves one attacker on; returns whether the wave may still count as arrived.</summary>
    private bool Lead(Unit unit, GridPoint waypoint, bool arrivedSoFar)
    {
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            unit.Action = ActionNumbers.LeaveBuilding;
            unit.OrderMode = OrderModes.Forced;
            return false;
        }
        bool arrived = arrivedSoFar;
        if (unit.OrderMode == OrderModes.Free)
        {
            unit.TaskSiteX = unit.DestinationX = waypoint.X;
            unit.TaskSiteY = unit.DestinationY = waypoint.Y;
            unit.OrderMode = OrderModes.Forced;
            unit.Action = ActionNumbers.Idle;
        }
        else
        {
            arrived = false;
        }
        return arrived && distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured <= CloseToGoal;
    }

    private int Muster(int tribe, AiState ai, int jobCount, int rampUp, int size, int skill, int scoutRoll)
    {
        int joined = 0;
        ListView<Unit> units = world.UnitsOf(tribe);
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (!CanJoin(unit, ai, unit.SlotInTribe, jobCount, skill, scoutRoll))
            {
                continue;
            }
            unit.Status |= UnitStatus.Mustered;
            unit.MeleeSkill = GridMath.Clamp(unit.MeleeSkill, 0, skill);
            unit.RangedSkill = GridMath.Clamp(unit.RangedSkill, 0, skill);
            int clock = Endless.EndlessPacing.ComputerFrames(world, tribe);
            if (clock < rampUp)
            {
                int max = unit.MaxHitPoints;
                unit.MaxHitPoints = (max / 2) + (max * clock / rampUp / 2);
                unit.HitPoints = unit.MaxHitPoints;
            }
            joined++;
            if (joined >= size)
            {
                break;
            }
        }
        return joined;
    }

    private static bool CanJoin(Unit unit, AiState ai, int slot, int jobCount, int skill, int scoutRoll) =>
        unit.HitPoints > 0 && unit.Age >= UnitAges.Adult && ai.JobSlot(slot) >= jobCount
        && (unit.Status & UnitStatus.Mustered) == 0 && unit.Weapon != UnitSpawning.BareHands
        && (unit.Gender != 1 || (ai.Flags & WomenStayHome) == 0)
        && (unit.MeleeSkill >= skill / 2 || unit.RangedSkill >= skill / 2 || unit.ScoutingSkill >= skill / 2)
        && (scoutRoll < ScoutsJoinBelow || unit.ScoutingSkill <= PoorScoutSkill);
}
