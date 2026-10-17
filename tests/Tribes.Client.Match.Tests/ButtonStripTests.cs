using Tribes.Client.MatchPlay.Logic;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// The big buttons always fit along their edge (<see cref="ButtonStrip"/>): at full size when
/// there is room, a little smaller down to the smallest a finger presses reliably, then in more
/// lines. A phone in landscape (360 logical pixels high) with the eight buttons of an endless
/// world was the case that ran off the screen.
/// </summary>
public sealed class ButtonStripTests
{
    private const int Gap = 3;

    [Fact]
    public void Seven_buttons_of_a_phone_fit_at_full_size()
    {
        StripFit fit = ButtonStrip.Fit(7, 354, preferred: 48, smallest: 41, Gap);

        Assert.Equal(new StripFit(1, 7, 48), fit);
        Assert.True((7 * fit.Button) + (6 * Gap) <= 354);
    }

    [Fact]
    public void Eight_buttons_get_a_little_smaller_in_one_line()
    {
        StripFit fit = ButtonStrip.Fit(8, 354, preferred: 48, smallest: 41, Gap);

        Assert.Equal(1, fit.Lines);
        Assert.InRange(fit.Button, 41, 47);
        Assert.True((8 * fit.Button) + (7 * Gap) <= 354);
    }

    [Fact]
    public void Too_short_an_edge_takes_two_lines_at_full_size()
    {
        // The same buttons with the room kept from rounded corners: one line would need 38-pixel buttons.
        StripFit fit = ButtonStrip.Fit(8, 326, preferred: 48, smallest: 41, Gap);

        Assert.Equal(new StripFit(2, 4, 48), fit);
        Assert.Equal((2 * 48) + Gap, fit.Thickness(Gap));
    }

    [Fact]
    public void The_lines_never_run_past_the_edge()
    {
        for (int count = 1; count <= 11; count++)
        {
            for (int length = 60; length <= 800; length += 7)
            {
                StripFit fit = ButtonStrip.Fit(count, length, preferred: 48, smallest: 30, Gap);

                Assert.True((fit.PerLine * fit.Button) + ((fit.PerLine - 1) * Gap) <= length, $"{count} buttons along {length}");
                Assert.True(fit.Lines * fit.PerLine >= count);
                Assert.InRange(fit.Button, 1, 48);
            }
        }
    }

    [Fact]
    public void No_buttons_need_no_room()
    {
        Assert.Equal(new StripFit(1, 0, 48), ButtonStrip.Fit(0, 300, 48, 41, Gap));
    }
}
