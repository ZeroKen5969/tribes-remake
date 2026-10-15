using System;
using System.Collections.Generic;
using Godot;

namespace Tribes.Client.Ui.Art;

/// <summary>
/// Draws interface pieces pixel by pixel into images, in the style of the original boxes: a
/// one-pixel outline with cut corners, a lit top-left edge, a shaded bottom-right edge and an
/// optional drop shadow.
/// </summary>
public static class PixelPainter
{
    /// <summary>How a box is shaded.</summary>
    /// <param name="Outline">Outline colour, or transparent for none.</param>
    /// <param name="Fill">Inside colour.</param>
    /// <param name="Light">Inner top and left edge.</param>
    /// <param name="Shade">Inner bottom and right edge.</param>
    /// <param name="Shadow">Colour of the row under the box (drop shadow), or transparent.</param>
    public sealed record BoxStyle(Color Outline, Color Fill, Color Light, Color Shade, Color Shadow);

    /// <summary>A new transparent image.</summary>
    public static Image Blank(int width, int height) =>
        Image.CreateEmpty(Math.Max(1, width), Math.Max(1, height), false, Image.Format.Rgba8);

    /// <summary>Sets one pixel if it lies inside the image.</summary>
    public static void Pixel(Image image, int x, int y, Color color)
    {
        if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight())
        {
            image.SetPixel(x, y, color);
        }
    }

    /// <summary>Fills a rectangle, clipped to the image.</summary>
    public static void Fill(Image image, Rect2I rect, Color color)
    {
        Rect2I clipped = rect.Intersection(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()));
        if (clipped.Size.X > 0 && clipped.Size.Y > 0)
        {
            image.FillRect(clipped, color);
        }
    }

    /// <summary>A one-pixel rectangle outline covering <paramref name="rect"/>.</summary>
    public static void Outline(Image image, Rect2I rect, Color color)
    {
        Fill(image, new Rect2I(rect.Position.X, rect.Position.Y, rect.Size.X, 1), color);
        Fill(image, new Rect2I(rect.Position.X, rect.End.Y - 1, rect.Size.X, 1), color);
        Fill(image, new Rect2I(rect.Position.X, rect.Position.Y, 1, rect.Size.Y), color);
        Fill(image, new Rect2I(rect.End.X - 1, rect.Position.Y, 1, rect.Size.Y), color);
    }

    /// <summary>A one-pixel line, both ends included.</summary>
    public static void Line(Image image, int x0, int y0, int x1, int y1, Color color)
    {
        foreach (Vector2I p in LinePoints(x0, y0, x1, y1))
        {
            Pixel(image, p.X, p.Y, color);
        }
    }

    /// <summary>The pixels of a line (Bresenham), both ends included.</summary>
    public static IEnumerable<Vector2I> LinePoints(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            yield return new Vector2I(x0, y0);
            if (x0 == x1 && y0 == y1)
            {
                yield break;
            }
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>
    /// A box filling <paramref name="rect"/>: outline with the corner pixels left out, lit and
    /// shaded inner edges, and the drop shadow on the row below <paramref name="rect"/>.
    /// </summary>
    public static void Box(Image image, Rect2I rect, BoxStyle style)
    {
        int x0 = rect.Position.X, y0 = rect.Position.Y, x1 = rect.End.X - 1, y1 = rect.End.Y - 1;
        bool outlined = style.Outline.A > 0;
        int inset = outlined ? 1 : 0;
        Fill(image, new Rect2I(x0 + inset, y0 + inset, rect.Size.X - 2 * inset, rect.Size.Y - 2 * inset), style.Fill);
        // Lit top and left, shaded bottom and right, just inside the outline.
        Fill(image, new Rect2I(x0 + inset, y0 + inset, rect.Size.X - 2 * inset, 1), style.Light);
        Fill(image, new Rect2I(x0 + inset, y0 + inset, 1, rect.Size.Y - 2 * inset), style.Light);
        Fill(image, new Rect2I(x0 + inset, y1 - inset, rect.Size.X - 2 * inset, 1), style.Shade);
        Fill(image, new Rect2I(x1 - inset, y0 + inset + 1, 1, rect.Size.Y - 2 * inset - 1), style.Shade);
        if (outlined)
        {
            Fill(image, new Rect2I(x0 + 1, y0, rect.Size.X - 2, 1), style.Outline);
            Fill(image, new Rect2I(x0 + 1, y1, rect.Size.X - 2, 1), style.Outline);
            Fill(image, new Rect2I(x0, y0 + 1, 1, rect.Size.Y - 2), style.Outline);
            Fill(image, new Rect2I(x1, y0 + 1, 1, rect.Size.Y - 2), style.Outline);
            // Round the inner corners a little, as the original slots do.
            Pixel(image, x0 + 1, y0 + 1, style.Outline);
            Pixel(image, x1 - 1, y0 + 1, style.Outline);
            Pixel(image, x0 + 1, y1 - 1, style.Outline);
            Pixel(image, x1 - 1, y1 - 1, style.Outline);
        }
        if (style.Shadow.A > 0)
        {
            Fill(image, new Rect2I(x0 + 2, y1 + 1, rect.Size.X - 3, 1), style.Shadow);
            Pixel(image, x1 + 1, y1, style.Shadow);
        }
    }

    /// <summary>A solid triangle pointing down, <paramref name="width"/> pixels wide at the top, with an outline.</summary>
    public static Image DownArrow(int width, Color fill, Color outline)
    {
        int height = (width + 1) / 2;
        Image image = Blank(width + 2, height + 2);
        for (int row = 0; row < height; row++)
        {
            int left = 1 + row, right = width - row;
            Fill(image, new Rect2I(left, 1 + row, Math.Max(1, right - left + 1), 1), fill);
            Pixel(image, left - 1, 1 + row, outline);
            Pixel(image, right + 1, 1 + row, outline);
        }
        Fill(image, new Rect2I(0, 0, width + 2, 1), outline);
        Pixel(image, (width + 1) / 2, height + 1, outline);
        if (width % 2 == 0)
        {
            Pixel(image, width / 2 + 1, height + 1, outline);
        }
        return image;
    }

    /// <summary>A copy of <paramref name="image"/> with every pixel multiplied by <paramref name="tint"/>.</summary>
    public static Image Tinted(Image image, Color tint)
    {
        Image copy = (Image)image.Duplicate();
        for (int y = 0; y < copy.GetHeight(); y++)
        {
            for (int x = 0; x < copy.GetWidth(); x++)
            {
                copy.SetPixel(x, y, copy.GetPixel(x, y) * tint);
            }
        }
        return copy;
    }

    /// <summary>A copy of <paramref name="image"/> in shades of <paramref name="tone"/> (for disabled pieces).</summary>
    public static Image Faded(Image image, Color tone)
    {
        Image copy = (Image)image.Duplicate();
        for (int y = 0; y < copy.GetHeight(); y++)
        {
            for (int x = 0; x < copy.GetWidth(); x++)
            {
                Color c = copy.GetPixel(x, y);
                if (c.A > 0)
                {
                    float v = 0.55f + 0.45f * c.Luminance;
                    copy.SetPixel(x, y, new Color(tone.R * v, tone.G * v, tone.B * v, c.A));
                }
            }
        }
        return copy;
    }
}
