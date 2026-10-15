using System;
using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>Size of a map cell in pixels.</summary>
/// <param name="Width">Cell width (22 in the original).</param>
/// <param name="Height">Cell height (16 in the original).</param>
public readonly record struct CellSize(int Width, int Height);

/// <summary>
/// The original game's complete sprite and animation model (<c>data/client/sprites.json</c>).
/// Every list is indexed by the entries' ids (entry <c>i</c> has id <c>i</c>).
/// </summary>
/// <param name="Format">Format name and version of the file.</param>
/// <param name="Cell">Map cell size.</param>
/// <param name="Sheets">Sprite sheets (pack pi0) followed by their recoloured variants (pack pi9).</param>
/// <param name="PaletteVariants">Sheet substitutions by palette variant number.</param>
/// <param name="PrimitiveColors">Colours of the line, outline and fill pieces.</param>
/// <param name="Pieces">All pieces.</param>
/// <param name="Sprites">All composite sprites, flattened to pieces.</param>
/// <param name="Effects">All effect animations.</param>
/// <param name="BodyAnimations">All villager and animal animations.</param>
/// <param name="DirectionMaps">Facing to direction tables, one per direction count.</param>
/// <param name="Poses">All poses.</param>
/// <param name="ObjectGraphics">Building, resource node, trap and terrain object graphics.</param>
/// <param name="ObjectGraphicFirstParts">
/// For each object graphic, the position of its first part in the original's single part table
/// (the parts of all graphics are stored one after the other). Some rules address parts by that
/// position and can reach into the next graphic.
/// </param>
/// <param name="Projectiles">Thrown weapons in flight.</param>
/// <param name="Tables">Data tables used by the appearance and object rules.</param>
public sealed record SpriteCatalog(
    string Format,
    CellSize Cell,
    IReadOnlyList<SheetInfo> Sheets,
    IReadOnlyList<PaletteVariant> PaletteVariants,
    IReadOnlyList<RgbColor> PrimitiveColors,
    IReadOnlyList<Piece> Pieces,
    IReadOnlyList<CompositeSprite> Sprites,
    IReadOnlyList<EffectAnimation> Effects,
    IReadOnlyList<BodyAnimation> BodyAnimations,
    IReadOnlyList<DirectionMap> DirectionMaps,
    IReadOnlyList<Pose> Poses,
    IReadOnlyList<ObjectGraphic> ObjectGraphics,
    IReadOnlyList<int> ObjectGraphicFirstParts,
    IReadOnlyList<ProjectileGraphic> Projectiles,
    GraphicsTables Tables)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-sprites/1";

    /// <summary>
    /// Checks that every list is indexed by id and that cross references are in range; throws
    /// <see cref="InvalidOperationException"/> describing the first problem found.
    /// </summary>
    public void Validate()
    {
        CheckIds(Pieces, p => p.Id, nameof(Pieces));
        CheckIds(Sprites, s => s.Id, nameof(Sprites));
        CheckIds(Effects, e => e.Id, nameof(Effects));
        CheckIds(BodyAnimations, a => a.Id, nameof(BodyAnimations));
        CheckIds(Poses, p => p.Id, nameof(Poses));
        CheckIds(ObjectGraphics, o => o.Id, nameof(ObjectGraphics));
        if (ObjectGraphicFirstParts.Count != ObjectGraphics.Count)
        {
            throw new InvalidOperationException("one first-part position is needed per object graphic");
        }
        foreach (CompositeSprite sprite in Sprites)
        {
            foreach (SpritePart part in sprite.Parts)
            {
                CheckRange(part.Piece, Pieces.Count, $"sprite {sprite.Id} piece");
            }
        }
        foreach (EffectAnimation effect in Effects)
        {
            foreach (EffectFrame frame in effect.Frames)
            {
                CheckRange(frame.Sprite, Sprites.Count, $"effect {effect.Id} sprite");
            }
        }
        foreach (BodyAnimation animation in BodyAnimations)
        {
            if (animation.Frames.Count != animation.DirectionCount * animation.FramesPerDirection)
            {
                throw new InvalidOperationException($"animation {animation.Id}: frame count does not match its directions");
            }
            foreach (BodyFrame frame in animation.Frames)
            {
                CheckRange(frame.Pose, Poses.Count, $"animation {animation.Id} pose");
            }
        }
    }

    private static void CheckIds<T>(IReadOnlyList<T> items, Func<T, int> id, string name)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (id(items[i]) != i)
            {
                throw new InvalidOperationException($"{name}[{i}] has id {id(items[i])}");
            }
        }
    }

    private static void CheckRange(int value, int count, string what)
    {
        if (value < 0 || value >= count)
        {
            throw new InvalidOperationException($"{what} {value} is outside 0..{count - 1}");
        }
    }
}
