using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>One frame of an effect animation: a composite sprite and its offset from the anchor.</summary>
/// <param name="Sprite">The composite sprite drawn.</param>
/// <param name="X">Horizontal offset from the anchor.</param>
/// <param name="Y">Vertical offset from the anchor.</param>
public readonly record struct EffectFrame(int Sprite, int X, int Y);

/// <summary>
/// An effect animation (cursor, markers, smoke, flags, thought bubbles, dust...). The frame shown
/// is the caller's tick modulo the frame count; most effects advance one frame per game frame.
/// </summary>
/// <param name="Id">Effect number (0..143).</param>
/// <param name="FirstFrame">
/// Position of the first frame in the original's single frame table. The effects are stored one
/// after the other, so a negative tick (which the original's remainder keeps negative) reaches
/// back into the frames of the effects before this one.
/// </param>
/// <param name="Frames">The frames in order.</param>
public sealed record EffectAnimation(int Id, int FirstFrame, IReadOnlyList<EffectFrame> Frames);

/// <summary>How an animation decides which weapon or load slots of its poses are shown.</summary>
public enum BodyAnimationCategory
{
    /// <summary>Each weapon has its own slot type; the slot of the held weapon is shown, the others hidden.</summary>
    WeaponBySlot = 0,

    /// <summary>The club slot shows the held weapon, picked by adding the weapon number to its sprite.</summary>
    WeaponInClubSlot = 1,

    /// <summary>The load slot shows what is carried; the club slot shows the weapon.</summary>
    Carrying = 2,
}

/// <summary>One frame of a body animation: the pose and its offset from the anchor.</summary>
/// <param name="Pose">The pose drawn.</param>
/// <param name="X">Horizontal offset from the anchor.</param>
/// <param name="Y">Vertical offset from the anchor.</param>
public readonly record struct BodyFrame(int Pose, int X, int Y);

/// <summary>
/// An animation of a villager or a wild animal (the unit's animation id, unit field 29, or the
/// animal's action). Frames are stored direction by direction: frame <c>f</c> of direction
/// <c>d</c> is <c>Frames[d * FramesPerDirection + f]</c>; the facing (0..7) is turned into a
/// direction with <see cref="SpriteCatalog.DirectionMaps"/>. The original never mirrors a sprite:
/// every direction has its own drawings.
/// </summary>
/// <param name="Id">Animation number (0..161).</param>
/// <param name="Category">Which weapon and load slots the poses show.</param>
/// <param name="DirectionCount">1, 2, 4 or 8.</param>
/// <param name="FramesPerDirection">Frames of one direction; the animation's length in ticks.</param>
/// <param name="TriggerFrame">Frame at which the action takes effect (a hit, a chop), or -1.</param>
/// <param name="TriggerParameter">What the action does at <see cref="TriggerFrame"/>, or -1.</param>
/// <param name="FirstFrame">Position of the first frame in the original's single frame table.</param>
/// <param name="Frames">The frames, direction by direction.</param>
public sealed record BodyAnimation(int Id, BodyAnimationCategory Category, int DirectionCount,
    int FramesPerDirection, int TriggerFrame, int TriggerParameter, int FirstFrame,
    IReadOnlyList<BodyFrame> Frames);

/// <summary>Which direction drawings a facing uses, for animations with a given number of directions.</summary>
/// <param name="DirectionCount">1, 2, 4 or 8.</param>
/// <param name="DirectionByFacing">
/// For facings 0..7 (0 east, then clockwise: 1 south-east, 2 south ... 7 north-east) the direction index.
/// </param>
public sealed record DirectionMap(int DirectionCount, IReadOnlyList<int> DirectionByFacing);

/// <summary>What a pose slot shows. Names are those of a villager's poses; animals only use <see cref="Torso"/> as their anchor.</summary>
public enum SlotType
{
    /// <summary>Torso and clothes; varies with tribe, sex and torso style. Effects on the unit are anchored here.</summary>
    Torso = 0,

    /// <summary>The arm drawn behind the body.</summary>
    BackArm = 1,

    /// <summary>The arm drawn in front.</summary>
    FrontArm = 2,

    /// <summary>Legs.</summary>
    Legs = 3,

    /// <summary>What a carrying villager carries; varies with the carried item.</summary>
    Load = 4,

    /// <summary>Club-type weapon in hand (club, bone hammer), or any weapon in animations that use one weapon slot.</summary>
    ClubWeapon = 5,

    /// <summary>Axe-type weapon in hand (stone axe, battle axe).</summary>
    AxeWeapon = 6,

    /// <summary>Headwear showing the villager's task or rank.</summary>
    Headwear = 7,

    /// <summary>Spear or stone in hand.</summary>
    SpearOrStone = 8,

    /// <summary>Head; varies with age, sex and hair colour. Icons, thought bubbles and health bars are anchored here.</summary>
    Head = 9,

    /// <summary>Shadow on the ground; the next sprite numbers are the selection highlights.</summary>
    Shadow = 10,
}

/// <summary>Whether a reference points at a piece or at a composite sprite.</summary>
public enum PartSource
{
    /// <summary>A single <see cref="Piece"/>.</summary>
    Piece,

    /// <summary>A <see cref="CompositeSprite"/>.</summary>
    Sprite,
}

/// <summary>One slot of a pose.</summary>
/// <param name="Type">What the slot shows.</param>
/// <param name="Source">Whether <paramref name="Id"/> is a piece or a sprite.</param>
/// <param name="Id">
/// The piece or sprite shown with the default appearance; the appearance rules add an offset to
/// it. 0 means the slot is empty; 1 means the slot is only drawn for wild animals.
/// </param>
/// <param name="X">Horizontal offset from the frame's anchor.</param>
/// <param name="Y">Vertical offset from the frame's anchor.</param>
public readonly record struct PoseSlot(SlotType Type, PartSource Source, int Id, int X, int Y);

/// <summary>A pose: the slots drawn, in drawing order.</summary>
/// <param name="Id">Pose number (0..822).</param>
/// <param name="Slots">The slots in drawing order.</param>
public sealed record Pose(int Id, IReadOnlyList<PoseSlot> Slots);

/// <summary>A thrown weapon's picture in flight.</summary>
/// <param name="Weapon">Weapon number (unit field 65): 1 stone, 2 spear, 5 battle axe.</param>
/// <param name="FirstSprite">Sprite of offset 0.</param>
/// <param name="DirectionStride">Entries of <paramref name="FrameOffsets"/> per flight direction.</param>
/// <param name="FrameOffsets">
/// Sprite offset by <c>direction * DirectionStride + flightFrame</c>; the drawn sprite is
/// <c>FirstSprite + FrameOffsets[...]</c>.
/// </param>
public sealed record ProjectileGraphic(int Weapon, int FirstSprite, int DirectionStride, IReadOnlyList<int> FrameOffsets);
