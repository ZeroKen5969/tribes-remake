using System.IO;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Maps;

/// <summary>
/// The original game's numeric cell codes. The content tables classify cells by these numbers
/// (code ranges, slot = code * multiplier + offset), and units remember the code of the cell they
/// were sent to, so some rules work on codes rather than on <see cref="MapCell"/> values.
/// The original's codes fit a byte: units 1..100, animals 101.., buildings -1..-60, nodes
/// -61..-110, the traps of tribes 0 and 1 -111..-122 (with the traps the player found, -114..-116),
/// then the special codes. Entities beyond those (further unit and building slots, more animals or
/// nodes, traps of further tribes, traps found by tribes other than tribe 0) get codes far outside
/// that range, of the same sign as their kind's, in ranges chosen so that the negation of a code
/// (which a unit keeps for the object of the cell it steps into) is never another code; ask the
/// helpers here (<see cref="UnitOf"/>, <see cref="OfUnit"/> ...) instead of comparing codes with
/// numbers.
/// </summary>
public static class CellCode
{
    /// <summary>An empty cell.</summary>
    public const int Empty = 0;

    /// <summary>Code of animal slot 0; animal slot i has code FirstAnimal + i.</summary>
    public const int FirstAnimal = 101;

    /// <summary>Code of resource node slot 0; node slot i has code FirstNode - i.</summary>
    public const int FirstNode = -61;

    /// <summary>Code of the first trap kind set by tribe 0; tribe t's trap of kind k is FirstTrap - 6t - (k - 20).</summary>
    public const int FirstTrap = -111;

    /// <summary>Trap codes reserved per tribe.</summary>
    public const int TrapCodesPerTribe = 6;

    /// <summary>Trap kinds (net, spring, pit).</summary>
    public const int TrapKinds = 3;

    /// <summary>Code of the first trap kind found by tribe 0 (the player's spotted traps of the original).</summary>
    public const int FirstSpottedTrap = -114;

    /// <summary>A trap the player ordered but has not set yet.</summary>
    public const int PlayerPendingTrap = -123;

    /// <summary>A thicket (a tree cell).</summary>
    public const int Thicket = -124;

    /// <summary>A rock formation.</summary>
    public const int Rocks = -125;

    /// <summary>Terrain nothing can enter, the map's edge, or a destroyed target.</summary>
    public const int Impassable = -126;

    /// <summary>Shore beside a fishing spot.</summary>
    public const int Shore = -127;

    /// <summary>The cell a unit or animal is stepping into.</summary>
    public const int Reserved = -128;

    /// <summary>The building kind number of the first trap kind.</summary>
    public const int FirstTrapKind = 20;

    /// <summary>Unit slots, building slots, animal slots, node slots and trap-setting tribes with codes of the original's range.</summary>
    public const int OriginalUnits = 100, OriginalBuildings = 60, OriginalAnimals = 27, OriginalNodes = 50, OriginalTrapTribes = 2;

    /// <summary>Further units have code ExtendedUnits + slot, further animals ExtendedAnimals + slot.</summary>
    private const int ExtendedUnits = 1 << 24, ExtendedAnimals = 1 << 25, PositiveEnd = 1 << 26;

    /// <summary>
    /// Further buildings have code -(ExtendedBuildings + slot), traps of further tribes
    /// -(ExtendedTraps + 6t + k - 20), traps found by tribe t >= 1 -(ExtendedTraps + 6t + 3 + k - 20),
    /// further nodes -(ExtendedNodes + slot).
    /// </summary>
    private const int ExtendedBuildings = 1 << 26, ExtendedTraps = 1 << 27, ExtendedNodes = 1 << 28, NegativeEnd = 1 << 30;

    /// <summary>The lowest code of the original's range.</summary>
    private const int LowestOriginal = Reserved;

    /// <summary>The code of unit slot <paramref name="unit"/>.</summary>
    public static int OfUnit(int unit) => unit <= OriginalUnits ? unit : ExtendedUnits + unit;

