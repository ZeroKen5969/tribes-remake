using System.Collections.Generic;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Commands;

/// <summary>
/// The choices the game's menus offer, which bound what a player may order: the build menu, an
/// armoury's products, a sanctuary's spells and a store's stock. The lists are the original
/// game's menu tables.
/// </summary>
public static class OrderMenus
{
    /// <summary>Most items an armoury can be asked to make of one weapon.</summary>
    public const int MaxArmouryCount = 9;

    /// <summary>The armoury products from this one on are traps (product p is trap kind 20 + p - 6).</summary>
    public const int FirstTrapProduct = 6;

    /// <summary>The spell cast on a place instead of a unit (the disease).</summary>
    public const int PlaceSpell = 3;

    /// <summary>A sanctuary offers this many more spells per level.</summary>
    private const int SpellsPerLevel = 2;

    /// <summary>A store offers a stone for every this many stones in stock.</summary>
    private const int StonesPerStoneWeapon = 5;

    /// <summary>Building kinds without a name elsewhere in the rules.</summary>
    private const int Gym = 1, Range = 5, PiaCentre = 9, Nursery = 10;

    /// <summary>Weapon products.</summary>
    private const int Club = 0, Spear = 2, StoneAxe = 3, BoneMace = 4, Tomahawk = 5;

    /// <summary>Trap products.</summary>
    private const int NetTrap = 6, SpringTrap = 7, PitTrap = 8;

    /// <summary>Weapons a store hands out (0 club ... 5 tomahawk).</summary>
    private const int StoreWeapons = 6;

    /// <summary>From this mission on the build menu offers every kind.</summary>
    private const int FullMenuMission = 2;

    /// <summary>Before this mission the menu grows with the story: 3 kinds, then 6 once this scene was reached.</summary>
    private const int LongerMenuScene = 18;

    private const int ShortMenu = 3, LongerMenu = 6;

    /// <summary>The build menu, in menu order.</summary>
    private static readonly int[] BuildMenu =
    {
        BuildingKindNumbers.Hut, BuildingKindNumbers.Store, BuildingKindNumbers.Kitchen, BuildingKindNumbers.Armoury,
        Gym, Range, BuildingKindNumbers.Sanctuary, PiaCentre, BuildingKindNumbers.Disco, Nursery, BuildingKindNumbers.Tower,
    };

    /// <summary>The products an armoury makes, by level 1..3.</summary>
    private static readonly int[][] ArmouryProducts =
    {
        new[] { Club, Spear, NetTrap },
        new[] { Club, Spear, StoneAxe, BoneMace, NetTrap, SpringTrap },
        new[] { Club, Spear, StoneAxe, BoneMace, Tomahawk, NetTrap, SpringTrap, PitTrap },
    };

    /// <summary>The building kinds the build menu offers now, in menu order.</summary>
    public static IReadOnlyList<int> BuildableKinds(GameWorld world)
    {
        System.ArgumentNullException.ThrowIfNull(world);
        int length = world.Mission >= FullMenuMission ? BuildMenu.Length
            : world.Script.SceneId < LongerMenuScene ? ShortMenu : LongerMenu;
        return world.Endless != null ? [.. BuildMenu[..length], Systems.Endless.AllianceBuilding.Kind] : BuildMenu[..length];
    }

    /// <summary>The products (weapons, then traps) an armoury of <paramref name="level"/> makes; none for other levels.</summary>
    public static IReadOnlyList<int> ArmouryMakes(int level) =>
        level >= 1 && level <= ArmouryProducts.Length ? ArmouryProducts[level - 1] : System.Array.Empty<int>();

    /// <summary>How many spells (0, 1, ...) a sanctuary of <paramref name="level"/> offers.</summary>
    public static int SanctuarySpells(GameWorld world, int level)
    {
        System.ArgumentNullException.ThrowIfNull(world);
        return System.Math.Clamp(level * SpellsPerLevel, 0, world.Content.Rules.Armoury.SpellActions.Count);
    }

    /// <summary>Whether a store of <paramref name="tribe"/> has <paramref name="weapon"/> to hand out.</summary>
    public static bool InStock(GameWorld world, int tribe, int weapon)
    {
        System.ArgumentNullException.ThrowIfNull(world);
        if (weapon < 0 || weapon >= StoreWeapons)
        {
            return false;
        }
        TribeState stock = world.Tribes[tribe];
        return weapon == WeaponNumbers.Stone
            ? stock[TribeCounter.Stones] / StonesPerStoneWeapon > 0
            : stock.Counter((int)TribeCounter.Clubs + weapon) > 0;
    }
}
