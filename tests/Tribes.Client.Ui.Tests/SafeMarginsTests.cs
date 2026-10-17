using System;
using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The room the interface keeps from the screen's edges: what camera cut-outs and rounded corners
/// need (so a button in a corner lies inside the rounding), in logical pixels, plus the player's
/// setting; a screen with neither keeps only the player's room.
/// </summary>
public sealed class SafeMarginsTests
{
    [Fact]
    public void A_plain_rounding_needs_about_three_tenths_of_its_radius()
    {
        float inset = SafeMargins.Inset(100, 100, 100);

        Assert.Equal(100 * (1 - (1 / MathF.Sqrt(2))), inset, 3);
        // The corner of a rectangle kept that far from both edges lies on the rounding.
        Assert.Equal(100, MathF.Sqrt(2 * (100 - inset) * (100 - inset)), 2);
    }

    [Fact]
    public void Square_corners_need_no_room()
    {
        Assert.Equal(0, SafeMargins.Inset(0, 0, 0));
        Assert.Equal(EdgeMargins.None, SafeMargins.ForCorners(1080, 2340, [ScreenCorner.Square, ScreenCorner.Square, ScreenCorner.Square, ScreenCorner.Square]));
    }

    [Fact]
    public void Each_edge_needs_the_room_of_the_corners_at_its_ends()
    {
        // Only the top corners rounded (a screen whose bottom corners are square).
        ScreenCorner[] corners = [new(60, 60, 60), new(60, 1020, 60), ScreenCorner.Square, ScreenCorner.Square];

        EdgeMargins margins = SafeMargins.ForCorners(1080, 2340, corners);

        float inset = SafeMargins.Inset(60, 60, 60);
        Assert.Equal(new EdgeMargins(inset, inset, inset, 0), margins);
    }

    [Fact]
    public void Corners_are_measured_from_the_window_edges_they_belong_to()
    {
        ScreenCorner[] corners = ScreenCorner.AllRounded(2340, 1080, 120);

        EdgeMargins margins = SafeMargins.ForCorners(2340, 1080, corners);

        float inset = SafeMargins.Inset(120, 120, 120);
        Assert.Equal(new EdgeMargins(inset, inset, inset, inset), margins);
    }

    [Fact]
    public void A_cut_out_keeps_its_side_clear()
    {
        // A camera hole on the left of a landscape phone: the safe area starts 80 pixels in.
        EdgeMargins margins = SafeMargins.OutsideSafeArea(2340, 1080, 80, 0, 2260, 1080);

        Assert.Equal(new EdgeMargins(80, 0, 0, 0), margins);
        Assert.Equal(EdgeMargins.None, SafeMargins.OutsideSafeArea(2340, 1080, 0, 0, 0, 0));
    }

    [Fact]
    public void The_room_is_in_logical_pixels_rounded_up_plus_the_players_room()
    {
        var cutouts = new EdgeMargins(80, 0, 0, 0);
        var corners = new EdgeMargins(30, 30, 31, 30);

        EdgeMargins margins = SafeMargins.Combine(cutouts, corners, pixelScale: 3, extra: 6);

        // 80 / 3 rounds up to 27; 30 / 3 is exactly 10; 31 / 3 rounds up to 11.
        Assert.Equal(new EdgeMargins(33, 16, 17, 16), margins);
    }

    [Fact]
    public void A_screen_without_cut_outs_or_rounding_keeps_only_the_players_room()
    {
        Assert.Equal(EdgeMargins.None, SafeMargins.Combine(EdgeMargins.None, EdgeMargins.None, 2, 0));
        Assert.Equal(new EdgeMargins(12, 12, 12, 12), SafeMargins.Combine(EdgeMargins.None, EdgeMargins.None, 2, 12));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 1)]
    [InlineData(13, 2)]
    [InlineData(24, 4)]
    [InlineData(99, 4)]
    public void A_saved_room_shows_as_the_nearest_choice(int pixels, int choice) =>
        Assert.Equal(choice, SafeMargins.ChoiceFor(pixels));
}
