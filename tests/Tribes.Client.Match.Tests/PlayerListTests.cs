using System;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>The in-match player list: people first, then computers, at most a few rows with a count of the rest.</summary>
public sealed class PlayerListTests
{
    [Fact]
    public void People_come_before_the_computers_in_table_order()
    {
        bool[] computer = { true, false, true, false };

        (var shown, int left) = PlayerList.Pick(computer);

        Assert.Equal(new[] { 1, 3, 0, 2 }, shown);
        Assert.Equal(0, left);
    }

    [Fact]
    public void A_long_table_keeps_the_people_and_counts_the_rest_on_the_last_row()
    {
        bool[] computer = new[] { false, false }.Concat(Enumerable.Repeat(true, 40)).ToArray();

        (var shown, int left) = PlayerList.Pick(computer);

        Assert.Equal(PlayerList.MaxRows - 1, shown.Count);
        Assert.Equal(new[] { 0, 1 }, shown.Take(2));
        Assert.Equal(computer.Length - shown.Count, left);
    }

    [Fact]
    public void A_table_that_fits_exactly_needs_no_count()
    {
        bool[] computer = Enumerable.Repeat(false, PlayerList.MaxRows).ToArray();

        (var shown, int left) = PlayerList.Pick(computer);

        Assert.Equal(PlayerList.MaxRows, shown.Count);
        Assert.Equal(0, left);
    }

    [Fact]
    public void At_least_one_row()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerList.Pick(new[] { false }, 0));
        Assert.Equal((0, 0), (PlayerList.Pick(Array.Empty<bool>()).Shown.Count, PlayerList.Pick(Array.Empty<bool>()).Left));
    }
}
