using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Systems.Units.Movement;

/// <summary>The pixel offset tables of one gait axis (straight or diagonal steps).</summary>
internal enum GaitOffset
{
    /// <summary>Column offset along the step.</summary>
    StepX,

    /// <summary>Row offset along the step.</summary>
    StepY,

    /// <summary>Sideways column offset while swapping cells.</summary>
    SwapX,

    /// <summary>Sideways row offset while swapping cells.</summary>
    SwapY,
}

/// <summary>
/// The gaits of all speed classes laid out as the original keeps them: per step direction
/// (straight or diagonal) and offset, one table with the classes one after the other, a class
/// reading from its first frame on. A frame number past a class's own frames (a unit whose speed
/// changed in the middle of a step) reads on into the neighbouring class, as in the original;
/// beyond the whole table the offset is 0.
/// </summary>
internal sealed class GaitTables
{
    private readonly IReadOnlyList<SpeedClass> speeds;
    private readonly Dictionary<(bool Diagonal, GaitOffset Offset), List<int>> tables = new();
    private readonly int[][] starts = new int[2][];

    /// <summary>Lays out the gaits of <paramref name="speeds"/>.</summary>
    public GaitTables(IReadOnlyList<SpeedClass> speeds)
    {
        this.speeds = speeds ?? throw new ArgumentNullException(nameof(speeds));
        foreach (bool diagonal in new[] { false, true })
        {
            int[] classStarts = new int[speeds.Count];
            foreach (GaitOffset offset in Enum.GetValues<GaitOffset>())
            {
                var table = new List<int>();
                for (int s = 0; s < speeds.Count; s++)
                {
                    classStarts[s] = table.Count;
                    table.AddRange(Offsets(Gait(s, diagonal), offset));
                }
                tables[(diagonal, offset)] = table;
            }
            starts[diagonal ? 1 : 0] = classStarts;
        }
    }

    /// <summary>The gait of speed class <paramref name="speed"/> in straight or diagonal steps.</summary>
    public SpeedGait Gait(int speed, bool diagonal) => diagonal ? speeds[speed].Diagonal : speeds[speed].Straight;

    /// <summary>The offset of frame <paramref name="frame"/> (counted from the class's first) of a gait.</summary>
    public int Offset(int speed, bool diagonal, GaitOffset offset, int frame)
    {
        List<int> table = tables[(diagonal, offset)];
        int flat = starts[diagonal ? 1 : 0][speed] + frame;
        return flat >= 0 && flat < table.Count ? table[flat] : 0;
    }

    private static IReadOnlyList<int> Offsets(SpeedGait gait, GaitOffset offset) => offset switch
    {
        GaitOffset.StepX => gait.OffsetX,
        GaitOffset.StepY => gait.OffsetY,
        GaitOffset.SwapX => gait.SwapOffsetX,
        _ => gait.SwapOffsetY,
    };
}
