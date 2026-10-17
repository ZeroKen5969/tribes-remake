using System;
using System.Numerics;
using Tribes.Core.Content;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// Dragging the ghost of a building with a finger: the ghost keeps the cell the finger took hold
/// of under the finger; one dragged out of the build menu starts just above the finger, so the
/// finger does not hide it; and while the finger rests near an edge of the map part of the
/// screen (or over the bars beyond it) the view scrolls that way, faster the closer it gets.
/// </summary>
public static class GhostDrag
{
    /// <summary>How the finger at <paramref name="fingerCell"/> holds a ghost whose top-left cell is <paramref name="origin"/>.</summary>
    public static GridPoint Hold(GridPoint origin, GridPoint fingerCell) => new(fingerCell.X - origin.X, fingerCell.Y - origin.Y);

    /// <summary>The ghost's top-left cell with the finger at <paramref name="fingerCell"/> holding it as <paramref name="hold"/>.</summary>
    public static GridPoint Follow(GridPoint fingerCell, GridPoint hold) => new(fingerCell.X - hold.X, fingerCell.Y - hold.Y);

    /// <summary>
    /// The map point the middle of a ghost <paramref name="heightCells"/> cells high goes to when it
    /// comes out of the build menu under the finger at map point <paramref name="finger"/>: its
    /// bottom half a cell above the finger.
    /// </summary>
    public static Vector2 AboveFinger(Vector2 finger, int heightCells, int cellHeight) =>
        new(finger.X, finger.Y - (Math.Max(1, heightCells) * cellHeight / 2f) - (cellHeight / 2f));

    /// <summary>
    /// How fast (screen pixels a second, positive to the right and down) the view scrolls with the
    /// finger at <paramref name="finger"/>: within <paramref name="band"/> of an edge of the area
    /// from <paramref name="areaMin"/> to <paramref name="areaMax"/> it scrolls towards that edge,
    /// up to <paramref name="fastest"/> at the edge and beyond it.
    /// </summary>
    public static Vector2 EdgeScroll(Vector2 finger, Vector2 areaMin, Vector2 areaMax, float band, float fastest) =>
        new(Axis(finger.X, areaMin.X, areaMax.X, band, fastest), Axis(finger.Y, areaMin.Y, areaMax.Y, band, fastest));

    private static float Axis(float at, float min, float max, float band, float fastest)
    {
        if (band <= 0 || max - min < 2 * band)
        {
            return 0;
        }
        if (at < min + band)
        {
            return -fastest * Math.Clamp((min + band - at) / band, 0, 1);
        }
        if (at > max - band)
        {
            return fastest * Math.Clamp((at - (max - band)) / band, 0, 1);
        }
        return 0;
    }
}
