using System;
using System.Collections.Generic;

namespace Tribes.Graphics.Composition;

/// <summary>Which of the original's object drawing passes to run.</summary>
public enum ObjectPass
{
    /// <summary>
    /// Terrain layer, one cell at a time: the ground run of the cell, plus the ground parts of the
    /// reached stages.
    /// </summary>
    Ground,

    /// <summary>Object layer: fixtures, animations and occupants, drawn once per object above the terrain.</summary>
    Upper,

    /// <summary>The building placement preview and the title screen scenery: ground and fixture parts of stage 0.</summary>
    Preview,
}

/// <summary>Numbers of the object graphics used for each kind of map object.</summary>
public static class ObjectGraphicIds
{
    /// <summary>Graphic of the title screen scenery.</summary>
    public const int MenuScene = 118;

    /// <summary>First terrain tile drawn as an object graphic instead of a sprite.</summary>
    public const int FirstObjectTile = 175;

    private const int BuildingOffset = 1;
    private const int ResourceNodeOffset = 26;
    private const int TrapOffset = 1;
    private const int TerrainObjectOffset = -146;

    /// <summary>Graphic of building kind <paramref name="kind"/> (0..25).</summary>
    public static int Building(int kind) => kind + BuildingOffset;

    /// <summary>Graphic of resource node kind <paramref name="kind"/> (1..7).</summary>
    public static int ResourceNode(int kind) => kind + ResourceNodeOffset;

    /// <summary>Graphic of trap kind <paramref name="kind"/> (20 net, 21 spring, 22 pit, 23 ordered but not placed).</summary>
    public static int Trap(int kind) => kind + TrapOffset;

    /// <summary>Graphic of terrain tile <paramref name="tile"/> (175..255).</summary>
    public static int TerrainObject(int tile)
    {
        if (tile < FirstObjectTile)
        {
            throw new ArgumentOutOfRangeException(nameof(tile), tile, "tiles below 175 are sprites");
        }
        return tile + TerrainObjectOffset;
    }
}

/// <summary>A villager inside an object (a building's occupant, or a unit caught in a trap).</summary>
/// <param name="Group">Occupant slot (unit field 46, 0..7).</param>
/// <param name="WorkState">Unit field 13: 0 or below idle, 1..49 working (picks the work animation), 50 resting.</param>
/// <param name="Facing">The unit's own facing (unit field 6).</param>
/// <param name="Animation">The unit's animation (unit field 29).</param>
/// <param name="Frame">The unit's animation frame (unit field 30), also the clock of its work animation.</param>
/// <param name="Appearance">The unit's appearance.</param>
public sealed record Occupant(int Group, int WorkState, int Facing, int Animation, int Frame, VillagerAppearance Appearance);

/// <summary>
/// What object to compose, with the state its rules read. The factory methods fill the fields the
/// way the original does for each kind of object; the properties are the raw inputs of its rules.
/// </summary>
/// <param name="Graphic">Object graphic number.</param>
/// <param name="Pass">Drawing pass.</param>
public sealed record ObjectRequest(int Graphic, ObjectPass Pass)
{
    /// <summary>Unit field 13 value of a resting occupant.</summary>
    public const int RestingWorkState = 50;

    /// <summary>
    /// Kind number the special rules test: 6 draws the store's stock piles, 4 the kitchen's food,
    /// and it selects the facings of working occupants.
    /// </summary>
    public int RuleKind { get; init; }

    /// <summary>
    /// Slot index the rules read building state through: 31 and above count as the computer's,
    /// and <see cref="SlotWorkMode"/> and <see cref="SlotBuildingKind"/> belong to this slot.
    /// </summary>
    public int SlotIndex { get; init; }

    /// <summary>Stage reached: building level, node stage, or trap state.</summary>
    public int Stage { get; init; }

    /// <summary>
    /// Ground pass: which cell of the footprint to draw (row by row from the top-left cell), or -1
    /// for objects that are not buildings, which draw the first ground run.
    /// </summary>
    public int CellIndex { get; init; } = -1;

    /// <summary>The game frame counter, the clock of the idle and ambient animations.</summary>
    public int Tick { get; init; }

    /// <summary>Map row of the cell that made the object draw (the first cell of it the scan reached); it shifts the ambient animations' clocks.</summary>
    public int MapRow { get; init; }

    /// <summary>Building field 4 (0 normal, 1 upgrading, 2 repairing) of <see cref="SlotIndex"/>; upgrading and repairing show the dust.</summary>
    public int SlotWorkMode { get; init; }