    /// <summary>The code of building slot <paramref name="building"/>.</summary>
    public static int OfBuilding(int building) => building <= OriginalBuildings ? -building : -(ExtendedBuildings + building);

    /// <summary>The code of animal slot <paramref name="animal"/>.</summary>
    public static int OfAnimal(int animal) => animal < OriginalAnimals ? FirstAnimal + animal : ExtendedAnimals + animal;

    /// <summary>The code of resource node slot <paramref name="node"/>.</summary>
    public static int OfNode(int node) => node < OriginalNodes ? FirstNode - node : -(ExtendedNodes + node);

    /// <summary>The unit slot <paramref name="code"/> stands for, or 0 when it is not a unit's code.</summary>
    public static int UnitOf(int code) => code switch
    {
        > 0 and <= OriginalUnits => code,
        > ExtendedUnits + OriginalUnits and < ExtendedAnimals => code - ExtendedUnits,
        _ => 0,
    };

    /// <summary>The building slot <paramref name="code"/> stands for, or 0 when it is not a building's code.</summary>
    public static int BuildingOf(int code) => code switch
    {
        < 0 and >= -OriginalBuildings => -code,
        < -(ExtendedBuildings + OriginalBuildings) and > -ExtendedTraps => -code - ExtendedBuildings,
        _ => 0,
    };

    /// <summary>The animal slot <paramref name="code"/> stands for, or -1 when it is not an animal's code.</summary>
    public static int AnimalOf(int code) => code switch
    {
        >= FirstAnimal and < FirstAnimal + OriginalAnimals => code - FirstAnimal,
        >= ExtendedAnimals + OriginalAnimals and < PositiveEnd => code - ExtendedAnimals,
        _ => -1,
    };

    /// <summary>The resource node slot <paramref name="code"/> stands for, or -1 when it is not a node's code.</summary>
    public static int NodeOf(int code) => code switch
    {
        <= FirstNode and > FirstNode - OriginalNodes => FirstNode - code,
        <= -(ExtendedNodes + OriginalNodes) and > -NegativeEnd => -code - ExtendedNodes,
        _ => -1,
    };

    /// <summary>
    /// The unit slot of a further unit's code; any code of the original's range as it is (the
    /// original uses unit codes as slots).
    /// </summary>
    public static int UnitOrOriginal(int code) => code is >= sbyte.MinValue and <= sbyte.MaxValue ? code : UnitOf(code);

    /// <summary>
    /// The slot of the kind expected that code <paramref name="code"/> stands for, as code *
    /// <paramref name="sign"/> (minus the first animal code for <paramref name="animal"/>) gives it
    /// for the original's codes: sign 1 a unit, -1 a building, an animal's slot for animals. A
    /// further code of another kind gives -1 (no slot), as the original's formula gives no valid
    /// slot for a code of the wrong kind.
    /// </summary>
    public static int SlotOf(int code, int sign, bool animal)
    {
        if (code is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return Wrap.Signed8((code * sign) - (animal ? FirstAnimal : 0));
        }
        int slot = animal ? AnimalOf(code) : sign > 0 ? UnitOf(code) : sign < 0 ? BuildingOf(code) : 0;
        return sign == 0 && !animal ? 0 : slot > 0 || (animal && slot == 0) ? slot : -1;
    }

    /// <summary>Whether <paramref name="code"/> is the code of a unit or an animal (a creature that moves).</summary>
    public static bool IsCreature(int code) => UnitOf(code) > 0 || AnimalOf(code) >= 0;

    /// <summary>
    /// Whether <paramref name="code"/> is an object a unit can stand on (as it keeps it in
    /// <see cref="Unit.NextOccupantOrCellObject"/>): a trap's or a special code of the original's
    /// range (<see cref="FirstTrap"/> down to <see cref="Reserved"/>), or a set trap of a further tribe.
    /// </summary>
    public static bool IsStandingObject(int code) =>
        (code <= FirstTrap && code >= LowestOriginal) || (code < -ExtendedTraps && TrapOwnerOf(code) >= 0);

