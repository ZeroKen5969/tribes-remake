using System;

namespace Tribes.Core.Content;

/// <summary>A cell on the map grid; x grows to the right, y downwards.</summary>
public readonly record struct GridPoint(int X, int Y);

/// <summary>A size in map cells.</summary>
public readonly record struct GridSize(int Width, int Height)
{
    /// <summary>Whether <paramref name="point"/> lies inside a grid of this size.</summary>
    public bool Contains(GridPoint point) =>
        point.X >= 0 && point.Y >= 0 && point.X < Width && point.Y < Height;
}

/// <summary>
/// A rectangle of cells from (<see cref="X"/>, <see cref="Y"/>), which may be negative (the land of
/// an endless world grows in every direction); <see cref="Right"/> and <see cref="Bottom"/> are the
/// first column and row past it.
/// </summary>
public readonly record struct GridRect(int X, int Y, int Width, int Height)
{
    /// <summary>The first column.</summary>
    public int Left => X;

    /// <summary>The first row.</summary>
    public int Top => Y;

    /// <summary>The first column past the rectangle.</summary>
    public int Right => X + Width;

    /// <summary>The first row past the rectangle.</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether the rectangle holds no cell.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The rectangle from (<paramref name="left"/>, <paramref name="top"/>) up to, not including, (<paramref name="right"/>, <paramref name="bottom"/>).</summary>
    public static GridRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    /// <summary>Whether <paramref name="point"/> lies inside.</summary>
    public bool Contains(GridPoint point) =>
        point.X >= X && point.Y >= Y && point.X < Right && point.Y < Bottom;

    /// <summary>The smallest rectangle holding this one and <paramref name="other"/> (an empty one adds nothing).</summary>
    public GridRect Union(GridRect other)
    {
        if (other.IsEmpty)
        {
            return this;
        }
        if (IsEmpty)
        {
            return other;
        }
        return FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }
}
