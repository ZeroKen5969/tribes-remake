using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// What particular story scenes do to the world when they start and when they end: the tutorial's
/// map opening up, the raid on the village and its rebuilding, the gathering, hunt and ending of
/// the third chapter. The places and units are the story's own.
/// </summary>
internal sealed partial class SceneScripts
{
    /// <summary>The tutorial's scout demonstration: actor 2 walks out and back.</summary>
    public const int ScoutDemonstration = 3;

    /// <summary>The tutorial's last scene: the whole map opens and the next mission's objects arrive.</summary>
    public const int TutorialMapOpens = 26;

    /// <summary>The second mission's first story scene ending: the tribe stops working on its own.</summary>
    public const int FirstAssignment = 28;

    /// <summary>The village mission's first scene ending: actor 5 walks off.</summary>
    public const int VillageDeparture = 34;

    /// <summary>The raid on the village.</summary>
    public const int VillageRaid = 37;

    /// <summary>The village rebuilt; when it ends the mission is won.</summary>
    public const int VillageRebuilt = 38;

    /// <summary>The third chapter's gathering of the tribe.</summary>
    public const int TribeGathering = 43;

    /// <summary>The third chapter's hunt: when it ends the animals go for the focus unit.</summary>
    public const int Hunt = 44;

    /// <summary>The third chapter's clearing: the enemy and the animals are gone, a new herd comes.</summary>
    public const int NewHerd = 45;

    /// <summary>The third chapter's ending, played on the village map; when it ends the mission is won.</summary>
    public const int ChapterEnding = 48;

    /// <summary>Weapon kinds of the units scenes bring in.</summary>
    private const int Club = 0, Spear = 2, StoneAxe = 3, BareHands = UnitSpawning.BareHands;

    /// <summary>Gender modes of new units.</summary>
    private const int AllMale = 0, AllFemale = 1, BalancedGenders = 2;

    /// <summary>Ages of the children and elders scenes bring in.</summary>
    private const int ChildAge = 3, ElderAge = 60;

    /// <summary>The victory countdown's first value.</summary>
    private const int VictoryCountdown = 1;

    /// <summary>The hut kind and the level of the huts a scene builds.</summary>
    private const int HutLevel = 2;

    /// <summary>The bit of the player's tribe in <see cref="ScriptState.BehaviourMask"/>.</summary>
    private const int PlayerWorksFreely = 1;

    /// <summary>The hint whose channel a scene frees (the hint about something out of sight).</summary>
    private const int OutOfSightHint = 4;

    /// <summary>Animals that go for the focus unit after the hunt.</summary>
    private const int HuntingAnimals = 10;

    private readonly GameWorld world;
    private readonly SceneStaging staging;
    private readonly Council council;
    private readonly GroupSpawner groups;
    private readonly UnitSpawning spawning;
    private readonly BuildingPlacement placement;
    private readonly WildlifeAndNodes objects;
    private readonly MissionReset reset;
    private readonly MissionStarter starter;
    private readonly MissionSetup setup;

    /// <summary>Plays scene scripts in <paramref name="world"/>.</summary>
    public SceneScripts(GameWorld world, SceneStaging staging, Council council, GroupSpawner groups, UnitSpawning spawning,
        BuildingPlacement placement, WildlifeAndNodes objects, MissionReset reset, MissionStarter starter, MissionSetup setup)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.staging = staging ?? throw new ArgumentNullException(nameof(staging));
        this.council = council ?? throw new ArgumentNullException(nameof(council));
        this.groups = groups ?? throw new ArgumentNullException(nameof(groups));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
        this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
        this.starter = starter ?? throw new ArgumentNullException(nameof(starter));
        this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
    }

    /// <summary>What scene <paramref name="dialogue"/> does when it starts (after its actors are cast).</summary>
    public void OnStart(int dialogue)
    {
        switch (dialogue)
        {
            case ScoutDemonstration:
                StartScoutDemonstration();
                break;
            case VillageRaid:
                StartVillageRaid();
                break;
            case VillageRebuilt:
                StartVillageRebuilt();
                break;
            case TribeGathering:
                staging.GatherPlayerTribe(GatheringCell);
                break;
            case NewHerd:
                StartNewHerd();
                break;
            case ChapterEnding:
                StartChapterEnding();
                break;
        }
    }

    /// <summary>
    /// What scene <paramref name="dialogue"/> (of layout kind <paramref name="sceneKind"/>) does
    /// when it ends, before its actors are released.
    /// </summary>
    public void OnEnd(int dialogue, int sceneKind)
    {
        if (dialogue == TutorialMapOpens)
        {
            world.Script.Active = false;
            setup.SwitchToNextPart();
            world.Script.Active = true;
        }
        if (sceneKind == ScenePlayer.CouncilKind)
        {
            council.Dismiss();
            return;
        }
        switch (dialogue)
        {
            case FirstAssignment:
                staging.SendActors(AssignmentCell, 0);
                world.Script.BehaviourMask &= ~PlayerWorksFreely;
                world.Hints.SetCooldown(world.Content.Rules.Story.Hints[OutOfSightHint].Channel, 0);
                break;
            case VillageDeparture:
                staging.SendActors(DepartureCell, DepartureActor);
                world.Script.BehaviourMask = 0;
                break;
            case VillageRebuilt:
                staging.SendActors(RebuiltCell, RebuiltActor);
                world.EndCountdown = VictoryCountdown;
                break;
            case Hunt:
                EndHunt();
                break;
            case ChapterEnding:
                world.EndCountdown = VictoryCountdown;
                staging.SendActors(EndingCell, SceneStaging.FirstExtra);
                break;
        }
    }

    private Unit PlayerUnit(int offset) => world.UnitsOf(TribeRoles.Player(world))[offset];

    private Unit ComputerUnit(int offset) => world.UnitsOf(TribeRoles.Computer(world))[offset];

    private int Player => TribeRoles.Player(world);

    private int Computer => TribeRoles.Computer(world);
}
