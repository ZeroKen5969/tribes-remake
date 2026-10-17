using System.Numerics;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Content;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// Dragging a building's ghost with a finger (<see cref="GhostDrag"/>): the cell the finger took
/// hold of stays under it, a ghost dragged out of the build menu starts above the finger, and
/// the view scrolls while the finger rests near an edge of the map part of the screen.
/// </summary>
public sealed class GhostDragTests
{
    [Fact]
    public void The_cell_taken_hold_of_stays_under_the_finger()
    {
        var origin = new GridPoint(10, 20);
        GridPoint hold = GhostDrag.Hold(origin, new GridPoint(11, 22));

        Assert.Equal(new GridPoint(10, 20), GhostDrag.Follow(new GridPoint(11, 22), hold));
        Assert.Equal(new GridPoint(15, 17), GhostDrag.Follow(new GridPoint(16, 19), hold));
        Assert.Equal(new GridPoint(-3, -5), GhostDrag.Follow(new GridPoint(-2, -3), hold));
    }

    [Fact]
    public void A_ghost_out_of_the_build_menu_starts_above_the_finger()
    {
        // A building three cells high, cells 16 map pixels high: its middle 32 pixels above the finger.
        Vector2 middle = GhostDrag.AboveFinger(new Vector2(100, 200), heightCells: 3, cellHeight: 16);

        Assert.Equal(new Vector2(100, 200 - 24 - 8), middle);
        Assert.True(middle.Y + (3 * 16 / 2f) < 200, "the ghost's bottom edge is above the finger");
    }

    [Fact]
    public void The_view_scrolls_towards_an_edge_the_finger_is_near()
    {
        Vector2 min = new(0, 30), max = new(700, 360);

        Assert.Equal(Vector2.Zero, GhostDrag.EdgeScroll(new Vector2(350, 200), min, max, band: 48, fastest: 360));
        Assert.Equal(new Vector2(-180, 0), GhostDrag.EdgeScroll(new Vector2(24, 200), min, max, 48, 360));
        Assert.Equal(new Vector2(360, 360), GhostDrag.EdgeScroll(new Vector2(720, 400), min, max, 48, 360));
        Assert.Equal(new Vector2(0, -360), GhostDrag.EdgeScroll(new Vector2(350, 10), min, max, 48, 360));
    }

    [Fact]
    public void An_area_too_small_for_the_edge_bands_does_not_scroll()
    {
        Assert.Equal(Vector2.Zero, GhostDrag.EdgeScroll(new Vector2(5, 5), Vector2.Zero, new Vector2(80, 80), band: 48, fastest: 360));
    }
}