    /// <summary>
    /// Whether <paramref name="value"/> is the negation of an object a unit is stepping onto (see
    /// <see cref="IsStandingObject"/>; the original's byte keeps the negation of the lowest code as itself).
    /// </summary>
    public static bool IsSteppingObject(int value) => value is >= -FirstTrap and <= -LowestOriginal || (value > ExtendedTraps && IsStandingObject(-value));

    /// <summary>
    /// The value a unit keeps for the object of the cell it steps into, of code
    /// <paramref name="code"/>: its negation (a byte in the original, so the codes of its range wrap).
    /// </summary>
    public static int SteppingValue(int code) => code >= LowestOriginal && code <= sbyte.MaxValue ? Wrap.Signed8(-code) : -code;

    /// <summary>Whether a unit standing on object <paramref name="code"/> is caught in a trap (the original's trap codes).</summary>
    public static bool IsCatching(int code) =>
        (code <= FirstTrap && code > PlayerPendingTrap) || (code < -ExtendedTraps && TrapOwnerOf(code) >= 0);

    /// <summary>Whether <paramref name="code"/> is the code of a set trap (not spotted, not pending) of any tribe.</summary>
    public static bool IsSetTrap(int code) => TrapOwnerOf(code) >= 0;

    /// <summary>The tribe whose set trap <paramref name="code"/> is, or -1.</summary>
    public static int TrapOwnerOf(int code)
    {
        if (code <= FirstTrap && code > FirstTrap - (OriginalTrapTribes * TrapCodesPerTribe))
        {
            int offset = FirstTrap - code;
            return offset % TrapCodesPerTribe < TrapKinds ? offset / TrapCodesPerTribe : -1;
        }
        if (code <= -(ExtendedTraps + (OriginalTrapTribes * TrapCodesPerTribe)) && code > -ExtendedNodes)
        {
            int offset = -code - ExtendedTraps;
            return offset % TrapCodesPerTribe < TrapKinds ? offset / TrapCodesPerTribe : -1;
        }
        return -1;
    }

    /// <summary>The trap kind (building kind 20..22) of set trap code <paramref name="code"/>.</summary>
    public static int TrapKindOf(int code)
    {
        int offset = code > -ExtendedTraps ? FirstTrap - code : -code - ExtendedTraps;
        return FirstTrapKind + (offset % TrapCodesPerTribe);
    }

    /// <summary>The code of the trap of kind <paramref name="kind"/> set by <paramref name="tribe"/>.</summary>
    public static int Trap(int tribe, int kind) => tribe < OriginalTrapTribes
        ? FirstTrap - (tribe * TrapCodesPerTribe) - (kind - FirstTrapKind)
        : -(ExtendedTraps + (tribe * TrapCodesPerTribe) + (kind - FirstTrapKind));

    /// <summary>The code of the trap of kind <paramref name="kind"/> that <paramref name="finder"/> found.</summary>
    public static int SpottedTrap(int finder, int kind) => finder == 0
        ? FirstSpottedTrap - (kind - FirstTrapKind)
        : -(ExtendedTraps + (finder * TrapCodesPerTribe) + TrapKinds + (kind - FirstTrapKind));

    /// <summary>The tribe that found the trap of code <paramref name="code"/>, or -1 when it is not a found trap.</summary>
    public static int SpottedTrapFinderOf(int code)
    {
        if (code <= FirstSpottedTrap && code > FirstSpottedTrap - TrapKinds)
        {
            return 0;
        }
        if (code <= -(ExtendedTraps + TrapCodesPerTribe) && code > -ExtendedNodes)
        {
            int offset = -code - ExtendedTraps;
            return offset % TrapCodesPerTribe >= TrapKinds ? offset / TrapCodesPerTribe : -1;
        }
        return -1;
    }

    /// <summary>The trap kind (building kind 20..22) of found trap code <paramref name="code"/>.</summary>
    public static int SpottedTrapKindOf(int code) => code > -ExtendedTraps
        ? FirstTrapKind + (FirstSpottedTrap - code)
        : FirstTrapKind + ((-code - ExtendedTraps) % TrapCodesPerTribe) - TrapKinds;

