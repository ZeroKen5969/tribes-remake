using Tribes.Core.Content;

namespace Tribes.Core.Maps;

/// <summary>What fills a map cell, as far as moving, building and targeting are concerned.</summary>
public enum CellContent : byte
{
    /// <summary>Nothing; units may walk here and buildings may stand here.</summary>
    Empty,

    /// <summary>A unit; <see cref="MapCell.Index"/> is its slot.</summary>
    Unit,

    /// <summary>A wild animal; <see cref="MapCell.Index"/> is its slot.</summary>
    Animal,

    /// <summary>Part of a building's footprint; <see cref="MapCell.Index"/> is the building slot.</summary>
    Building,

    /// <summary>A resource node; <see cref="MapCell.Index"/> is its slot.</summary>
    ResourceNode,

    /// <summary>A trap; <see cref="MapCell.Index"/> is the trap kind, <see cref="MapCell.Owner"/> the tribe.</summary>
    Trap,

    /// <summary>A trap an enemy scout has spotted; <see cref="MapCell.Index"/> is the trap kind.</summary>
    SpottedTrap,

    /// <summary>A trap ordered but not set yet; <see cref="MapCell.Owner"/> is the tribe.</summary>
    PendingTrap,

    /// <summary>Terrain nothing can enter, or the edge of the map.</summary>
    Impassable,

    /// <summary>A rock formation.</summary>
    Rocks,

    /// <summary>Dense vegetation.</summary>
    Thicket,

    /// <summary>Shore beside a node that marks the shore; only units working that node may enter.</summary>
    Shore,

    /// <summary>The cell a unit or animal is stepping into.</summary>
    Reserved,
}

/// <summary>The content of one cell: its kind, the slot or kind it refers to and, for traps, the owner.</summary>
public readonly record struct MapCell(CellContent Content, int Index, int Owner)
{
    /// <summary>An empty cell (also the default value).</summary>
    public static MapCell Empty => default;

    /// <summary>A cell filled with <paramref name="content"/> that refers to nothing else.</summary>
    public static MapCell Of(CellContent content) => new(content, 0, 0);

    /// <summary>The cell of resource node <paramref name="slot"/>.</summary>
    public static MapCell Node(int slot) => new(CellContent.ResourceNode, slot, 0);

    /// <summary>A cell of unit <paramref name="slot"/>.</summary>
    public static MapCell UnitAt(int slot) => new(CellContent.Unit, slot, 0);

    /// <summary>A cell of animal <paramref name="slot"/>.</summary>
    public static MapCell AnimalAt(int slot) => new(CellContent.Animal, slot, 0);

    /// <summary>A cell of building <paramref name="slot"/>.</summary>
    public static MapCell BuildingAt(int slot) => new(CellContent.Building, slot, 0);

    /// <summary>Whether a unit or animal can step in (nothing there).</summary>
    public bool IsEmpty => Content == CellContent.Empty;
}

/// <summary>A place where units gather a resource, in a fixed slot; the slot is free when <see cref="Kind"/> is 0.</summary>
public sealed class ResourceNode
{
    private int kind, amount, stage;

    internal ResourceNode(int index) => Index = index;

    /// <summary>Slot number.</summary>
    public int Index { get; }

    /// <summary>Whether the slot holds a node.</summary>
    public bool InUse => Kind != 0;

    /// <summary>The <see cref="ResourceNodeKind"/> number; 0 for a free slot.</summary>
    public int Kind { get => kind; set => kind = World.Wrap.Signed8(value); }

    /// <summary>Cell column.</summary>
    public int X { get; set; }

    /// <summary>Cell row.</summary>
    public int Y { get; set; }

    /// <summary>The cell it stands on.</summary>
    public GridPoint Position => new(X, Y);

    /// <summary>What is left to gather.</summary>
    public int Amount { get => amount; set => amount = World.Wrap.Signed8(value); }

    /// <summary>How full it looks (0..3, from the amount left); also read when picking nodes to gather.</summary>
    public int Stage { get => stage; set => stage = World.Wrap.Signed8(value); }

    internal void Exchange(World.StateExchange s)
    {
        Kind = s.Value(Kind);
        X = s.Value(X);
        Y = s.Value(Y);
        Amount = s.Value(Amount);
        Stage = s.Value(Stage);
    }
}
