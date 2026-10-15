using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>Events some story steps bring before their scene: the third chapter's finale and the enemy's counter-attack.</summary>
internal sealed class ObjectiveEvents
{
    /// <summary>Weapons of the units the events bring in.</summary>
    private const int StoneAxe = 3, BareHands = UnitSpawning.BareHands;

    /// <summary>The finale: the women who join, the units cast and the two children born.</summary>
    private const int JoiningWomen = 3, CastUnits = 8, ChildAge = 2;

    /// <summary>Actor slots the finale casts the tribe's men and women into.</summary>
    private const int ManSlot = 4, FirstWomanSlot = 2, SecondWomanSlot = 3;

    /// <summary>The finale's units face south-east (men) or south-west (women): facing = base + gender * step.</summary>
    private const int FacingBase = 3, FacingPerGender = 4;

    /// <summary>Births the finale takes back from the statistics (its own units do not count).</summary>
    private const int UncountedBirths = 5;

    /// <summary>Gender modes of new units.</summary>
    private const int AllFemale = 1, BalancedGenders = 2;

    /// <summary>The counter-attack: two groups of raiders.</summary>
    private const int RaidersPerGroup = 8;

    /// <summary>The target cell code the raiders forget.</summary>
    private const int NoTarget = -1;

    private static readonly GridPoint FinaleGathering = new(58, 30), WomenCell = new(57, 30);
    private static readonly GridPoint[] ChildCells = { new(56, 30), new(55, 31) };
    private static readonly GridPoint[] RaiderCells = { new(21, 72), new(21, 75) };

    private readonly GameWorld world;
    private readonly SceneStaging staging;
    private readonly GroupSpawner groups;
    private readonly UnitSpawning spawning;

    /// <summary>Plays the events in <paramref name="world"/>.</summary>
    public ObjectiveEvents(GameWorld world, SceneStaging staging, GroupSpawner groups, UnitSpawning spawning)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.staging = staging ?? throw new ArgumentNullException(nameof(staging));
        this.groups = groups ?? throw new ArgumentNullException(nameof(groups));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
    }

    /// <summary>
    /// The third chapter's finale: the tribe gathers, women join it, the first units are cast (men
    /// and women in their slots) and turn, and two children are born.
    /// </summary>
    public void ThirdChapterFinale()
    {
        int player = TribeRoles.Player(world);
        staging.GatherPlayerTribe(FinaleGathering);
        world.SpawnGenderMode = AllFemale;
        groups.Spawn(JoiningWomen, WomenCell, player, BareHands);
        world.SpawnGenderMode = BalancedGenders;
        ScriptState script = world.Script;
        ListView<Unit> units = world.UnitsOf(player);
        for (int i = 0; i < CastUnits && i < units.Count; i++)
        {
            Unit unit = units[i];
            int id = unit.Id;
            if (unit.HitPoints <= 0)
            {
                continue;
            }
            if (unit.Gender == 0)
            {
                CastIfFree(ManSlot, id);
            }
            else if (script.Actor(FirstWomanSlot) < 1)
            {
                script.SetActor(FirstWomanSlot, id);
            }
            else
            {
                CastIfFree(SecondWomanSlot, id);
            }
            unit.Facing = FacingBase + (unit.Gender * FacingPerGender);
        }
        foreach (GridPoint cell in ChildCells)
        {
            spawning.Spawn(player, cell, ChildAge, UnitTraits.None);
        }
        world.Tribes[player][TribeCounter.Born] -= UncountedBirths;
    }

    /// <summary>The enemy's counter-attack: raiders appear and every enemy unit marches on the player's first building.</summary>
    public void EnemyCounterAttack()
    {
        int computer = TribeRoles.Computer(world);
        foreach (GridPoint cell in RaiderCells)
        {
            groups.Spawn(RaidersPerGroup, cell, computer, StoneAxe);
        }
        Building target = world.Building(world.FirstBuildingSlot(TribeRoles.Player(world)));
        foreach (Unit unit in world.UnitsOf(computer))
        {
            if (unit.HitPoints > 0)
            {
                unit.OrderMode = OrderModes.Walk;
                unit.TargetCellCode = NoTarget;
                unit.DestinationX = target.X;
                unit.DestinationY = target.Y;
            }
        }
    }

    private void CastIfFree(int slot, int unit)
    {
        if (world.Script.Actor(slot) < 1)
        {
            world.Script.SetActor(slot, unit);
        }
    }
}
