namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// Numbers of the rules the match screen works with. The rules keep their own named copies
/// internal; these are the same values (building kinds, ages, unit sub-states) as the content
/// tables and the order handlers use them.
/// </summary>
public static class RuleNumbers
{
    /// <summary>Building kinds 0..11 (the build menu and the buildings standing on the map).</summary>
    public const int Armoury = 0, Gym = 1, Hut = 2, ChiefsHut = 3, Kitchen = 4, Range = 5, Store = 6, Disco = 7,
        Sanctuary = 8, PiaCentre = 9, Nursery = 10, Tower = 11;

    /// <summary>Kinds below this one are buildings that can be planned, upgraded and repaired.</summary>
    public const int PlannedKinds = 12;

    /// <summary>Construction site kinds.</summary>
    public const int FirstSite = 12, LastSite = 19;

    /// <summary>Trap kinds (net, spring, pit).</summary>
    public const int FirstTrap = 20, LastTrap = 22;

    /// <summary>Units of this age or older are adults (spells and the chief need one).</summary>
    public const int AdultAge = 12;

    /// <summary>Below this happiness a unit refuses orders unless the tribe is called to arms.</summary>
    public const int WillingHappiness = 10;

    /// <summary><see cref="Tribes.Core.World.Unit.ActionSubState"/> of a unit inside a building.</summary>
    public const int InsideSubState = -3;

    /// <summary>Weapon kinds a store hands out (0 club ... 5 tomahawk) and the bare hands.</summary>
    public const int Club = 0, Stone = 1, Spear = 2, StoneAxe = 3, BoneMace = 4, Tomahawk = 5, BareHands = 9;

    /// <summary>Weapons a store hands out.</summary>
    public const int StoreWeapons = 6;

    /// <summary>Whether <paramref name="kind"/> is a construction site.</summary>
    public static bool IsSite(int kind) => kind >= FirstSite && kind <= LastSite;

    /// <summary>Whether <paramref name="kind"/> is a trap.</summary>
    public static bool IsTrap(int kind) => kind >= FirstTrap && kind <= LastTrap;

    /// <summary>Whether <paramref name="kind"/> is a building that can be planned, upgraded and repaired.</summary>
    public static bool IsPlanned(int kind) => kind >= 0 && kind < PlannedKinds;

    /// <summary>
    /// The kind a building's panel shows: a construction site shows the building it becomes (its
    /// <paramref name="aux"/>, one of the planned kinds or the alliance building); anything else its own kind.
    /// </summary>
    public static int ShownKind(int kind, int aux) =>
        IsSite(kind) && (IsPlanned(aux) || Tribes.Core.Systems.Endless.AllianceBuilding.Is(aux)) ? aux : kind;
}
