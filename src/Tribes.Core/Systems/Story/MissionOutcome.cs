using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>The mission is over: <paramref name="Victory"/> tells won from lost; the host shows the result screen.</summary>
/// <param name="Victory">Whether the player won.</param>
public sealed record MissionEnded(bool Victory) : GameEvent;

/// <summary>
/// Victory and defeat: the mission is won when the enemy has lost every building (where the
/// mission says so) and lost when the player's tribe is gone or (where the mission says so) its
/// first building is destroyed. Either starts a countdown that runs on normal frames only; the
/// result is shown when it reaches its end. The fifth mission instead goes on to its second part.
/// </summary>
internal sealed class MissionOutcome
{
    /// <summary>The countdown's start for a victory and for a defeat.</summary>
    public const int VictoryStart = 1, DefeatStart = 50;

    /// <summary>The countdown values at which the result is shown.</summary>
    public const int VictoryShown = 41, DefeatShown = 90;

    /// <summary>End condition bits: won without enemy buildings; lost with the first building.</summary>
    private const int WinsWithoutEnemyBuildings = 1, LosesFirstBuilding = 2;

    /// <summary>The enemy is beaten when it has no more than this many buildings and no units.</summary>
    private const int BeatenBuildings = 4;

    /// <summary>The village mission jumps to this story stage when its enemy is beaten.</summary>
    private const int VillageMission = 2, VillageBeatenStage = 2;

    /// <summary>Score weights are given in tenths of a point.</summary>
    private const int TenthsPerPoint = 10;

    /// <summary>The two-part mission and the countdown value at which it goes on to its second part.</summary>
    private const int TwoPartMission = 5, SecondPartAt = 20;

    private readonly GameWorld world;
    private readonly MissionSetup setup;
    private readonly Action<GameEvent> raise;

    /// <summary>Watches the outcome of <paramref name="world"/>'s mission.</summary>
    public MissionOutcome(GameWorld world, MissionSetup setup, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary>Starts the victory or defeat countdown when the mission is decided (once per frame, with the story).</summary>
    public void Check()
    {
        TribeState player = world.Tribes[TribeRoles.Player(world)];
        TribeState enemy = world.Tribes[TribeRoles.Computer(world)];
        int conditions = world.Content.Rules.Missions.Missions[world.Mission].EndConditions;
        bool firstBuildingLost = world.Building(world.FirstBuildingSlot(TribeRoles.Player(world))).HitPoints <= 0;
        if (enemy[TribeCounter.Buildings] <= 0 && (conditions & WinsWithoutEnemyBuildings) != 0)
        {
            StartCountdown(VictoryStart);
        }
        else if (player[TribeCounter.Population] <= 0 || (firstBuildingLost && (conditions & LosesFirstBuilding) != 0))
        {
            StartCountdown(DefeatStart);
        }
        if (enemy[TribeCounter.Buildings] > BeatenBuildings || enemy[TribeCounter.Population] > 0)
        {
            return;
        }
        if (world.Mission == VillageMission && world.Script.ObjectiveStage < VillageBeatenStage)
        {
            world.Script.ObjectiveStage = VillageBeatenStage;
        }
        else if (world.Mission == TwoPartMission)
        {
            if (world.EndCountdown == 0)
            {
                world.EndCountdown = VictoryStart;
            }
            else if (world.EndCountdown == SecondPartAt)
            {
                world.EndCountdown = 0;
                setup.SwitchToNextPart();
            }
        }
    }

    /// <summary>
    /// The end of a world frame: a running countdown moves on when the world runs normally
    /// (<paramref name="worldRunning"/>; not on the frames a scene step runs) and the result is
    /// announced when it reaches its end. Returns the result when it was announced.
    /// </summary>
    public bool? AdvanceCountdown(bool worldRunning)
    {
        if (world.EndCountdown <= 0 || !worldRunning)
        {
            return null;
        }
        world.EndCountdown++;
        if (world.EndCountdown != VictoryShown && world.EndCountdown != DefeatShown)
        {
            return null;
        }
        bool victory = world.EndCountdown == VictoryShown;
        AddScores();
        if (world.Mode == WorldMode.Match)
        {
            raise(new Match.MatchEnded(Match.MatchStandings.Of(world).Winners));
        }
        else
        {
            raise(new MissionEnded(victory));
        }
        return victory;
    }

    /// <summary>Every tribe's score grows by its mission statistics, each times its weight (in tenths).</summary>
    private void AddScores()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            foreach (ScoreWeight weight in world.Content.Rules.Missions.ScoreWeights)
            {
                tribe[TribeCounter.Score] += tribe[weight.Counter] * weight.WeightTenths / TenthsPerPoint;
            }
        }
    }

    private void StartCountdown(int start)
    {
        if (world.EndCountdown == 0)
        {
            world.EndCountdown = start;
        }
    }
}
