using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// How the original's drawing sorts traps, seen by a viewer: the original's player (tribe 0) sees
/// its own traps and the ones its scouts found, never the computer's, and marks the traps it
/// ordered. With more tribes "own" means a tribe of the viewer's sight (<see cref="ViewerSight.Includes"/>:
/// the viewer and its allies, or for a spectator every human player): the traps they set or their
/// scouts found; ordered traps show for the tribes whose marks the viewer sees. For viewer 0 of a
/// two-tribe world every code sorts exactly as in the original.
/// </summary>
internal static class CellClasses
{
    /// <summary>A trap the viewer knows: set by one of its tribes, or found by their scouts.</summary>
    public const int OwnTrap = 0;

    /// <summary>Another tribe's set trap (not shown).</summary>
    public const int OtherTrap = 1;

    /// <summary>No trap.</summary>
    public const int NotATrap = 2;

    /// <summary>A trap the viewer ordered that is not set yet.</summary>
    public const int PendingTrap = 3;

    /// <summary>The object graphic kind of an ordered trap.</summary>
    public const int PendingTrapKind = 23;

    /// <summary>What a cell shows of a building (see <see cref="ITrapJudge.Building"/>): the cell as it is.</summary>
    public const int AsItIs = 0;

    /// <summary>A remembered building in place of whatever the cell holds now.</summary>
    public const int Remembered = 1;

    /// <summary>No building: the one there is out of sight and not known.</summary>
    public const int NoBuilding = 2;

    private const int LastSpotted = CellCode.FirstSpottedTrap - CellCode.TrapKinds + 1;
    private const int LastOriginalTrapCode = CellCode.FirstTrap - (CellCode.OriginalTrapTribes * CellCode.TrapCodesPerTribe) + 1;

    /// <summary>The trap class of what fills a cell, seen by the player of <paramref name="viewer"/>.</summary>
    public static int Of(MapCell cell, int viewer) => Of(cell, new PlayerJudge(viewer));

    /// <summary>
    /// The trap class of what fills a cell, seen with <paramref name="sight"/> (a set trap's owner
    /// is its setter, a found trap's is the finder, an ordered trap's the tribe that ordered it).
    /// </summary>
    public static int Of(MapCell cell, ViewerSight sight) => Of(cell, new SightJudge(sight));

    /// <summary>The trap class of what fills a cell, seen by <paramref name="judge"/>.</summary>
    public static int Of<TJudge>(MapCell cell, TJudge judge)
        where TJudge : struct, ITrapJudge => cell.Content switch
    {
        CellContent.Trap or CellContent.SpottedTrap => judge.Knows(cell.Owner) ? OwnTrap : OtherTrap,
        CellContent.PendingTrap => judge.Marks(cell.Owner) ? PendingTrap : NotATrap,
        _ => NotATrap,
    };

    /// <summary>
    /// The trap class of a cell code (what a unit keeps of the cell it stands on), seen by the player
    /// of <paramref name="viewer"/>. The original's unused codes of the computer's spotted traps count
    /// as the computer's.
    /// </summary>
    public static int OfCode(int code, int viewer) => OfCode(code, new PlayerJudge(viewer));

    /// <summary>The trap class of a cell code, seen with <paramref name="sight"/>.</summary>
    public static int OfCode(int code, ViewerSight sight) => OfCode(code, new SightJudge(sight));

    private static int OfCode<TJudge>(int code, TJudge judge)
        where TJudge : struct, ITrapJudge
    {
        int owner = CellCode.TrapOwnerOf(code);
        if (owner >= 0)
        {
            return judge.Knows(owner) ? OwnTrap : OtherTrap;
        }
        int finder = CellCode.SpottedTrapFinderOf(code);
        if (finder >= 0)
        {
            // The original's codes are those of its player's (tribe 0's) finds.
            return judge.Knows(finder) ? OwnTrap : OtherTrap;
        }
        if (code < LastSpotted && code >= LastOriginalTrapCode)
        {
            return OtherTrap;
        }
        return code == CellCode.PlayerPendingTrap && judge.Marks(0) ? PendingTrap : NotATrap;
    }

    /// <summary>The trap kind (20..22) of a trap's code, folded as the original folds its codes.</summary>
    public static int KindOfCode(int code)
    {
        if (code < sbyte.MinValue && CellCode.SpottedTrapFinderOf(code) >= 0)
        {
            return CellCode.SpottedTrapKindOf(code);
        }
        if (code < LastOriginalTrapCode && CellCode.TrapOwnerOf(code) >= 0)
        {
            return CellCode.TrapKindOf(code);
        }
        int kind = CellCode.FirstTrapKind - (code - CellCode.FirstTrap);
        while (kind > CellCode.FirstTrapKind + CellCode.TrapKinds - 1)
        {
            kind -= CellCode.TrapKinds;
        }
        return kind;
    }
}

/// <summary>Whose traps a viewer knows, whose ordered traps it sees and which buildings it is shown.</summary>
internal interface ITrapJudge
{
    /// <summary>Whether the traps <paramref name="tribe"/> set or found are known.</summary>
    bool Knows(int tribe);

    /// <summary>Whether the traps <paramref name="tribe"/> ordered show.</summary>
    bool Marks(int tribe);

    /// <summary>
    /// The building the cell (<paramref name="x"/>, <paramref name="y"/>) holding <paramref name="content"/>
    /// shows: <see cref="CellClasses.AsItIs"/>, <see cref="CellClasses.Remembered"/> (<paramref name="seen"/>,
    /// out of sight in a match) or <see cref="CellClasses.NoBuilding"/> (a building out of sight not known).
    /// </summary>
    int Building(GameWorld world, int x, int y, MapCell content, out RememberedBuilding seen);
}

/// <summary>The player of one tribe, alone, seeing every building on its explored land as it is.</summary>
internal readonly record struct PlayerJudge(int Viewer) : ITrapJudge
{
    public bool Knows(int tribe) => tribe == Viewer;

    public bool Marks(int tribe) => tribe == Viewer;

    public int Building(GameWorld world, int x, int y, MapCell content, out RememberedBuilding seen)
    {
        seen = default;
        return CellClasses.AsItIs;
    }
}

/// <summary>A viewer's sight (see <see cref="ViewerSight"/>).</summary>
internal readonly record struct SightJudge(ViewerSight Sight) : ITrapJudge
{
    public bool Knows(int tribe) => Sight.Includes(tribe);

    public bool Marks(int tribe) => Sight.ShowsMarksOf(tribe);

    public int Building(GameWorld world, int x, int y, MapCell content, out RememberedBuilding seen)
    {
        seen = default;
        if (!Sight.RemembersBuildings)
        {
            return CellClasses.AsItIs;
        }
        bool building = content.Content == CellContent.Building;
        if (building && Sight.ShowsAsItIs(world.Building(content.Index)))
        {
            return CellClasses.AsItIs;
        }
        if (Sight.ShowsRememberedAt(x, y, out seen))
        {
            return CellClasses.Remembered;
        }
        return building ? CellClasses.NoBuilding : CellClasses.AsItIs;
    }
}
