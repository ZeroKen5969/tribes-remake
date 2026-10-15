using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The opening grace of an endless world as the player sees it: no computer wave comes for a
/// people's tribe before the opening grace frame, nor for a newcomer during its own grace; the
/// time left is told in whole minutes, and the end is noticed once.
/// </summary>
public sealed class EndlessGraceTests
{
    private const int Opening = 7500, Newcomer = 3000;

    /// <summary>The game's tick (Tribes.Session's LocalMatchRunner.TickMilliseconds).</summary>
    private const int Tick = 120;

    [Fact]
    public void A_tribe_there_from_the_start_has_the_opening_grace()
    {
        Assert.Equal(Opening, EndlessGrace.EndFrame(Opening, Newcomer, arrivedAt: 0));
    }

    [Fact]
    public void A_newcomer_has_its_own_grace_but_never_less_than_the_opening()
    {
        Assert.Equal(12000 + Newcomer, EndlessGrace.EndFrame(Opening, Newcomer, arrivedAt: 12000));
        Assert.Equal(Opening, EndlessGrace.EndFrame(Opening, Newcomer, arrivedAt: 1000));
    }

    [Theory]
    [InlineData(0, 7500, 15)] // 7500 frames of 120 ms
    [InlineData(7000, 7500, 1)] // a minute or less: one
    [InlineData(7499, 7500, 1)]
    [InlineData(7500, 7500, 0)]
    [InlineData(9000, 7500, 0)]
    public void The_time_left_is_in_whole_minutes(int frame, int end, int minutes)
    {
        Assert.Equal(minutes, EndlessGrace.MinutesLeft(frame, end, Tick));
    }

    [Fact]
    public void A_longer_tick_makes_more_minutes()
    {
        Assert.Equal(30, EndlessGrace.MinutesLeft(0, 7500, 240));
    }

    [Fact]
    public void The_end_is_noticed_once()
    {
        Assert.False(EndlessGrace.EndedBetween(7000, 7499, 7500));
        Assert.True(EndlessGrace.EndedBetween(7499, 7500, 7500));
        Assert.True(EndlessGrace.EndedBetween(7400, 7600, 7500));
        Assert.False(EndlessGrace.EndedBetween(7500, 7600, 7500));
    }
}