    /// <summary>The model cell for code <paramref name="code"/> in a world of <paramref name="layout"/>'s fixed slots.</summary>
    /// <exception cref="InvalidDataException">The code is not one of such a world.</exception>
    public static MapCell FromCode(int code, WorldLayout layout)
    {
        System.ArgumentNullException.ThrowIfNull(layout);
        return FromCode(code, layout.Tribes * layout.UnitsPerTribe, layout.Tribes * layout.BuildingsPerTribe, layout.Animals, layout.Tribes);
    }

    /// <summary>The model cell for code <paramref name="code"/> in <paramref name="world"/> (with the slots it has now).</summary>
    /// <exception cref="InvalidDataException">The code is not one of the world.</exception>
    public static MapCell FromCode(int code, GameWorld world)
    {
        System.ArgumentNullException.ThrowIfNull(world);
        return FromCode(code, world.UnitSlots, world.BuildingSlots, world.AnimalSlots, world.Tribes.Count);
    }

    private static MapCell FromCode(int code, int units, int buildings, int animals, int tribes)
    {
        if (code == Empty)
        {
            return MapCell.Empty;
        }
        if (UnitOf(code) is > 0 and int unit && unit <= units)
        {
            return MapCell.UnitAt(unit);
        }
        if (AnimalOf(code) is >= 0 and int animal && animal < animals)
        {
            return MapCell.AnimalAt(animal);
        }
        if (BuildingOf(code) is > 0 and int building && building <= buildings)
        {
            return MapCell.BuildingAt(building);
        }
        if (NodeOf(code) is >= 0 and int node)
        {
            return MapCell.Node(node);
        }
        if (TrapOwnerOf(code) is >= 0 and int owner && owner < System.Math.Max(tribes, OriginalTrapTribes))
        {
            return new MapCell(CellContent.Trap, TrapKindOf(code), owner);
        }
        if (SpottedTrapFinderOf(code) is >= 0 and int finder && (finder == 0 || finder < tribes))
        {
            return new MapCell(CellContent.SpottedTrap, SpottedTrapKindOf(code), finder);
        }
        return code switch
        {
            PlayerPendingTrap => new MapCell(CellContent.PendingTrap, 0, 0),
            Thicket => MapCell.Of(CellContent.Thicket),
            Rocks => MapCell.Of(CellContent.Rocks),
            Impassable => MapCell.Of(CellContent.Impassable),
            Shore => MapCell.Of(CellContent.Shore),
            Reserved => MapCell.Of(CellContent.Reserved),
            _ => throw new InvalidDataException($"unknown cell code {code}"),
        };
    }

    /// <summary>
    /// The code of model cell <paramref name="cell"/>. The computer's pending traps have no code of
    /// their own in the original: their cells read as impassable.
    /// </summary>
    /// <exception cref="InvalidDataException">The cell content is not a known kind.</exception>
    public static int ToCode(MapCell cell) => cell.Content switch
    {
        CellContent.Empty => Empty,
        CellContent.Unit => OfUnit(cell.Index),
        CellContent.Animal => OfAnimal(cell.Index),
        CellContent.Building => OfBuilding(cell.Index),
        CellContent.ResourceNode => OfNode(cell.Index),
        CellContent.Trap => Trap(cell.Owner, cell.Index),
        CellContent.SpottedTrap => SpottedTrap(cell.Owner, cell.Index),
        CellContent.PendingTrap => cell.Owner == 0 ? PlayerPendingTrap : Impassable,
        CellContent.Impassable => Impassable,
        CellContent.Rocks => Rocks,
        CellContent.Thicket => Thicket,
        CellContent.Shore => Shore,
        CellContent.Reserved => Reserved,
        _ => throw new InvalidDataException(cell.ToString()),
    };

    /// <summary>The code of what fills <paramref name="cell"/> of <paramref name="map"/>.</summary>
    public static int At(WorldMap map, GridPoint cell)
    {
        System.ArgumentNullException.ThrowIfNull(map);
        return ToCode(map.CellAt(cell));
    }
}
