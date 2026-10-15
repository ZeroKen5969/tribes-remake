using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;

namespace Tribes.Core.Simulation;

/// <summary>The scenario systems: mission start, story scenes and objectives, and the computer player.</summary>
public sealed partial class RuleSystems
{
    private MissionReset? missionReset;
    private MissionSetup? missionSetup;
    private MissionStarter? missionStarter;
    private StockArming? stockArming;
    private GroupSpawner? groupSpawner;
    private Council? council;
    private SceneStaging? sceneStaging;
    private SceneScripts? sceneScripts;
    private ScenePlayer? scenes;
    private AlarmCalls? alarmCalls;
    private ObjectiveEvents? objectiveEvents;
    private StoryTriggers? storyTriggers;
    private MissionOutcome? missionOutcome;
    private Tutorial? tutorial;
    private StoryUpdate? storyUpdate;
    private FrameStart? frameStart;
    private ComputerPlans? computerPlans;
    private ComputerTargets? computerTargets;
    private ComputerBuilder? computerBuilder;
    private ComputerOrders? computerOrders;
    private AttackWaves? attackWaves;
    private ComputerTroops? computerTroops;
    private ComputerPlayer? computerPlayer;
    private HutBirths? hutBirths;
    private Systems.Match.MatchUpdate? matchUpdate;

    /// <summary>The story step shown, or null when no story scene is playing.</summary>
    public SceneStep? CurrentSceneStep => Scenes.Current;

    /// <summary>Whether the player may go on to the next step of the scene now.</summary>
    public bool CanContinueScene => Scenes.CanContinue;

    /// <summary>
    /// One display frame of the scene step shown, once its slide-in is over: when it returns
    /// <see cref="SceneFrame.AdvanceWorld"/> the host runs one world frame as a staged frame (the
    /// world does not run otherwise while a scene plays).
    /// </summary>
    public SceneFrame AdvanceSceneStep() => Scenes.AdvanceStep();

    /// <summary>The player taps on in a scene (only when <see cref="CanContinueScene"/>): the next step, or the scene's end.</summary>
    public void ContinueScene() => Scenes.Continue();

    /// <summary>Clearing the world for a mission.</summary>
    internal MissionReset MissionReset => missionReset ??= new MissionReset(World, UnlockLevels);

    /// <summary>A mission's starting objects and the switch to a mission's second part.</summary>
    internal MissionSetup MissionSetup =>
        missionSetup ??= new MissionSetup(World, BuildingPlacement, UnitSpawning, WildlifeAndNodes);

    /// <summary>Starting a mission on the world.</summary>
    internal MissionStarter MissionStarter => missionStarter ??= new MissionStarter(World, MissionReset, MissionSetup, Sight);

    /// <summary>Units taking weapons from the stock.</summary>
    internal StockArming StockArming => stockArming ??= new StockArming(World);

    /// <summary>Groups of adults appearing at a place.</summary>
    internal GroupSpawner GroupSpawner => groupSpawner ??= new GroupSpawner(World, UnitSpawning, SpiralSearch, StockArming);

    /// <summary>The council scene's seating.</summary>
    internal Council Council => council ??= new Council(World);

    /// <summary>Moves scenes make: sending actors, gathering the tribe, clearing the world.</summary>
    internal SceneStaging SceneStaging =>
        sceneStaging ??= new SceneStaging(World, UnitLifecycle, BuildingLifecycle, WildlifeAndNodes, SpiralSearch, Sight, UnitActions);

    /// <summary>What particular scenes do when they start and end.</summary>
    internal SceneScripts SceneScripts => sceneScripts ??= new SceneScripts(World, SceneStaging, Council, GroupSpawner,
        UnitSpawning, BuildingPlacement, WildlifeAndNodes, MissionReset, MissionStarter, MissionSetup);

    /// <summary>The story scene player.</summary>
    internal ScenePlayer Scenes => scenes ??= new ScenePlayer(World, Sight, SceneScripts, Raise);

    /// <summary>The tribes' alarm calls.</summary>
    internal AlarmCalls AlarmCalls => alarmCalls ??= new AlarmCalls(World);

    /// <summary>Events story steps bring.</summary>
    internal ObjectiveEvents ObjectiveEvents =>
        objectiveEvents ??= new ObjectiveEvents(World, SceneStaging, GroupSpawner, UnitSpawning);

    /// <summary>The chief's death and the mission's story steps.</summary>
    internal StoryTriggers StoryTriggers => storyTriggers ??=
        new StoryTriggers(World, StoryProgress, Council, Hints, SpiralSearch, ObjectiveEvents, SceneStaging.StopAndIdle);

    /// <summary>Victory, defeat and the end countdown.</summary>
    internal MissionOutcome MissionOutcome => missionOutcome ??= new MissionOutcome(World, MissionSetup, Raise);

    /// <summary>The tutorial of the first missions.</summary>
    internal Tutorial Tutorial => tutorial ??= new Tutorial(World, StoryProgress);

    /// <summary>The story's part of each frame.</summary>
    internal StoryUpdate StoryUpdate =>
        storyUpdate ??= new StoryUpdate(World, AlarmCalls, StoryTriggers, MissionOutcome, Tutorial, UnlockLevels, StoryProgress, MatchUpdate);

    /// <summary>A match's part of each frame (chiefs, tribes going out, the decision).</summary>
    internal Systems.Match.MatchUpdate MatchUpdate => matchUpdate ??= new Systems.Match.MatchUpdate(World, Hints, Raise);

    /// <summary>The start of each frame.</summary>
    internal FrameStart FrameStart => frameStart ??= new FrameStart(World, Sight);

    /// <summary>The plan each computer tribe follows.</summary>
    internal ComputerPlans ComputerPlans => computerPlans ??= new ComputerPlans(World);

    /// <summary>The enemies computer tribes go for in a match.</summary>
    internal ComputerTargets ComputerTargets => computerTargets ??= new ComputerTargets(World, ComputerPlans);

    /// <summary>The computer's building plan.</summary>
    internal ComputerBuilder ComputerBuilder =>
        computerBuilder ??= new ComputerBuilder(World, ComputerPlans, BuildingLifecycle, BuildingPlacement);

    /// <summary>The computer's armoury orders and spells.</summary>
    internal ComputerOrders ComputerOrders => computerOrders ??= new ComputerOrders(World, ComputerPlans, ComputerTargets, SpiralSearch);

    /// <summary>The computer's attack waves.</summary>
    internal AttackWaves AttackWaves =>
        attackWaves ??= new AttackWaves(World, ComputerPlans, ComputerTargets, GroupSpawner, TargetDistances);

    /// <summary>The computer's units.</summary>
    internal ComputerTroops ComputerTroops => computerTroops ??=
        new ComputerTroops(World, ComputerPlans, UnitActions, UnitOrders, TargetDistances, AttackWaves, NearestBuildings.HeadFor);

    /// <summary>The computer player's turn.</summary>
    internal ComputerPlayer ComputerPlayer =>
        computerPlayer ??= new ComputerPlayer(World, ComputerPlans, ComputerBuilder, ComputerTroops, ComputerOrders);

    /// <summary>Children born in huts.</summary>
    internal HutBirths HutBirths => hutBirths ??= new HutBirths(World, UnitSpawning, Occupancy);

    /// <summary>The player opened the game menu (a tutorial step may wait for that).</summary>
    public void OpenedGameMenu() => Tutorial.PlayerOpenedMenu();

    /// <summary>Plays a mission's opening dialogue on the chapter screen.</summary>
    internal void PlayOpening(int dialogue) => Scenes.Start(dialogue, chapterScreen: true);
}
