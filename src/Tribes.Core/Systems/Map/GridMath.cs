namespace Tribes.Core.Systems.Map;

/// <summary>Small pieces of grid arithmetic the rules share.</summary>
internal static class GridMath
{
    /// <summary>
    /// <paramref name="value"/> kept within <paramref name="min"/>..<paramref name="max"/>: below
    /// <paramref name="min"/> gives <paramref name="min"/>, otherwise the smaller of the value and
    /// <paramref name="max"/> (so <paramref name="max"/> wins when the bounds cross, as in the original).
    /// </summary>
    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        return value < max ? value : max;
    }

    /// <summary>The squared straight-line distance between two cells.</summary>
    public static int SquaredDistance(int x1, int y1, int x2, int y2)
    {
        int dx = x1 - x2;
        int dy = y1 - y2;
        return (dx * dx) + (dy * dy);
    }
}
