using Tribes.Core.Maps;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>A point in map pixels.</summary>
/// <param name="X">Pixels from the left of the map.</param>
/// <param name="Y">Pixels from the top of the map.</param>
public readonly record struct MapPixel(int X, int Y);

/// <summary>
/// Where the four corner marks of the selected building go: on the outer corners of its footprint,
/// as the original draws them (the top-left mark at the footprint's top-left pixel, the others one
/// footprint width and height further), so they frame every cell of it at any size. Another
/// tribe's building looked at is framed the same way.
/// </summary>
public static class BuildingFrame
{
    /// <summary>
    /// The building slot <paramref name="selection"/> frames: the player's selected building, or
    /// another tribe's building being looked at; 0 for none.
    /// </summary>
    public static int MarkedBuilding(UnitSelection selection)
    {
        System.ArgumentNullException.ThrowIfNull(selection);
        return selection.Kind switch
        {
            SelectionKind.Building => selection.Building,
            SelectionKind.Other => System.Math.Max(0, CellCode.BuildingOf(selection.OtherCode)),
            _ => 0,
        };
    }

    /// <summary>
    /// The anchors of the top-left, top-right, bottom-left and bottom-right marks of a footprint of
    /// <paramref name="width"/> by <paramref name="height"/> cells from cell (<paramref name="x"/>,
    /// <paramref name="y"/>), with cells of <paramref name="cellWidth"/> by <paramref name="cellHeight"/> pixels.
    /// </summary>
    public static (MapPixel TopLeft, MapPixel TopRight, MapPixel BottomLeft, MapPixel BottomRight) Corners(
        int x, int y, int width, int height, int cellWidth, int cellHeight)
    {
        int left = x * cellWidth, top = y * cellHeight;
        int right = (x + width) * cellWidth, bottom = (y + height) * cellHeight;
        return (new MapPixel(left, top), new MapPixel(right, top), new MapPixel(left, bottom), new MapPixel(right, bottom));
    }
}
