namespace Tribes.Graphics.Composition;

/// <summary>Whose rules a body animation is drawn with.</summary>
public enum BodySubject
{
    /// <summary>A villager: appearance, weapon, load and headwear rules apply.</summary>
    Villager,

    /// <summary>A wild animal: every non-empty slot is drawn as stored.</summary>
    Animal,
}

/// <summary>A villager's tribe.</summary>
public enum Tribe
{
    /// <summary>The player's tribe (unit slots 1..50).</summary>
    Player = 0,

    /// <summary>The computer's tribe (unit slots 51..100).</summary>
    Computer = 1,
}

/// <summary>A villager's sex (unit field 15).</summary>
public enum Sex
{
    /// <summary>Value 0.</summary>
    Male = 0,

    /// <summary>Value 1.</summary>
    Female = 1,
}

/// <summary>Everything about a villager that changes how its poses look.</summary>
public sealed record VillagerAppearance
{
    /// <summary>Age (unit field 14) below which a villager is a child.</summary>
    public const int ChildAgeLimit = 12;

    /// <summary>Age of a default adult.</summary>
    public const int DefaultAdultAge = 20;

    /// <summary>Task code meaning "no assigned task" (unit field 64).</summary>
    public const int NoTask = 69;

    /// <summary>Weapon code meaning "bare hands" (unit field 65).</summary>
    public const int BareHands = 9;

    /// <summary>Weapon code of the stone, which the rules single out.</summary>
    public const int Stone = 1;

    private const int StyleMask = 3;
    private const int TorsoStyleShift = 2;

    /// <summary>A grown-up player villager, male, unarmed, carrying nothing.</summary>
    public static VillagerAppearance Default { get; } = new();

    /// <summary>Tribe (from the unit slot: 51 and above are the computer's).</summary>
    public Tribe Tribe { get; init; } = Tribe.Player;

    /// <summary>Age in years (unit field 14).</summary>
    public int Age { get; init; } = DefaultAdultAge;

    /// <summary>Sex (unit field 15).</summary>
    public Sex Sex { get; init; } = Sex.Male;

    /// <summary>Torso style 0..3 (bits 2-3 of unit field 51); styles 2 and 3 are styles 0 and 1 recoloured.</summary>
    public int TorsoStyle { get; init; }

    /// <summary>Hair colour 0..3 (bits 0-1 of unit field 51); 1..3 recolour the head.</summary>
    public int HeadStyle { get; init; }

    /// <summary>The player's leader, who wears the leader's headwear.</summary>
    public bool IsLeader { get; init; }

    /// <summary>Assigned task (unit field 64); its task class picks the headwear.</summary>
    public int AssignedTask { get; init; } = NoTask;

    /// <summary>Weapon held (unit field 65): 0 club, 1 stone, 2 spear, 3 stone axe, 4 bone hammer, 5 battle axe, 9 none.</summary>
    public int Weapon { get; init; } = BareHands;

    /// <summary>Carried item type (unit field 35), 0 for nothing.</summary>
    public int CarriedItem { get; init; }

    /// <summary>Carried amount (unit field 36).</summary>
    public int CarriedAmount { get; init; }

    /// <summary>Map screen only: the villager is in the selected group; a group marker is drawn under its shadow.</summary>
    public bool InSelectedGroup { get; init; }

    /// <summary>Map screen only: the villager is under the cursor; its shadow is highlighted.</summary>
    public bool Hovered { get; init; }

    /// <summary>Map screen only: a player villager inside the selection rectangle; its shadow is highlighted.</summary>
    public bool InSelectionRectangle { get; init; }

    /// <summary>Whether the villager is a child.</summary>
    public bool IsChild => Age < ChildAgeLimit;

    /// <summary>The appearance with both styles taken from unit field 51.</summary>
    public VillagerAppearance WithAppearanceBits(int bits) =>
        this with { HeadStyle = bits & StyleMask, TorsoStyle = (bits >> TorsoStyleShift) & StyleMask };
}

/// <summary>What body to compose.</summary>
/// <param name="Subject">Villager or animal rules.</param>
/// <param name="Animation">Animation number (unit field 29, or the animal's action).</param>
/// <param name="Facing">Facing 0..7 (0 east, clockwise).</param>
/// <param name="Frame">Frame number (unit field 30, or the animal's action frame).</param>
public sealed record BodyRequest(BodySubject Subject, int Animation, int Facing, int Frame)
{
    /// <summary>Appearance of a villager; ignored for animals.</summary>
    public VillagerAppearance Appearance { get; init; } = VillagerAppearance.Default;

    /// <summary>
    /// Draw the still picture used in the unit bar instead of an animation frame: the third
    /// stored frame of <see cref="Animation"/> (a child's portrait uses animation 14, 6 px higher).
    /// <see cref="Facing"/> and <see cref="Frame"/> are ignored.
    /// </summary>
    public bool Portrait { get; init; }

    /// <summary>
    /// The pose a villager has while idle inside a building with something in hand: the pose 48
    /// entries after the frame's own, drawn with the carrying rules (not applied to animation 124).
    /// </summary>
    public bool InsideBuildingPose { get; init; }

    /// <summary>
    /// The facing that moves the lunge animations (141..144) one cell forward; by default
    /// <see cref="Facing"/>. The original takes unit field 6 even when drawing an animal or an
    /// occupant that faces another way.
    /// </summary>
    public int? LungeFacing { get; init; }

    /// <summary>Show the selection highlights of the map screen (group marker, highlighted shadow).</summary>
    public bool MapSelectionMarks { get; init; }

    /// <summary>Palette variant still selected from what was drawn before.</summary>
    public int PaletteVariant { get; init; } = PaletteVariants.None;
}

/// <summary>A point relative to the anchor.</summary>
/// <param name="X">Horizontal offset.</param>
/// <param name="Y">Vertical offset.</param>
public readonly record struct AnchorPoint(int X, int Y);

/// <summary>A composed body and the points its overlays are attached to.</summary>
/// <param name="Drawing">The parts.</param>
/// <param name="TorsoAnchor">
/// Position of the torso slot: unit effects (hit marks, healing, death) are drawn here. Null
/// when the pose has no torso slot.
/// </param>
/// <param name="HeadAnchor">
/// Position of the head slot: thought bubbles, task icons, health bars and the tribe name are
/// drawn here. For animals the same as <paramref name="TorsoAnchor"/>.
/// </param>
public sealed record BodyComposition(DrawList Drawing, AnchorPoint? TorsoAnchor, AnchorPoint? HeadAnchor);
