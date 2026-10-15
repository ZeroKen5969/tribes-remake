using System;
using System.Collections.Generic;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Composition;

/// <summary>
/// Turns pieces, composite sprites and effect animations into draw parts, the way the original
/// draws them: image pieces whose source rectangle does not fit the sheet are skipped, and the
/// selected palette variant swaps the sheets it covers.
/// </summary>
public sealed class SpriteComposer
{
    private readonly SpriteCatalog catalog;
    private readonly string[] sheetIdByNumber;
    private readonly Dictionary<string, SheetInfo> sheetsById = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Variant, int Sheet), string> substitutions = new();
    private readonly EffectFrame[] effectFrames;

    /// <summary>Creates a composer for <paramref name="catalog"/> (validated first).</summary>
    public SpriteComposer(SpriteCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        this.catalog = catalog;
        var spriteSheets = new List<string>();
        foreach (SheetInfo sheet in catalog.Sheets)
        {
            sheetsById[sheet.Id] = sheet;
            if (sheet.Pack == SheetPack.Sprites)
            {
                if (sheet.Index != spriteSheets.Count)
                {
                    throw new ArgumentException($"sprite sheet {sheet.Id} is out of order", nameof(catalog));
                }
                spriteSheets.Add(sheet.Id);
            }
        }
        sheetIdByNumber = spriteSheets.ToArray();
        foreach (PaletteVariant variant in catalog.PaletteVariants)
        {
            foreach (SheetSubstitution substitution in variant.Substitutions)
            {
                substitutions[(variant.Id, substitution.Sheet)] = substitution.Replacement;
            }
        }
        effectFrames = FlattenEffectFrames(catalog.Effects);
    }

    /// <summary>The catalog this composer draws from.</summary>
    public SpriteCatalog Catalog => catalog;

    /// <summary>The parts of composite sprite <paramref name="sprite"/> drawn at the anchor.</summary>
    /// <param name="sprite">Sprite number.</param>
    /// <param name="paletteVariant">Palette variant selected when drawing starts.</param>
    public DrawList ComposeSprite(int sprite, int paletteVariant = PaletteVariants.None) =>
        Run(paletteVariant, buffer => AppendSprite(buffer, sprite, 0, 0));

    /// <summary>The part of piece <paramref name="piece"/> drawn at the anchor (empty for piece 0).</summary>
    /// <param name="piece">Piece number.</param>
    /// <param name="paletteVariant">Palette variant selected when drawing starts.</param>
    public DrawList ComposePiece(int piece, int paletteVariant = PaletteVariants.None) =>
        Run(paletteVariant, buffer => AppendPiece(buffer, piece, 0, 0));

    /// <summary>
    /// The parts of effect <paramref name="effect"/> at <paramref name="tick"/>: frame
    /// <c>tick % frameCount</c> (a negative tick keeps a negative remainder, as in the original).
    /// </summary>
    /// <param name="effect">Effect number.</param>
    /// <param name="tick">Animation clock; most effects use the game frame counter.</param>
    /// <param name="paletteVariant">Palette variant selected when drawing starts.</param>
    public DrawList ComposeEffect(int effect, int tick, int paletteVariant = PaletteVariants.None) =>
        Run(paletteVariant, buffer => AppendEffect(buffer, effect, 0, 0, tick));

    /// <summary>The sprite a thrown <paramref name="weapon"/> shows in flight, or -1 if it is not a thrown weapon.</summary>
    /// <param name="weapon">Weapon number (unit field 65).</param>
    /// <param name="direction">Flight direction variant (unit field 76).</param>
    /// <param name="flightFrame">Frame of the flight.</param>
    public int ProjectileSprite(int weapon, int direction, int flightFrame)
    {
        foreach (ProjectileGraphic projectile in catalog.Projectiles)
        {
            if (projectile.Weapon == weapon)
            {
                int index = (direction * projectile.DirectionStride) + flightFrame;
                if (index < 0 || index >= projectile.FrameOffsets.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(flightFrame), "no such flight frame");
                }
                return projectile.FirstSprite + projectile.FrameOffsets[index];
            }
        }
        return -1;
    }

    /// <summary>Runs <paramref name="draw"/> into a fresh buffer and returns what it drew.</summary>
    internal static DrawList Run(int paletteVariant, Action<PartBuffer> draw)
    {
        var buffer = new PartBuffer(paletteVariant);
        try
        {
            draw(buffer);
            return buffer.ToDrawList(false);
        }
        catch (DrawingStoppedException)
        {
            return buffer.ToDrawList(true);
        }
    }

    internal void AppendEffect(PartBuffer buffer, int effect, int x, int y, int tick)
    {
        EffectAnimation animation = Lookup(catalog.Effects, effect, "effect");
        int index = animation.FirstFrame + (tick % animation.Frames.Count);
        EffectFrame frame = Lookup(effectFrames, index, "effect frame");
        AppendSprite(buffer, frame.Sprite, x + frame.X, y + frame.Y);
    }

    internal void AppendSprite(PartBuffer buffer, int sprite, int x, int y)
    {
        CompositeSprite composite = Lookup(catalog.Sprites, sprite, "sprite");
        foreach (SpritePart part in composite.Parts)
        {
            AppendPiece(buffer, part.Piece, x + part.X, y + part.Y);
        }
    }

    internal void AppendPiece(PartBuffer buffer, int pieceId, int x, int y)
    {
        if (pieceId == 0)
        {
            return;
        }
        Piece piece = Lookup(catalog.Pieces, pieceId, "piece");
        switch (piece.Shape)
        {
            case PieceShape.Image:
                AppendImage(buffer, piece, x, y);
                break;
            case PieceShape.Line:
                buffer.Add(DrawPart.Line(x + piece.X, y + piece.Y, x + (piece.EndX ?? piece.X), y + (piece.EndY ?? piece.Y),
                    ColorOf(piece), pieceId));
                break;
            case PieceShape.Outline:
                buffer.Add(DrawPart.Outline(x + piece.X, y + piece.Y, piece.Width, piece.Height, ColorOf(piece), pieceId));
                break;
            case PieceShape.Fill:
                int scale = piece.Scale ?? 1;
                buffer.Add(DrawPart.Fill(x + (piece.X * scale), y + (piece.Y * scale), piece.Width * scale, piece.Height * scale,
                    ColorOf(piece), pieceId));
                break;
            default:
                break;
        }
    }

    private void AppendImage(PartBuffer buffer, Piece piece, int x, int y)
    {
        int sheetNumber = piece.Sheet ?? throw new DrawingStoppedException($"image piece {piece.Id} has no sheet");
        string sheetId = Lookup(sheetIdByNumber, sheetNumber, "sheet");
        if (buffer.PaletteVariant != PaletteVariants.None
            && substitutions.TryGetValue((buffer.PaletteVariant, sheetNumber), out string? replacement))
        {
            sheetId = replacement;
        }
        SheetInfo sheet = sheetsById[sheetId];
        bool fits = piece.X >= 0 && piece.Y >= 0 && piece.Width > 0 && piece.Height > 0
            && piece.X + piece.Width <= sheet.Width && piece.Y + piece.Height <= sheet.Height;
        if (fits)
        {
            buffer.Add(DrawPart.Image(sheetId, piece.X, piece.Y, piece.Width, piece.Height, x, y, piece.Id));
        }
    }

    private RgbColor ColorOf(Piece piece) =>
        Lookup(catalog.PrimitiveColors, piece.Color ?? throw new DrawingStoppedException($"piece {piece.Id} has no colour"), "primitive colour");

    internal static T Lookup<T>(IReadOnlyList<T> items, int index, string what)
    {
        if (index < 0 || index >= items.Count)
        {
            throw new DrawingStoppedException($"{what} {index} does not exist");
        }
        return items[index];
    }

    private static EffectFrame[] FlattenEffectFrames(IReadOnlyList<EffectAnimation> effects)
    {
        var frames = new List<EffectFrame>();
        foreach (EffectAnimation effect in effects)
        {
            if (effect.FirstFrame != frames.Count || effect.Frames.Count == 0)
            {
                throw new ArgumentException($"effect {effect.Id} is not stored after the one before it", nameof(effects));
            }
            frames.AddRange(effect.Frames);
        }
        return frames.ToArray();
    }
}
