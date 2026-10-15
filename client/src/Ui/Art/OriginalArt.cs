using System;
using System.Collections.Generic;
using Godot;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;

namespace Tribes.Client.Ui.Art;

/// <summary>
/// The original game's pictures for the interface: the sprite model (content/client/sprites.json,
/// read once) composed by Tribes.Graphics, and the sheet textures in res://assets/original. Draws
/// compositions on a <see cref="CanvasItem"/> or turns them into images for theme pieces.
/// </summary>
public sealed class OriginalArt
{
    private const string CatalogPath = "res://content/client/sprites.json";
    private const string AssetRoot = "res://assets/";

    private static OriginalArt? shared;

    private readonly Dictionary<string, SheetInfo> sheets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image> images = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (Texture2D Texture, Vector2I Origin)> spriteTextures = new();

    private OriginalArt(SpriteCatalog catalog)
    {
        Catalog = catalog;
        Composers = new Composers(catalog);
        foreach (SheetInfo sheet in catalog.Sheets)
        {
            sheets[sheet.Id] = sheet;
        }
    }

    /// <summary>The art of the running game, loaded on first use.</summary>
    public static OriginalArt Shared => shared ??= Load();

    /// <summary>The sprite model.</summary>
    public SpriteCatalog Catalog { get; }

    /// <summary>Composers for sprites, effects, bodies and objects.</summary>
    public Composers Composers { get; }

    private static OriginalArt Load()
    {
        string json = Godot.FileAccess.GetFileAsString(CatalogPath);
        if (json.Length == 0)
        {
            throw new InvalidOperationException($"cannot read {CatalogPath}: {Godot.FileAccess.GetOpenError()}");
        }
        return new OriginalArt(CatalogJson.Deserialize<SpriteCatalog>(json));
    }

    /// <summary>The texture of sheet <paramref name="id"/> (for example "pi0_12").</summary>
    public Texture2D Sheet(string id)
    {
        if (!textures.TryGetValue(id, out Texture2D? texture))
        {
            string path = AssetRoot + sheets[id].File;
            texture = GD.Load<Texture2D>(path) ?? throw new InvalidOperationException($"missing sheet {path}");
            textures[id] = texture;
        }
        return texture;
    }

    /// <summary>The pixels of sheet <paramref name="id"/> as RGBA8.</summary>
    public Image SheetImage(string id)
    {
        if (!images.TryGetValue(id, out Image? image))
        {
            image = Sheet(id).GetImage();
            if (image.IsCompressed())
            {
                image.Decompress();
            }
            if (image.GetFormat() != Image.Format.Rgba8)
            {
                image.Convert(Image.Format.Rgba8);
            }
            images[id] = image;
        }
        return image;
    }

    /// <summary>The colour of a <see cref="DrawPart"/> line, outline or fill.</summary>
    public static Color ToColor(RgbColor? color) =>
        color is { } c ? new Color(c.R / 255f, c.G / 255f, c.B / 255f) : Colors.Magenta;

    /// <summary>Draws <paramref name="parts"/> with their anchor at <paramref name="at"/>.</summary>
    public void Draw(CanvasItem target, IReadOnlyList<DrawPart> parts, Vector2 at, Color? modulate = null)
    {
        Color tint = modulate ?? Colors.White;
        foreach (DrawPart part in parts)
        {
            switch (part.Kind)
            {
                case DrawPartKind.Image:
                    target.DrawTextureRectRegion(Sheet(part.Sheet!),
                        new Rect2(at.X + part.X, at.Y + part.Y, part.Width, part.Height),
                        new Rect2(part.SourceX, part.SourceY, part.Width, part.Height), tint);
                    break;
                case DrawPartKind.Fill:
                    target.DrawRect(new Rect2(at.X + part.X, at.Y + part.Y, part.Width, part.Height), ToColor(part.Color) * tint);
                    break;
                case DrawPartKind.Outline:
                    DrawOutline(target, new Rect2I(part.X, part.Y, part.Width + 1, part.Height + 1), at, ToColor(part.Color) * tint);
                    break;
                case DrawPartKind.Line:
                    DrawPixelLine(target, part, at, ToColor(part.Color) * tint);
                    break;
            }
        }
    }

    /// <summary>Draws sprite <paramref name="sprite"/> with its anchor at <paramref name="at"/>.</summary>
    public void DrawSprite(CanvasItem target, int sprite, Vector2 at, Color? modulate = null) =>
        Draw(target, Composers.Sprites.ComposeSprite(sprite).Parts, at, modulate);

    /// <summary>Draws frame <paramref name="tick"/> of effect <paramref name="effect"/> at <paramref name="at"/>.</summary>
    public void DrawEffect(CanvasItem target, int effect, int tick, Vector2 at, Color? modulate = null) =>
        Draw(target, Composers.Sprites.ComposeEffect(effect, tick).Parts, at, modulate);

