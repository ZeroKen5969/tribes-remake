using System;

namespace Tribes.Client.World.Scene;

/// <summary>
/// A rectangle of map cells: columns <see cref="X"/> to <see cref="Right"/> - 1 and rows
/// <see cref="Y"/> to <see cref="Bottom"/> - 1. Cells of an endless map can be negative.
/// </summary>
/// <param name="X">Left column.</param>
/// <param name="Y">Top row.</param>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
public readonly record struct CellRect(int X, int Y, int Width, int Height)
{
    /// <summary>No cells.</summary>
    public static readonly CellRect Empty = new(0, 0, 0, 0);

    /// <summary>Left column.</summary>
    public int Left => X;

    /// <summary>Top row.</summary>
    public int Top => Y;

    /// <summary>The column after the last one.</summary>
    public int Right => X + Width;

    /// <summary>The row after the last one.</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether the rectangle has no cells.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The rectangle from <paramref name="left"/>, <paramref name="top"/> to (not including) <paramref name="right"/>, <paramref name="bottom"/>.</summary>
    public static CellRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    /// <summary>Whether cell (<paramref name="x"/>, <paramref name="y"/>) is inside.</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    /// <summary>The smallest rectangle holding both (an empty one adds nothing).</summary>
    public CellRect Union(CellRect other)
    {
        if (other.IsEmpty)
        {
            return this;
        }
        if (IsEmpty)
        {
            return other;
        }
        return FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>The cells in both (empty when they do not overlap).</summary>
    public CellRect Intersect(CellRect other)
    {
        CellRect overlap = FromEdges(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return overlap.IsEmpty ? Empty : overlap;
    }

    /// <summary>The rectangle grown by <paramref name="cells"/> on every side (an empty one stays empty).</summary>
    public CellRect Inflate(int cells) => IsEmpty ? Empty : FromEdges(Left - cells, Top - cells, Right + cells, Bottom + cells);
}
