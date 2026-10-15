using System;
using System.Collections.Generic;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Composition;

/// <summary>
/// Composes villagers and wild animals: picks the frame for the animation, facing and frame
/// number, then draws the pose's slots with the appearance rules of the original.
/// </summary>
public sealed class BodyComposer
{
    /// <summary>Sprite drawn under the shadow of a villager in the selected group.</summary>
    public const int GroupMarkerSprite = 599;

    /// <summary>Number of facings.</summary>
    public const int FacingCount = 8;

    private const int EmptySlot = 0;
    private const int AnimalOnlySlot = 1;
    private const int TorsoStyleStride = 72;
    private const int RecolouredTorsoStyles = 2;
    private const int ComputerTorsoOffset = 36;
    private const int FemaleTorsoOffset = 18;
    private const int HeadRowStride = 12;
    private const int FemaleHeadRow = 1;
    private const int YoungHeadRow = 3;
    private const int MiddleAgedHeadRow = 2;
    private const int OldHeadRow = 0;
    private const int YoungAgeLimit = 30;
    private const int MiddleAgeLimit = 50;
    private const int LeaderHeadwearOffset = 2;
    private const int PlayerHighlightOffset = 1;
    private const int ComputerHighlightOffset = 3;
    private const int TorsoRecolourVariant = 0;
    private const int ComputerHeadVariant = 3;
    private const int ComputerVariant = 4;
    private const int BoneHammer = 4;
    private const int BattleAxe = 5;
    private const int AlternateWeaponOffset = 1;
    private const int InsideBuildingPoseOffset = 48;
    private const int InsideBuildingAnimation = 1;
    private const int AnimationWithoutInsidePose = 124;
    private const int FirstLungeAnimation = 141;
    private const int LastLungeAnimation = 144;
    private const int FirstShownLoadAnimation = 113;
    private const int LastShownLoadAnimation = 116;
    private const int PortraitFrame = 2;
    private const int ChildPortraitAnimation = 14;
    private const int ChildPortraitRaise = 6;

    private readonly SpriteComposer sprites;
    private readonly SpriteCatalog catalog;
    private readonly BodyFrame[] frames;
    private readonly Dictionary<int, DirectionMap> directionMaps = new();

