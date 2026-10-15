using System;
using System.Collections.Generic;
using Godot;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World;

/// <summary>
/// All sheets the world view draws from, packed into one texture (with the tribe-recoloured
/// sheets added when first needed), so a whole frame of the object layer draws with one texture
/// and the renderer can batch it. It starts large enough for the base sheets and every colour, so
/// it normally never grows (growing replaces the texture). A small white block serves fills, lines and outlines, which are
/// drawn as tinted pieces of it.
/// </summary>
internal sealed class SheetAtlas
{
    private const int Width = 1024;
    private const int StartHeight = 2048;
    private const int MaxHeight = 4096;
    private const int Padding = 2;
    private const int WhiteSize = 4;

    private readonly Dictionary<string, Rect2I> regions = new(StringComparer.Ordinal);
    private Image image;
    private ImageTexture? texture;
    private bool dirty;
    private int shelfX, shelfY, shelfHeight;

    public SheetAtlas()
    {
        image = Image.CreateEmpty(Width, StartHeight, false, Image.Format.Rgba8);
        Rect2I white = Allocate(WhiteSize, WhiteSize);
        image.FillRect(white, Colors.White);
        White = new Rect2(white.Position.X + 1, white.Position.Y + 1, 1, 1);
    }

    /// <summary>The source rectangle of one white pixel (inside a white block, so filtering never reaches its border).</summary>
    public Rect2 White { get; }

    /// <summary>The atlas texture, with every sheet added so far.</summary>
    public Texture2D Texture
    {
        get
        {
            if (texture == null || dirty)
            {
                if (texture != null && texture.GetSize() == new Vector2(image.GetWidth(), image.GetHeight()))
                {
                    texture.Update(image);
                }
                else
                {
                    texture = ImageTexture.CreateFromImage(image);
                }
                dirty = false;
            }
            return texture;
        }
    }

    /// <summary>Whether a sheet is in the atlas under <paramref name="key"/>.</summary>
    public bool TryGet(string key, out Rect2I region) => regions.TryGetValue(key, out region);

    /// <summary>Adds the pixels of <paramref name="sheet"/> under <paramref name="key"/> and returns where they are.</summary>
    public Rect2I Add(string key, RgbaImage sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (regions.TryGetValue(key, out Rect2I existing))
        {
            return existing;
        }
        Rect2I region = Allocate(sheet.Width, sheet.Height);
        image.BlitRect(WorldArt.ToImage(sheet), new Rect2I(0, 0, sheet.Width, sheet.Height), region.Position);
        regions[key] = region;
        dirty = true;
        return region;
    }

    /// <summary>Space for a width × height picture on the shelves, the atlas growing taller when full.</summary>
    private Rect2I Allocate(int width, int height)
    {
        if (shelfX + width + Padding > Width)
        {
            shelfY += shelfHeight + Padding;
            shelfX = 0;
            shelfHeight = 0;
        }
        while (shelfY + height + Padding > image.GetHeight())
        {
            int taller = image.GetHeight() * 2;
            if (taller > MaxHeight)
            {
                throw new InvalidOperationException("the world view's sheet atlas is full");
            }
            Image grown = Image.CreateEmpty(Width, taller, false, Image.Format.Rgba8);
            grown.BlitRect(image, new Rect2I(0, 0, Width, image.GetHeight()), Vector2I.Zero);
            image = grown;
        }
        var region = new Rect2I(shelfX + Padding, shelfY + Padding, width, height);
        shelfX += width + Padding;
        shelfHeight = Math.Max(shelfHeight, height);
        dirty = true;
        return region;
    }
}
