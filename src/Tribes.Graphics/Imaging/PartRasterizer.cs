using System;
using System.Collections.Generic;
using Tribes.Graphics.Composition;

namespace Tribes.Graphics.Imaging;

/// <summary>
/// Draws composed parts into an <see cref="RgbaImage"/> with the original's drawing rules: images
/// are blended over the target (the original's sheets only have fully opaque or fully transparent
/// pixels), lines are one-pixel Bresenham lines with both ends drawn, outlines cover one pixel
/// more than their size, and everything is clipped to the image.
/// </summary>
public sealed class PartRasterizer
{
    private const int OpaqueAlpha = 255;
    private const int AlphaShift = 24;
    private const int RedShift = 16;
    private const int GreenShift = 8;
    private const int ChannelMask = 0xFF;
    private readonly Func<string, RgbaImage> sheets;

    /// <summary>Creates a rasterizer that finds sheets by id with <paramref name="sheets"/>.</summary>
    public PartRasterizer(Func<string, RgbaImage> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        this.sheets = sheets;
    }

    /// <summary>Draws <paramref name="parts"/> with their anchor at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    public void Draw(RgbaImage target, IEnumerable<DrawPart> parts, int anchorX, int anchorY)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parts);
        foreach (DrawPart part in parts)
        {
            Draw(target, part, anchorX, anchorY);
        }
    }

    /// <summary>Draws one part with its anchor at (<paramref name="anchorX"/>, <paramref name="anchorY"/>).</summary>
    public void Draw(RgbaImage target, DrawPart part, int anchorX, int anchorY)
    {
        ArgumentNullException.ThrowIfNull(target);
        int x = anchorX + part.X;
        int y = anchorY + part.Y;
        int color = (part.Color?.Value ?? 0) | unchecked((int)0xFF000000);
        switch (part.Kind)
        {
            case DrawPartKind.Image:
                Blit(target, sheets(part.Sheet ?? throw new ArgumentException("image part without a sheet", nameof(part))),
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

    /// <summary>Fills a rectangle; nothing when the size is not positive.</summary>
    public static void FillRect(RgbaImage target, int x, int y, int width, int height, int argb)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (width <= 0 || height <= 0)
        {
            return;
        }
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(target.Width, x + width);
        int y1 = Math.Min(target.Height, y + height);
        for (int py = y0; py < y1; py++)
        {
            for (int px = x0; px < x1; px++)
            {
                target[px, py] = argb;
            }
        }
    }

    /// <summary>A rectangle outline covering <c>width + 1</c> by <c>height + 1</c> pixels; a zero size draws a line.</summary>
    public static void Outline(RgbaImage target, int x, int y, int width, int height, int argb)
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

    /// <summary>A one-pixel line with both end points drawn.</summary>
    public static void Line(RgbaImage target, int x1, int y1, int x2, int y2, int argb)
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

    private static void Blit(RgbaImage target, RgbaImage sheet, int sx, int sy, int width, int height, int x, int y, bool flip)
    {
        for (int row = 0; row < height; row++)
        {
            int ty = y + row;
            if (ty < 0 || ty >= target.Height)
            {
                continue;
            }
            for (int column = 0; column < width; column++)
            {
                int tx = x + column;
                if (tx < 0 || tx >= target.Width)
                {
                    continue;
                }
                int source = sheet[sx + (flip ? width - 1 - column : column), sy + row];
                int alpha = (source >> AlphaShift) & ChannelMask;
                if (alpha == OpaqueAlpha)
                {
                    target[tx, ty] = source;
                }
                else if (alpha != 0)
                {
                    target[tx, ty] = Blend(source, target[tx, ty], alpha);
                }
            }
        }
    }

    private static int Blend(int source, int destination, int alpha)
    {
        int inverse = OpaqueAlpha - alpha;
        int r = Mix((source >> RedShift) & ChannelMask, (destination >> RedShift) & ChannelMask, alpha, inverse);
        int g = Mix((source >> GreenShift) & ChannelMask, (destination >> GreenShift) & ChannelMask, alpha, inverse);
        int b = Mix(source & ChannelMask, destination & ChannelMask, alpha, inverse);
        int destinationAlpha = (destination >> AlphaShift) & ChannelMask;
        int a = alpha + ((destinationAlpha * inverse) / OpaqueAlpha);
        return (a << AlphaShift) | (r << RedShift) | (g << GreenShift) | b;
    }

    private static int Mix(int source, int destination, int alpha, int inverse) =>
        ((source * alpha) + (destination * inverse) + (OpaqueAlpha / 2)) / OpaqueAlpha;
}
