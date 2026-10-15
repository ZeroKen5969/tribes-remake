using System;

namespace Tribes.Graphics.Imaging;

/// <summary>A 32-bit image in memory; each pixel is <c>0xAARRGGBB</c>.</summary>
public sealed class RgbaImage
{
    private const int OpaqueAlpha = unchecked((int)0xFF000000);
    private readonly int[] pixels;

    /// <summary>Creates a transparent image.</summary>
    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"image size {width}x{height} must be positive");
        }
        Width = width;
        Height = height;
        pixels = new int[width * height];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public int this[int x, int y]
    {
        get => pixels[Index(x, y)];
        set => pixels[Index(x, y)] = value;
    }

    /// <summary>Sets every pixel to <paramref name="argb"/>.</summary>
    public void Clear(int argb) => Array.Fill(pixels, argb);

    /// <summary>Sets every pixel to the opaque colour <paramref name="rgb"/> (<c>0xRRGGBB</c>).</summary>
    public void ClearOpaque(int rgb) => Clear(OpaqueAlpha | rgb);

    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is inside the image.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>The pixels, row by row, without copying.</summary>
    public ReadOnlySpan<int> Pixels => pixels;

    /// <summary>A copy of the pixels, row by row.</summary>
    public int[] ToArray() => (int[])pixels.Clone();

    private int Index(int x, int y)
    {
        if (!Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside {Width}x{Height}");
        }
        return (y * Width) + x;
    }
}
