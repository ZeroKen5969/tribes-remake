using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tribes.Graphics.Model;

/// <summary>What a part of a map object (building, resource node, trap, terrain object) does.</summary>
public enum ObjectPartKind
{
    /// <summary>
    /// Ground drawn into the terrain layer, cell by cell. A ground part with stage 0 starts the
    /// cell's run; the ground parts with a higher stage that follow it are drawn over it once the
    /// object reaches that stage. <see cref="ObjectPart.Id"/> is a sprite.
    /// </summary>
    Ground = 0,

    /// <summary>A sprite drawn above the terrain when the object's stage is at least <see cref="ObjectPart.Stage"/>.</summary>
    Fixture = 1,

    /// <summary>An effect played while the occupant of the part's group is not working.</summary>
    IdleAnimation = 2,

    /// <summary>Where the occupant of the part's group is drawn while working inside.</summary>
    OccupantAtWork = 3,

    /// <summary>
    /// An effect played while the occupant of the part's group works. Consecutive parts of this
    /// kind form a list chosen by the occupant's work state.
    /// </summary>
    WorkAnimation = 4,

    /// <summary>Where the occupant of the part's group is drawn while idle inside.</summary>
    OccupantIdle = 5,

    /// <summary>An effect that always plays (group 1: flag, group 2: building-site dust, others: smoke).</summary>
    AmbientAnimation = 6,

    /// <summary>Where the occupant of the part's group is drawn while resting inside.</summary>
    OccupantResting = 7,
}

/// <summary>One part of an object graphic.</summary>
/// <param name="Kind">What the part does.</param>
/// <param name="Group">Occupant slot (0..7) the part belongs to; for ambient animations, which kind of effect.</param>
/// <param name="Stage">Stage (0..3) from which the part is shown: building level, node stage or construction stage.</param>
/// <param name="Id">Sprite (ground, fixture) or effect (animations) number; unused for occupant positions.</param>
/// <param name="X">Horizontal offset from the object's top-left cell.</param>
/// <param name="Y">Vertical offset from the object's top-left cell.</param>
/// <param name="ShiftX">Extra horizontal offset (a multiple of 128) added to <paramref name="X"/>.</param>
/// <param name="ShiftY">Extra vertical offset (a multiple of 128) the menu panels add; map objects ignore it.</param>
public readonly record struct ObjectPart(ObjectPartKind Kind, int Group, int Stage, int Id, int X, int Y,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ShiftX = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ShiftY = 0);

/// <summary>
/// The drawing of a building, resource node, trap or terrain object: all its parts in the
/// original order (the order matters, see <see cref="ObjectPartKind.Ground"/>).
/// </summary>
/// <param name="Id">Graphic number (0..118).</param>
/// <param name="Parts">The parts.</param>
public sealed record ObjectGraphic(int Id, IReadOnlyList<ObjectPart> Parts);

/// <summary>Size of a building kind in cells.</summary>
/// <param name="Kind">Building kind (0..25).</param>
/// <param name="Width">Width in cells.</param>
/// <param name="Height">Height in cells.</param>
public readonly record struct BuildingFootprint(int Kind, int Width, int Height);

/// <summary>Data tables the composers use, copied from the original data file <c>a</c>.</summary>
/// <param name="TaskClassByTask">Block 41: villager task (unit field 64) to task class (0..16).</param>
/// <param name="HeadwearByTaskClass">Block 42: task class to headwear (0 = none, else sprite offset + 1).</param>
/// <param name="WeaponSlotByWeapon">Block 145: weapon (unit field 65) to the slot type that shows it.</param>
/// <param name="StepXByFacing">Block 1 (first 8 entries): one cell's x step per facing, used by the lunge animations.</param>
/// <param name="OccupantFacingStart">Block 140: per building kind, the first entry in <paramref name="OccupantFacing"/>.</param>
/// <param name="OccupantFacing">Block 139: facing of a working occupant by building kind and group; -1 = the occupant's own facing.</param>
/// <param name="StoreStockResources">Block 142: tribe resources shown as the store's five stock piles.</param>
/// <param name="Footprints">Block 138 rows 4 and 5: building sizes.</param>
public sealed record GraphicsTables(
    IReadOnlyList<int> TaskClassByTask,
    IReadOnlyList<int> HeadwearByTaskClass,
    IReadOnlyList<int> WeaponSlotByWeapon,
    IReadOnlyList<int> StepXByFacing,
    IReadOnlyList<int> OccupantFacingStart,
    IReadOnlyList<int> OccupantFacing,
    IReadOnlyList<int> StoreStockResources,
    IReadOnlyList<BuildingFootprint> Footprints);