    /// <summary>The rectangle covered by <paramref name="parts"/>, relative to their anchor.</summary>
    public static Rect2I Bounds(IReadOnlyList<DrawPart> parts)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (DrawPart part in parts)
        {
            Rect2I r = PartRect(part);
            minX = Math.Min(minX, r.Position.X);
            minY = Math.Min(minY, r.Position.Y);
            maxX = Math.Max(maxX, r.End.X);
            maxY = Math.Max(maxY, r.End.Y);
        }
        return minX == int.MaxValue ? new Rect2I() : new Rect2I(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Paints <paramref name="parts"/> into a new transparent image just big enough for them;
    /// <paramref name="origin"/> is where their anchor lies in the image.
    /// </summary>
    public Image Rasterize(IReadOnlyList<DrawPart> parts, out Vector2I origin)
    {
        Rect2I bounds = Bounds(parts);
        origin = -bounds.Position;
        Image image = Image.CreateEmpty(Math.Max(1, bounds.Size.X), Math.Max(1, bounds.Size.Y), false, Image.Format.Rgba8);
        Paint(image, parts, origin);
        return image;
    }

    /// <summary>Paints <paramref name="parts"/> into <paramref name="image"/> with their anchor at <paramref name="at"/>.</summary>
    public void Paint(Image image, IReadOnlyList<DrawPart> parts, Vector2I at)
    {
        foreach (DrawPart part in parts)
        {
            switch (part.Kind)
            {
                case DrawPartKind.Image:
                    image.BlendRect(SheetImage(part.Sheet!), new Rect2I(part.SourceX, part.SourceY, part.Width, part.Height),
                        new Vector2I(at.X + part.X, at.Y + part.Y));
                    break;
                case DrawPartKind.Fill:
                    image.FillRect(new Rect2I(at.X + part.X, at.Y + part.Y, part.Width, part.Height).Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight())), ToColor(part.Color));
                    break;
                case DrawPartKind.Outline:
                    PixelPainter.Outline(image, new Rect2I(at.X + part.X, at.Y + part.Y, part.Width + 1, part.Height + 1), ToColor(part.Color));
                    break;
                case DrawPartKind.Line:
                    PixelPainter.Line(image, at.X + part.X, at.Y + part.Y, at.X + part.EndX, at.Y + part.EndY, ToColor(part.Color));
                    break;
            }
        }
    }

    /// <summary>Sprite <paramref name="sprite"/> as a texture of its own size; <paramref name="origin"/> is its anchor.</summary>
    public Texture2D SpriteTexture(int sprite, out Vector2I origin)
    {
        if (!spriteTextures.TryGetValue(sprite, out var entry))
        {
            Image image = Rasterize(Composers.Sprites.ComposeSprite(sprite).Parts, out Vector2I anchor);
            entry = (ImageTexture.CreateFromImage(image), anchor);
            spriteTextures[sprite] = entry;
        }
        origin = entry.Origin;
        return entry.Texture;
    }

    /// <summary>Sprite <paramref name="sprite"/> as a texture of its own size.</summary>
    public Texture2D SpriteTexture(int sprite) => SpriteTexture(sprite, out _);

    private static Rect2I PartRect(DrawPart part) => part.Kind switch
    {
        DrawPartKind.Line => new Rect2I(Math.Min(part.X, part.EndX), Math.Min(part.Y, part.EndY),
            Math.Abs(part.EndX - part.X) + 1, Math.Abs(part.EndY - part.Y) + 1),
        DrawPartKind.Outline => new Rect2I(part.X, part.Y, part.Width + 1, part.Height + 1),
        _ => new Rect2I(part.X, part.Y, part.Width, part.Height),
    };

    private static void DrawOutline(CanvasItem target, Rect2I r, Vector2 at, Color color)
    {
        Vector2 p = at + r.Position;
        target.DrawRect(new Rect2(p.X, p.Y, r.Size.X, 1), color);
        target.DrawRect(new Rect2(p.X, p.Y + r.Size.Y - 1, r.Size.X, 1), color);
        target.DrawRect(new Rect2(p.X, p.Y, 1, r.Size.Y), color);
        target.DrawRect(new Rect2(p.X + r.Size.X - 1, p.Y, 1, r.Size.Y), color);
    }

    /// <summary>Lines are drawn as one-pixel squares so they keep the pixel grid at any scale.</summary>
    private static void DrawPixelLine(CanvasItem target, DrawPart part, Vector2 at, Color color)
    {
        if (part.X == part.EndX || part.Y == part.EndY)
        {
            Rect2I r = PartRect(part);
            target.DrawRect(new Rect2(at + r.Position, r.Size), color);
            return;
        }
        foreach (Vector2I p in PixelPainter.LinePoints(part.X, part.Y, part.EndX, part.EndY))
        {
            target.DrawRect(new Rect2(at.X + p.X, at.Y + p.Y, 1, 1), color);
        }
    }
}