    /// <summary>Creates a composer drawing through <paramref name="sprites"/>.</summary>
    public BodyComposer(SpriteComposer sprites)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        this.sprites = sprites;
        catalog = sprites.Catalog;
        var all = new List<BodyFrame>();
        foreach (BodyAnimation animation in catalog.BodyAnimations)
        {
            if (animation.FirstFrame != all.Count)
            {
                throw new ArgumentException($"animation {animation.Id} is not stored after the one before it", nameof(sprites));
            }
            all.AddRange(animation.Frames);
        }
        frames = all.ToArray();
        foreach (DirectionMap map in catalog.DirectionMaps)
        {
            directionMaps[map.DirectionCount] = map;
        }
    }

    /// <summary>
    /// The parts of one frame of a body. Frame numbers past the end of a direction run on into the
    /// next direction's (and animation's) frames, as in the original.
    /// </summary>
    public BodyComposition Compose(BodyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Portrait && (request.Facing < 0 || request.Facing >= FacingCount))
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Facing, "facing must be 0..7");
        }
        var state = new SlotState(request);
        DrawList drawing = SpriteComposer.Run(request.PaletteVariant, buffer => Append(buffer, request, state, 0, 0));
        return new BodyComposition(drawing, state.TorsoAnchor, state.HeadAnchor);
    }

    /// <summary>
    /// The offset of the first slot of <paramref name="slotType"/> in the frame's pose (the last
    /// slot when there is none), without the frame's own offset: where the original draws an
    /// effect attached to that slot of a unit or an animal.
    /// </summary>
    public AnchorPoint EffectSlotOffset(int animation, int facing, int frame, SlotType slotType)
    {
        Pose pose = PoseOf(SpriteComposer.Lookup(catalog.BodyAnimations, animation, "animation"), facing, frame, out _, out _);
        PoseSlot chosen = default;
        foreach (PoseSlot slot in pose.Slots)
        {
            chosen = slot;
            if (slot.Type == slotType)
            {
                break;
            }
        }
        return new AnchorPoint(chosen.X, chosen.Y);
    }

    /// <summary>
    /// Draws the body at (<paramref name="x"/>, <paramref name="y"/>) into <paramref name="buffer"/>
    /// and returns its torso and head anchors (relative to the same origin as x and y). A missing
    /// reference stops this body only, as the original's unit drawing catches its own exceptions.
    /// </summary>
    internal (AnchorPoint? Torso, AnchorPoint? Head) Append(PartBuffer buffer, BodyRequest request, int x, int y)
    {
        var state = new SlotState(request);
        try
        {
            Append(buffer, request, state, x, y);
        }
        catch (DrawingStoppedException)
        {
            // Drawing of this body ends here; the caller goes on with the rest.
        }
        return (state.TorsoAnchor, state.HeadAnchor);
    }

    private void Append(PartBuffer buffer, BodyRequest request, SlotState state, int x, int y)
    {
        int animationId = request.Animation;
        int poseId;
        if (!request.Portrait)
        {
            BodyAnimation animation = SpriteComposer.Lookup(catalog.BodyAnimations, animationId, "animation");
            Pose framePose = PoseOf(animation, request.Facing, request.Frame, out int dx, out int dy);
            poseId = framePose.Id;
            x += dx;
            y += dy;
            if (request.InsideBuildingPose && animationId != AnimationWithoutInsidePose)
            {
                poseId += InsideBuildingPoseOffset;
                animationId = InsideBuildingAnimation;
            }
            if (animationId >= FirstLungeAnimation && animationId <= LastLungeAnimation)
            {
                int facing = request.LungeFacing ?? request.Facing;
                x += catalog.Cell.Width * SpriteComposer.Lookup(catalog.Tables.StepXByFacing, facing, "facing step");
            }
        }
        else
        {
            if (request.Subject == BodySubject.Villager && request.Appearance.IsChild)
            {
                animationId = ChildPortraitAnimation;
                y -= ChildPortraitRaise;
            }
            BodyAnimation animation = SpriteComposer.Lookup(catalog.BodyAnimations, animationId, "animation");
            poseId = SpriteComposer.Lookup(frames, animation.FirstFrame + PortraitFrame, "frame").Pose;
        }
        state.Animation = animationId;
        state.Category = SpriteComposer.Lookup(catalog.BodyAnimations, animationId, "animation").Category;
        AppendPose(buffer, SpriteComposer.Lookup(catalog.Poses, poseId, "pose"), state, x, y);
    }

    private Pose PoseOf(BodyAnimation animation, int facing, int frame, out int dx, out int dy)
    {
        if (!directionMaps.TryGetValue(animation.DirectionCount, out DirectionMap? map))
        {
            throw new DrawingStoppedException($"no direction map for {animation.DirectionCount} directions");
        }
        int direction = SpriteComposer.Lookup(map.DirectionByFacing, facing, "facing");
        BodyFrame bodyFrame = SpriteComposer.Lookup(frames, animation.FirstFrame + (direction * animation.FramesPerDirection) + frame, "frame");
        dx = bodyFrame.X;
        dy = bodyFrame.Y;
        return SpriteComposer.Lookup(catalog.Poses, bodyFrame.Pose, "pose");
    }

    private void AppendPose(PartBuffer buffer, Pose pose, SlotState state, int x, int y)
    {
        VillagerAppearance look = state.Request.Appearance;
        bool villager = state.Request.Subject == BodySubject.Villager;
        foreach (PoseSlot slot in pose.Slots)
        {
            if (slot.Id == EmptySlot)
            {
                continue;
            }
            int sx = x + slot.X;
            int sy = y + slot.Y;
            state.Offset = 0;
            if (villager)
            {
                ApplySelectionMarks(buffer, slot, state, sx, sy);
                bool shown = look.IsChild ? ChildSlotShown(slot, state, sx, sy) : AdultSlotShown(buffer, slot, state, sx, sy);
                if (!shown || slot.Id == AnimalOnlySlot)
                {
                    continue;
                }
            }
            else if (slot.Type == SlotType.Torso)
            {
                state.TorsoAnchor = new AnchorPoint(sx, sy);
                state.HeadAnchor = state.TorsoAnchor;
            }
            if (slot.Source == PartSource.Piece)
            {
                sprites.AppendPiece(buffer, slot.Id + state.Offset, sx, sy);
            }
            else
            {
                sprites.AppendSprite(buffer, slot.Id + state.Offset, sx, sy);
            }
            buffer.PaletteVariant = PaletteVariants.None;
        }
    }

    private void ApplySelectionMarks(PartBuffer buffer, PoseSlot slot, SlotState state, int sx, int sy)
    {
        VillagerAppearance look = state.Request.Appearance;
        if (slot.Type != SlotType.Shadow || !state.Request.MapSelectionMarks || state.Request.Portrait)
        {
            return;
        }
        if (look.InSelectedGroup)
        {
            sprites.AppendSprite(buffer, GroupMarkerSprite, sx, sy);
        }
        else if (look.Hovered)
        {
            state.Offset = look.Tribe == Tribe.Computer ? ComputerHighlightOffset : PlayerHighlightOffset;
        }
        if (look.InSelectionRectangle && look.Tribe == Tribe.Player)
        {
            state.Offset = PlayerHighlightOffset;
        }
    }

    private static bool ChildSlotShown(PoseSlot slot, SlotState state, int sx, int sy)
    {
        VillagerAppearance look = state.Request.Appearance;
        switch (slot.Type)
        {
            case SlotType.Headwear:
                return look.Sex != Sex.Male;
            case SlotType.Torso:
                state.TorsoAnchor = new AnchorPoint(sx, sy);
                if (look.Tribe == Tribe.Computer)
                {
                    state.Offset++;
                }
                return true;
            case SlotType.Head:
                state.HeadAnchor = new AnchorPoint(sx, sy);
                state.Offset = look.HeadStyle & 1;
                return true;
            case SlotType.ClubWeapon:
                return false;
            default:
                return true;
        }
    }

    private bool AdultSlotShown(PartBuffer buffer, PoseSlot slot, SlotState state, int sx, int sy)
    {
        VillagerAppearance look = state.Request.Appearance;
        if (look.Tribe == Tribe.Computer)
        {
            buffer.PaletteVariant = ComputerVariant;
        }
        switch (slot.Type)
        {
            case SlotType.Torso:
                state.TorsoAnchor = new AnchorPoint(sx, sy);
                int style = look.TorsoStyle;
                if (style > 1)
                {
                    style -= RecolouredTorsoStyles;
                    buffer.PaletteVariant = TorsoRecolourVariant;
                }
                state.Offset = (style * TorsoStyleStride)
                    + (look.Tribe == Tribe.Computer ? ComputerTorsoOffset : 0)
                    + ((int)look.Sex * FemaleTorsoOffset);
                return true;
            case SlotType.Head:
                state.HeadAnchor = new AnchorPoint(sx, sy);
                if (look.Tribe == Tribe.Computer)
                {
                    buffer.PaletteVariant = ComputerHeadVariant;
                }
                else if (look.HeadStyle > 0)
                {
                    buffer.PaletteVariant = look.HeadStyle;
                }
                state.Offset = HeadRow(look) * HeadRowStride;
                return true;
            case SlotType.Headwear:
                return HeadwearShown(look, state);
            default:
                return state.Category switch
                {
                    BodyAnimationCategory.WeaponBySlot => WeaponBySlotShown(slot.Type, look, state),
                    BodyAnimationCategory.Carrying => CarryingSlotShown(slot.Type, look, state),
                    BodyAnimationCategory.WeaponInClubSlot => ClubSlotShown(slot.Type, look, state),
                    _ => true,
                };
        }
    }

    private static int HeadRow(VillagerAppearance look)
    {
        if (look.Sex == Sex.Female)
        {
            return FemaleHeadRow;
        }
        if (look.Age < YoungAgeLimit)
        {
            return YoungHeadRow;
        }
        return look.Age < MiddleAgeLimit ? MiddleAgedHeadRow : OldHeadRow;
    }

    private bool HeadwearShown(VillagerAppearance look, SlotState state)
    {
        if (look.IsLeader)
        {
            state.Offset += LeaderHeadwearOffset;
            return true;
        }
        int taskClass = SpriteComposer.Lookup(catalog.Tables.TaskClassByTask, look.AssignedTask, "task");
        int headwear = SpriteComposer.Lookup(catalog.Tables.HeadwearByTaskClass, taskClass, "task class");
        if (headwear == 0)
        {
            return false;
        }
        state.Offset += headwear - 1;
        return true;
    }

    private bool WeaponBySlotShown(SlotType type, VillagerAppearance look, SlotState state)
    {
        if (type < SlotType.ClubWeapon || type > SlotType.SpearOrStone)
        {
            return true;
        }
        if (state.Request.Portrait && look.Weapon == VillagerAppearance.Stone)
        {
            return false;
        }
        if ((type == SlotType.ClubWeapon && look.Weapon == BoneHammer) || (type == SlotType.AxeWeapon && look.Weapon == BattleAxe))
        {
            state.Offset = AlternateWeaponOffset;
            return true;
        }
        return SpriteComposer.Lookup(catalog.Tables.WeaponSlotByWeapon, look.Weapon, "weapon") == (int)type;
    }

    private static bool CarryingSlotShown(SlotType type, VillagerAppearance look, SlotState state)
    {
        if (look.CarriedItem != 0)
        {
            bool loadShown = look.CarriedAmount > 0
                || (state.Animation >= FirstShownLoadAnimation && state.Animation <= LastShownLoadAnimation);
            if (type == SlotType.Load && loadShown)
            {
                state.Offset += look.CarriedItem;
                return true;
            }
            if (type == SlotType.ClubWeapon)
            {
                return AddWeapon(look, state, look.Weapon == VillagerAppearance.BareHands);
            }
            return type != SlotType.SpearOrStone;
        }
        switch (type)
        {
            case SlotType.Load:
                return false;
            case SlotType.ClubWeapon:
                return AddWeapon(look, state, look.Weapon == VillagerAppearance.Stone || look.Weapon == VillagerAppearance.BareHands);
            case SlotType.SpearOrStone:
                return look.Weapon == VillagerAppearance.Stone;
            default:
                return true;
        }
    }

    private static bool ClubSlotShown(SlotType type, VillagerAppearance look, SlotState state)
    {
        if (type != SlotType.ClubWeapon)
        {
            return true;
        }
        return AddWeapon(look, state, look.Weapon == VillagerAppearance.BareHands);
    }

    private static bool AddWeapon(VillagerAppearance look, SlotState state, bool hidden)
    {
        if (hidden)
        {
            return false;
        }
        state.Offset += look.Weapon;
        return true;
    }

    /// <summary>The working state of one body composition.</summary>
    private sealed class SlotState
    {
        internal SlotState(BodyRequest request)
        {
            Request = request;
        }

        internal BodyRequest Request { get; }

        internal int Animation { get; set; }

        internal BodyAnimationCategory Category { get; set; }

        internal int Offset { get; set; }

        internal AnchorPoint? TorsoAnchor { get; set; }

        internal AnchorPoint? HeadAnchor { get; set; }
    }
}