    /// <summary>Building field 6 of <see cref="SlotIndex"/>; kind 2 (hut) adds hearts to a working pair.</summary>
    public int SlotBuildingKind { get; init; }

    /// <summary>Whether occupants are drawn (always for the player's objects; for the computer's only once seen).</summary>
    public bool OccupantsVisible { get; init; } = true;

    /// <summary>The player tribe's resource counts (26 entries), read by the store and kitchen fixtures.</summary>
    public IReadOnlyList<int> PlayerStock { get; init; } = Array.Empty<int>();

    /// <summary>Villagers inside.</summary>
    public IReadOnlyList<Occupant> Occupants { get; init; } = Array.Empty<Occupant>();

    /// <summary>Campaign mission 3's computer headquarters (slot 31), whose occupant animations follow a special rule.</summary>
    public bool MissionThreeHeadquarters { get; init; }

    /// <summary>Drawn on a menu screen: idle occupants face their own way instead of east.</summary>
    public bool MenuScreen { get; init; }

    /// <summary>Show the map-screen selection marks of occupants.</summary>
    public bool MapSelectionMarks { get; init; }

    /// <summary>Palette variant still selected from what was drawn before.</summary>
    public int PaletteVariant { get; init; } = PaletteVariants.None;

    /// <summary>Building <paramref name="kind"/> at <paramref name="level"/> in <paramref name="slot"/> (1..60).</summary>
    public static ObjectRequest Building(int kind, int level, int slot, ObjectPass pass) =>
        new(ObjectGraphicIds.Building(kind), pass)
        {
            RuleKind = kind,
            SlotIndex = slot,
            Stage = level,
            SlotBuildingKind = kind,
        };

    /// <summary>
    /// Resource node <paramref name="kind"/> at <paramref name="stage"/> in node slot
    /// <paramref name="slot"/> (0..49). The original reads the building state of the building slot
    /// with the same number; set <see cref="SlotWorkMode"/> and <see cref="SlotBuildingKind"/> to reproduce it.
    /// </summary>
    public static ObjectRequest ResourceNode(int kind, int stage, int slot, ObjectPass pass) =>
        new(ObjectGraphicIds.ResourceNode(kind), pass) { RuleKind = kind, SlotIndex = slot, Stage = stage };

    /// <summary>Terrain tile <paramref name="tile"/> (175..255).</summary>
    public static ObjectRequest TerrainObject(int tile, ObjectPass pass) =>
        new(ObjectGraphicIds.TerrainObject(tile), pass) { RuleKind = ObjectGraphicIds.TerrainObject(tile) };

    /// <summary>
    /// Trap <paramref name="kind"/> (20..23): stage 1 placed, 2 with a unit caught in it (pass that
    /// unit as the occupant of group 0), 0 ordered but not placed (kind 23). In the terrain layer
    /// the original draws a computer trap with <see cref="ObjectPass.Upper"/> instead of
    /// <see cref="ObjectPass.Ground"/>, which hides it.
    /// </summary>
    public static ObjectRequest Trap(int kind, int stage, ObjectPass pass) =>
        new(ObjectGraphicIds.Trap(kind), pass) { RuleKind = kind, Stage = stage };

    /// <summary>The placement preview of building <paramref name="kind"/>.</summary>
    public static ObjectRequest Preview(int kind) =>
        new(ObjectGraphicIds.Building(kind), ObjectPass.Preview) { RuleKind = ObjectGraphicIds.Building(kind) };
}

/// <summary>A composed object.</summary>
/// <param name="Drawing">The parts, relative to the object's top-left cell, occupants included.</param>
/// <param name="Occupants">The occupants drawn, with their positions and anchors.</param>
public sealed record ObjectComposition(DrawList Drawing, IReadOnlyList<PlacedOccupant> Occupants);

/// <summary>An occupant as drawn inside an object.</summary>
/// <param name="Group">Its occupant slot.</param>
/// <param name="X">Horizontal position of its anchor relative to the object's top-left cell.</param>
/// <param name="Y">Vertical position of its anchor.</param>
/// <param name="Facing">The facing it was drawn with.</param>
/// <param name="TorsoAnchor">Its torso anchor, relative to the object's top-left cell.</param>
/// <param name="HeadAnchor">Its head anchor, relative to the object's top-left cell.</param>
public sealed record PlacedOccupant(int Group, int X, int Y, int Facing, AnchorPoint? TorsoAnchor, AnchorPoint? HeadAnchor);
