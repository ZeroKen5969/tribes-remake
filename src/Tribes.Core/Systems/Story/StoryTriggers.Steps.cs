using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>The story steps with timers and counters, and the ambush mission's scenes.</summary>
internal sealed partial class StoryTriggers
{
    /// <summary>The ambush mission: its scenes come by time and by what the scouts see.</summary>
    private const int AmbushMission = 4;

    /// <summary>The ambush mission's story step whose area brings the council together.</summary>
    private const int AmbushCouncilStage = 1;

    /// <summary>The ambush mission's sighting scene, and its two timed council scenes.</summary>
    private const int SightingScene = 51, FirstCouncilScene = 53, SecondCouncilScene = 54;

    /// <summary>The flag of the computer's flags that marks the sighting scene as played.</summary>
    private const int SightingPlayed = 0x40;

    /// <summary>The timed council scenes come at frame CouncilBase + stage * CouncilInterval.</summary>
    private const int CouncilBase = 3000, CouncilInterval = 7000;

    /// <summary>The story stages during which the timed council scenes come (exclusive bounds).</summary>
    private const int FirstCouncilStage = 1, LastCouncilStage = 2;

    /// <summary>The story stage of the third chapter whose adults' area step waits for quiet.</summary>
    private const int QuietAreaStage = 2;

    /// <summary>Actor slots stopped when an area step of the third chapter starts.</summary>
    private const int StoppedActors = 5;

    /// <summary>Both tribes' "picks work by itself" bits.</summary>
    private const int BothTribesWorkFreely = 3;

    /// <summary>The cell the scouts of the ambush mission must see.</summary>
    private static readonly GridPoint SightingCell = new(8, 67);

    /// <summary>
    /// The ambush mission's own scenes: once the player sees the watched cell, and the two council
    /// scenes that come at fixed times. Returns whether one started.
    /// </summary>
    public bool CheckAmbushScenes()
    {
        if (!ChiefIsActive())
        {
            return false;
        }
        ScriptState script = world.Script;
        bool quiet = world.FramesSinceCombat >= QuietFrames;
        if (world.Fog(Player).IsVisible(SightingCell) && (world.Ai.Flags & SightingPlayed) == 0 && quiet)
        {
            story.CastActors(Chief, 0, world.FirstUnitSlot(Player));
            story.StartScene(SightingScene);
            world.Ai.Flags |= SightingPlayed;
            return true;
        }
        int stage = script.ObjectiveStage;
        if (stage >= FirstCouncilStage && stage <= LastCouncilStage && world.Frame >= CouncilBase + (stage * CouncilInterval) && quiet)
        {
            ConveneCouncil(false);
            story.StartScene(stage == FirstCouncilStage ? FirstCouncilScene : SecondCouncilScene);
            script.ObjectiveStage += StoryProgress.ScenePlaying;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The current story step's trigger: when it fires its scene starts (with a council, the
    /// mission's actors, or the step's own events). Returns whether a scene started.
    /// </summary>
    public bool CheckObjective()
    {
        ScriptState script = world.Script;
        if (story.NextStep is not StoryStep step || (world.Mission != ChieflessMission && !ChiefIsActive()))
        {
            return false;
        }
        int trigger = step.Trigger;
        if (world.Tribes[TribeRoles.Computer(world)][TribeCounter.Population] <= 0 && trigger == EnemyGoneTrigger
            && script.ObjectiveTimer == ScenePlayer.TimerNotStarted)
        {
            script.ObjectiveTimer = world.Frame;
        }
        if (trigger < EnemyGoneTrigger || trigger > LastTimedTrigger)
        {
            return false;
        }
        if (trigger == AdultInAreaTrigger)
        {
            return CheckAdultsInArea();
        }
        if (!Fires(trigger))
        {
            return false;
        }
        if (world.Mission < FirstMissionsCount)
        {
            script.BehaviourMask = trigger == CounterTrigger ? BothTribesWorkFreely : 0;
        }
        if (trigger == FirstBuildingLostTrigger)
        {
            events.ThirdChapterFinale();
        }
        else if (trigger == FewBuildingsTrigger)
        {
            events.EnemyCounterAttack();
        }
        else if (world.Mission == SoloScenesMission)
        {
            story.CastActors(0, 0, world.FirstUnitSlot(Player));
        }
        else
        {
            ConveneCouncil(world.Mission == VillageMission);
        }
        story.Reach(step);
        return true;
    }

    private bool Fires(int trigger)
    {
        ScriptState script = world.Script;
        int computer = TribeRoles.Computer(world);
        return trigger switch
        {
            EnemyGoneTrigger => world.Frame - script.ObjectiveTimer > EnemyGoneDelay,
            CounterTrigger => script.ObjectiveTimer > CounterThreshold,
            TimerTrigger => world.Frame - script.ObjectiveTimer > (world.Mission < FirstMissionsCount ? EarlyTimerDelay : TimerDelay),
            FewBuildingsTrigger => world.Tribes[computer][TribeCounter.Buildings] < FewBuildings,
            FirstBuildingLostTrigger => world.Building(world.FirstBuildingSlot(computer)).HitPoints == 0,
            _ => false,
        };
    }

    /// <summary>The step comes when any adult of the player standing on a cell centre is in its area.</summary>
    private bool CheckAdultsInArea()
    {
        if (world.Mission == ChieflessMission && world.Script.ObjectiveStage == QuietAreaStage
            && world.FramesSinceCombat < QuietFrames)
        {
            return false;
        }
        ListView<Unit> units = world.UnitsOf(Player);
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit.HitPoints <= 0 || unit.Age < Entities.UnitAges.Adult || unit.OffsetX != 0 || unit.OffsetY != 0)
            {
                continue;
            }
            AreaReached(unit.Id, 0, new GridPoint(unit.X, unit.Y));
            if (world.Script.ObjectiveStage < StoryProgress.ScenePlaying)
            {
                continue;
            }
            if (world.Mission == ChieflessMission)
            {
                for (int slot = 0; slot < StoppedActors; slot++)
                {
                    if (SceneActors.Unit(world, slot) is Unit actor)
                    {
                        stopActor(actor);
                    }
                }
            }
            return true;
        }
        return false;
    }
}
