using System;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// The sound effects by name (as data/client/sounds.json lists them) and which happenings of a
/// match play which. Only what the player may know sounds: the notices of the player's own tribe,
/// and blows and throws in sight on the screen.
/// </summary>
public static class SoundCues
{
    /// <summary>A button of the menus or the panels.</summary>
    public const string Click = "click";

    /// <summary>Villagers sent somewhere on the map or told to attack.</summary>
    public const string Order = "order";

    /// <summary>A building or trap laid out.</summary>
    public const string Place = "place";

    /// <summary>A blow landed.</summary>
    public const string Hit = "hit";

    /// <summary>A spear, stone or axe thrown.</summary>
    public const string Throw = "throw";

    /// <summary>A child of the tribe was born.</summary>
    public const string Birth = "birth";

    /// <summary>One of the tribe died.</summary>
    public const string Death = "death";

    /// <summary>A building of the tribe was finished.</summary>
    public const string Built = "built";

    /// <summary>An enemy was seen.</summary>
    public const string Enemy = "enemy";

    /// <summary>The tribe is under attack.</summary>
    public const string Attack = "attack";

    /// <summary>A building of the tribe was destroyed.</summary>
    public const string Destroyed = "destroyed";

    /// <summary>Something cannot be done (resources, limits).</summary>
    public const string Denied = "denied";

    /// <summary>Another message for the tribe.</summary>
    public const string Notice = "notice";

    /// <summary>A hint comes up.</summary>
    public const string Hint = "hint";

    /// <summary>The match or mission is won.</summary>
    public const string Victory = "victory";

    /// <summary>The match or mission is lost.</summary>
    public const string Defeat = "defeat";

    /// <summary>The original's notice kinds (its message numbers from 41 on).</summary>
    private const int ChooseChief = 0, BornMale = 1, BornFemale = 2, DiedMale = 3, DiedFemale = 4, BuildingFinished = 5,
        EnemySeen = 6, UnderAttack = 7, BuildingLost = 8, NoResources = 9, BuildingLimit = 10, VillagerLimit = 11,
        FirstWorkerNeeded = 12, LastWorkerNeeded = 15;

    /// <summary>The sound of a notice for the player's tribe (kind as <c>NoticeEvent.Kind</c>), or null.</summary>
    public static string? ForNotice(int kind) => kind switch
    {
        BornMale or BornFemale => Birth,
        DiedMale or DiedFemale => Death,
        BuildingFinished => Built,
        EnemySeen => Enemy,
        UnderAttack => Attack,
        BuildingLost => Destroyed,
        NoResources or BuildingLimit or VillagerLimit => Denied,
        ChooseChief or (>= FirstWorkerNeeded and <= LastWorkerNeeded) => Notice,
        _ => null,
    };

    /// <summary>The cell categories of <see cref="BlowLanded.Category"/> whose blows sound: a unit, a building, an animal.</summary>
    private const int UnitCategory = 1, BuildingCategory = 2, AnimalCategory = 5;

    /// <summary>
    /// The sound of a happening heard only where it is seen (a blow on a unit, building or animal; a
    /// throw) and the cell it happens on, or null for one that has none.
    /// </summary>
    public static (string Sound, GridPoint Cell)? Seen(GameWorld world, GameEvent happening)
    {
        ArgumentNullException.ThrowIfNull(world);
        switch (happening)
        {
            case BlowLanded { Category: UnitCategory } blow when blow.Slot >= 1 && blow.Slot <= world.UnitSlots:
                return (Hit, new GridPoint(world.Unit(blow.Slot).X, world.Unit(blow.Slot).Y));
            case BlowLanded { Category: BuildingCategory } blow when blow.Slot >= 1 && blow.Slot <= world.BuildingSlots:
                return (Hit, new GridPoint(world.Building(blow.Slot).X, world.Building(blow.Slot).Y));
            case BlowLanded { Category: AnimalCategory } blow when blow.Slot >= 0 && blow.Slot < world.AnimalSlots:
                return (Hit, new GridPoint(world.Animal(blow.Slot).X, world.Animal(blow.Slot).Y));
            case ProjectileThrown thrown when thrown.Unit >= 1 && thrown.Unit <= world.UnitSlots:
                return (Throw, new GridPoint(world.Unit(thrown.Unit).X, world.Unit(thrown.Unit).Y));
            default:
                return null;
        }
    }

    /// <summary>The sound at the end of a match or mission.</summary>
    public static string ForResult(bool victory) => victory ? Victory : Defeat;
}
