using System;
using System.Collections.Generic;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World.Scene;

/// <summary>
/// Draws parts into a plain pixel buffer (ARGB, row by row) with the rules and results of
/// <see cref="PartRasterizer"/>, several times faster: rows are clipped once and the sheets are
/// read from flat arrays. The terrain blocks of the world view are drawn with it. A part whose
/// source lies outside its sheet fails before drawing anything (the slow one draws up to it).
/// </summary>
public sealed class FastRaster
{
    private const int OpaqueAlpha = 255;
    private readonly Func<string, RgbaImage> sheets;
    private readonly Dictionary<string, (int[] Pixels, int Width, int Height)> flat = new(StringComparer.Ordinal);

    /// <summary>A rasterizer finding sheets by id with <paramref name="sheets"/>.</summary>
    public FastRaster(Func<string, RgbaImage> sheets)
    {
        this.sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
    }

    /// <summary>A buffer of <paramref name="width"/> x <paramref name="height"/> pixels to draw into.</summary>
    public sealed class Target
    {
        /// <summary>A buffer of the given size.</summary>
        public Target(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new int[width * height];
        }

        /// <summary>Width in pixels.</summary>
        public int Width { get; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; }

        /// <summary>The pixels, ARGB, row by row.</summary>
        public int[] Pixels { get; }

        /// <summary>Sets every pixel to opaque <paramref name="rgb"/>.</summary>
        public void ClearOpaque(int rgb) => Array.Fill(Pixels, unchecked((int)0xFF000000) | rgb);
    }

    /// <summary>Draws <paramref name="parts"/> with their anchor at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    public void Draw(Target target, IReadOnlyList<DrawPart> parts, int anchorX, int anchorY)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parts);
        for (int i = 0; i < parts.Count; i++)
        {
            Draw(target, parts[i], anchorX, anchorY);
        }
    }

    /// <summary>Draws one part with its anchor at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    public void Draw(Target target, DrawPart part, int anchorX, int anchorY)
    {
        int x = anchorX + part.X;
        int y = anchorY + part.Y;
        int color = (part.Color?.Value ?? 0) | unchecked((int)0xFF000000);
        switch (part.Kind)
        {
            case DrawPartKind.Image:
                Blit(target, Sheet(part.Sheet ?? throw new ArgumentException("image part without a sheet", nameof(part))),
                    part.SourceX, part.SourceY, part.Width, part.Height, x, y, part.FlipX);
                break;
            case DrawPartKind.Fill:
                FillRect(target, x, y, part.Width, part.Height, color);
                break;
            case DrawPartKind.Outline:
                Outline(target, x, y, part.Width, part.Height, color);
                break;
            case DrawPartKind.Line:
                Line(target, x, y, anchorX + part.EndX, anchorY + part.EndY, color);
                break;
            default:
                break;
        }
    }

    /// <summary>Fills a rectangle (clipped); nothing when the size is not positive.</summary>
    public static void FillRect(Target target, int x, int y, int width, int height, int argb)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(target.Width, x + width), y1 = Math.Min(target.Height, y + height);
        if (x1 <= x0)
        {
            return;
        }
        for (int py = y0; py < y1; py++)
        {
            target.Pixels.AsSpan((py * target.Width) + x0, x1 - x0).Fill(argb);
        }
    }

    private (int[] Pixels, int Width, int Height) Sheet(string id)
    {
        if (!flat.TryGetValue(id, out var sheet))
        {
            RgbaImage image = sheets(id);
            sheet = (image.ToArray(), image.Width, image.Height);
            flat[id] = sheet;
        }
        return sheet;
    }

    private static void Outline(Target target, int x, int y, int width, int height, int argb)
    {
        if (width < 0 || height < 0)
        {
            return;
        }
        if (width == 0 || height == 0)
        {
            Line(target, x, y, x + width, y + height, argb);
            return;
        }
        FillRect(target, x, y, width + 1, 1, argb);
        FillRect(target, x, y + height, width + 1, 1, argb);
        FillRect(target, x, y + 1, 1, height - 1, argb);
        FillRect(target, x + width, y + 1, 1, height - 1, argb);
    }

    private static void Line(Target target, int x1, int y1, int x2, int y2, int argb)
    {
        if (y1 == y2)
        {
            FillRect(target, Math.Min(x1, x2), y1, Math.Abs(x2 - x1) + 1, 1, argb);
            return;
        }
        if (x1 == x2)
        {
            FillRect(target, x1, Math.Min(y1, y2), 1, Math.Abs(y2 - y1) + 1, argb);
            return;
        }
        int dx = Math.Abs(x2 - x1);
        int dy = -Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1;
        int sy = y1 < y2 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            FillRect(target, x1, y1, 1, 1, argb);
            if (x1 == x2 && y1 == y2)
            {
                return;
            }
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x1 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y1 += sy;
            }
        }
    }

    private static void Blit(Target target, (int[] Pixels, int Width, int Height) sheet, int sx, int sy, int width, int height, int x, int y, bool flip)
    {
        if (sx < 0 || sy < 0 || sx + width > sheet.Width || sy + height > sheet.Height)
        {
            if (width > 0 && height > 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sx), $"({sx}, {sy}) {width}x{height} is outside the {sheet.Width}x{sheet.Height} sheet");
            }
            return;
        }
        int c0 = Math.Max(0, -x), c1 = Math.Min(width, target.Width - x);
        int r0 = Math.Max(0, -y), r1 = Math.Min(height, target.Height - y);
        if (c1 <= c0 || r1 <= r0)
        {
            return;
        }
        int[] destination = target.Pixels;
        int[] source = sheet.Pixels;
        for (int row = r0; row < r1; row++)
        {
            int sourceRow = (sy + row) * sheet.Width;
            int targetRow = ((y + row) * target.Width) + x;
            for (int column = c0; column < c1; column++)
            {
                int pixel = source[sourceRow + sx + (flip ? width - 1 - column : column)];
                int alpha = (pixel >> 24) & 0xFF;
                if (alpha == OpaqueAlpha)
                {
                    destination[targetRow + column] = pixel;
                }
                else if (alpha != 0)
                {
                    destination[targetRow + column] = Blend(pixel, destination[targetRow + column], alpha);
                }
            }
        }
    }

    private static int Blend(int source, int destination, int alpha)
    {
        int inverse = OpaqueAlpha - alpha;
        int r = Mix((source >> 16) & 0xFF, (destination >> 16) & 0xFF, alpha, inverse);
        int g = Mix((source >> 8) & 0xFF, (destination >> 8) & 0xFF, alpha, inverse);
        int b = Mix(source & 0xFF, destination & 0xFF, alpha, inverse);
        int destinationAlpha = (destination >> 24) & 0xFF;
        int a = alpha + ((destinationAlpha * inverse) / OpaqueAlpha);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int Mix(int source, int destination, int alpha, int inverse) =>
        ((source * alpha) + (destination * inverse) + (OpaqueAlpha / 2)) / OpaqueAlpha;
}
