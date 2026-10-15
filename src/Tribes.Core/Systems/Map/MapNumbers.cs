namespace Tribes.Core.Systems.Map;

/// <summary>
/// Cell categories (rows of <c>GameRules.Targets.CellCategories</c>) and the two extra categories
/// an order check uses to pick a building's other actions.
/// </summary>
internal static class CellCategories
{
    /// <summary>An empty cell (or one the viewing tribe has not explored).</summary>
    public const int Empty = 0;

    /// <summary>A unit.</summary>
    public const int Unit = 1;

    /// <summary>A building.</summary>
    public const int Building = 2;

    /// <summary>A resource node, carcasses included.</summary>
    public const int ResourceNode = 3;

    /// <summary>A tree or rock cell.</summary>
    public const int TerrainResource = 4;

    /// <summary>An animal.</summary>
    public const int Animal = 5;

    /// <summary>Anything else: blocked cells, traps, destroyed targets.</summary>
    public const int Other = 6;

    /// <summary>A building another unit already runs (its occupied-use action applies).</summary>
    public const int OccupiedBuilding = 7;

    /// <summary>A building being upgraded or repaired (its busy action applies).</summary>
    public const int BusyBuilding = 8;
}

/// <summary>Resource node kinds the world rules refer to by name.</summary>
internal static class NodeKinds
{
    /// <summary>A fishing spot; only units with a spear can fish there.</summary>
    public const int FishingSpot = 5;

    /// <summary>An animal's carcass.</summary>
    public const int Carcass = 6;

    /// <summary>The bones left of a carcass.</summary>
    public const int Bones = 7;

    /// <summary>Whether <paramref name="kind"/> is left by a dead animal (a carcass or its bones).</summary>
    public static bool IsRemains(int kind) => kind == Carcass || kind == Bones;
}

