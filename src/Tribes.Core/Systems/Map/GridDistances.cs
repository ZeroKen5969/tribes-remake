using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Systems.Map;

/// <summary>
/// The rules' measure of how far a cell is: the movement table's eight-way estimate, which also
/// gives the facing towards the cell. Values are 10 * distance + facing.
/// </summary>
internal sealed class GridDistances
{
    /// <summary>How many cells the table reaches in each direction.</summary>
    public const int Reach = 12;

    /// <summary>What <see cref="Measure"/> gives for a cell beyond the table.</summary>
    public const int OutOfReach = 200;

    /// <summary>A value's distance part is the value divided by this.</summary>
    public const int FacingDigits = 10;

    private readonly IReadOnlyList<IReadOnlyList<int>> table;

    /// <summary>Uses the distance table of <paramref name="movement"/>.</summary>
    public GridDistances(MovementTables movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        table = movement.DistanceAndFacing;
    }

    /// <summary>10 * distance + facing towards the cell (dx, dy) away, or <see cref="OutOfReach"/> beyond the table.</summary>
    public int Measure(int dx, int dy) =>
        InReach(dx) && InReach(dy) ? table[dy + Reach][dx + Reach] : OutOfReach;

    /// <summary>
    /// Like <see cref="Measure"/>, but an offset beyond the table reads the table's first row or
    /// column instead (the near-side edge), as the passability and target searches look it up.
    /// </summary>
    public int MeasureAtEdge(int dx, int dy) => table[EdgeIndex(dy)][EdgeIndex(dx)];

    /// <summary>The distance part of a measured value, in cells.</summary>
    public static int Cells(int measured) => measured / FacingDigits;

    /// <summary>The facing part of a measured value.</summary>
    public static int Facing(int measured) => measured % FacingDigits;

    private static bool InReach(int delta) => delta >= -Reach && delta <= Reach;

    private static int EdgeIndex(int delta) => InReach(delta) ? delta + Reach : 0;
}
