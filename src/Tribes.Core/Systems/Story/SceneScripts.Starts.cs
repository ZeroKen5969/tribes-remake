using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>The scenes' start scripts and the places they use.</summary>
internal sealed partial class SceneScripts
{
    /// <summary>The scout demonstration: the scout's forced task.</summary>
    private const int ScoutActor = 2, ScoutSubState = 3, ScoutIdleTicks = -1;

    /// <summary>Actors of the village scenes' ends.</summary>
    private const int DepartureActor = 5, RebuiltActor = 1;

    /// <summary>The raid: the player's adults and the child, then the raiders.</summary>
    private const int RaidAdults = 4, RaidChildOffset = 4, RaidChildFacing = 2, RaidLastRaiders = 2;

    /// <summary>The raiders' facings (second and third raider).</summary>
    private const int SecondRaiderFacing = 7, ThirdRaiderFacing = 5;

    /// <summary>The rebuilt village's men.</summary>
    private const int VillageMen = 5;

    /// <summary>The ending: the tribe, the enemy chief's guards and their poses.</summary>
    private const int EndingTribe = 7, EndingGuards = 2, EndingGuardAnimation = 42, EndingFacing = 0;

    /// <summary>The ending's stage props (kinds placed only by this scene) and their level.</summary>
    private const int LargeProp = 24, SmallProp = 25, PropLevel = 2;

    /// <summary>The objective stage the ending's world is set to.</summary>
    private const int EndingStage = 6;

    /// <summary>The mission whose map the ending is staged on, and the mission the ending belongs to.</summary>
    private const int VillageMission = 2, EndingMission = 3;

    /// <summary>The spawn zone of the new herd's animals.</summary>
    private const int HerdZone = 5;

    private static readonly GridPoint ScoutWalkCell = new(5, 20), ScoutTaskSite = new(5, 10);
    private static readonly GridPoint AssignmentCell = new(5, 11), DepartureCell = new(48, 15);
    private static readonly GridPoint RaidAdultsCell = new(46, 9), RaidChildCell = new(48, 9);
    private static readonly GridPoint[] RaiderCells = { new(44, 9), new(45, 11), new(48, 11) };
    private static readonly GridPoint LastRaidersCell = new(48, 9);
    private static readonly GridPoint[] RebuiltHuts = { new(52, 18), new(54, 16), new(55, 19), new(58, 19) };
    private static readonly GridPoint VillageMenCell = new(60, 12), RebuiltWalkCell = new(57, 17), RebuiltCell = new(65, 18);
    private static readonly GridPoint GatheringCell = new(52, 7), HuntCell = new(53, 5);
    private static readonly GridPoint HerdGatheringCell = new(57, 30);
    private static readonly GridPoint[] HerdCells = { new(54, 30), new(54, 31), new(55, 31), new(55, 32) };
    private static readonly (int Kind, GridPoint Cell)[] EndingProps =
        { (LargeProp, new(18, 67)), (SmallProp, new(19, 69)), (SmallProp, new(21, 71)) };
    private static readonly GridPoint EndingElderCell = new(26, 69), EndingWalkCell = new(23, 69);
    private static readonly GridPoint EndingGuardsCell = new(1, 69), EndingEnemyElderCell = new(21, 69);
    private static readonly GridPoint EndingCell = new(45, 69);

    private void StartScoutDemonstration()
    {
        staging.SendActors(ScoutWalkCell, ScoutActor);
        if (SceneActors.Unit(world, ScoutActor) is not Unit unit)
        {
            return;
        }
        unit.OrderMode = OrderModes.Forced;
        unit.Action = ScenePlayer.StagedAction;
        unit.TaskSiteX = ScoutTaskSite.X;
        unit.TaskSiteY = ScoutTaskSite.Y;
        unit.ActionSubState = ScoutSubState;
        unit.IdleTicks = ScoutIdleTicks;
    }

    private void StartVillageRaid()
    {
        staging.ClearWorld();
        world.SpawnGenderMode = AllFemale;
        groups.Spawn(RaidAdults, RaidAdultsCell, Player, BareHands);
        spawning.Spawn(Player, RaidChildCell, ChildAge, UnitTraits.None);
        Unit child = PlayerUnit(RaidChildOffset);
        child.Animation = AnimationNumbers.ChildStanding;
        child.Facing = RaidChildFacing;
        world.SpawnGenderMode = BalancedGenders;
        groups.Spawn(1, RaiderCells[0], Computer, Spear);
        groups.Spawn(1, RaiderCells[1], Computer, Spear);
        ComputerUnit(1).Facing = SecondRaiderFacing;
        groups.Spawn(1, RaiderCells[2], Computer, Spear);
        ComputerUnit(2).Facing = ThirdRaiderFacing;
        groups.Spawn(RaidLastRaiders, LastRaidersCell, Computer, Spear);
        world.Tribes[Player][TribeCounter.Chief] = 0;
    }

    private void StartVillageRebuilt()
    {
        staging.ClearWorld();
        foreach (GridPoint cell in RebuiltHuts)
        {
            placement.Place(new PlacementRequest(Player, BuildingKindNumbers.Hut, cell, HutLevel, Immediate: true));
        }
        world.SpawnGenderMode = AllMale;
        groups.Spawn(VillageMen, VillageMenCell, Player, Club);
        world.SpawnGenderMode = BalancedGenders;
        staging.SendActors(RebuiltWalkCell, RebuiltActor);
    }

    private void StartNewHerd()
    {
        staging.ClearEnemyAndAnimals();
        staging.GatherPlayerTribe(HerdGatheringCell);
        foreach (GridPoint cell in HerdCells)
        {
            objects.SpawnAnimal(MissionSetup.StartAnimalKind, cell, HerdZone);
        }
    }

    /// <summary>The ending is staged in the village of the mission before: its map is built anew.</summary>
    private void StartChapterEnding()
    {
        world.Mission = VillageMission;
        reset.Reset();
        world.Script.ObjectiveStage = EndingStage;
        starter.BuildMap();
        world.Mission = EndingMission;
        foreach ((int kind, GridPoint cell) in EndingProps)
        {
            placement.Place(new PlacementRequest(Player, kind, cell, PropLevel, Immediate: true));
        }
        world.Tribes[Player][TribeCounter.Males] = 0;
        spawning.Spawn(Player, EndingElderCell, ElderAge, UnitTraits.None);
        world.Tribes[Player][TribeCounter.Chief] = MissionStarter.FirstChief;
        groups.Spawn(EndingTribe, EndingElderCell, Player, Spear);
        staging.SendActors(EndingWalkCell, 1);
        groups.Spawn(EndingGuards, EndingGuardsCell, Computer, StoneAxe);
        spawning.Spawn(Computer, EndingEnemyElderCell, ElderAge, UnitTraits.None);
        Unit guard = ComputerUnit(2);
        guard.Facing = EndingFacing;
        guard.Animation = EndingGuardAnimation;
        ComputerUnit(0).Facing = EndingFacing;
    }

    private void EndHunt()
    {
        for (int i = 0; i < HuntingAnimals; i++)
        {
            world.Animal(i).Target = world.Script.FocusUnit;
        }
        staging.SendActors(HuntCell, 0);
    }
}
