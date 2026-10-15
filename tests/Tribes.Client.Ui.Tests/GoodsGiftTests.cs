using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// Giving goods to an ally: at least one, at most what the giver has and what still fits in the
/// ally's store (the rules keep the rest with the giver); nothing when either side has no room.
/// </summary>
public sealed class GoodsGiftTests
{
    [Theory]
    [InlineData(40, 100, 999, 40)] // all of it fits
    [InlineData(40, 980, 999, 19)] // the ally's store is nearly full
    [InlineData(40, 999, 999, 0)] // full
    [InlineData(0, 10, 999, 0)] // nothing to give
    [InlineData(-3, 10, 999, 0)]
    [InlineData(40, 1200, 999, 0)] // already over the cap
    public void The_most_worth_giving(int have, int allyHas, int cap, int most)
    {
        Assert.Equal(most, GoodsGift.Most(have, allyHas, cap));
    }

    [Theory]
    [InlineData(10, 40, 10)]
    [InlineData(0, 40, 1)]
    [InlineData(55, 40, 40)]
    [InlineData(5, 0, 0)]
    public void An_amount_is_kept_between_one_and_the_most(int asked, int most, int amount)
    {
        Assert.Equal(amount, GoodsGift.Clamp(asked, most));
    }

    [Fact]
    public void A_first_amount_is_a_handful()
    {
        Assert.Equal(10, GoodsGift.FirstAmount(40));
        Assert.Equal(7, GoodsGift.FirstAmount(7));
        Assert.Equal(0, GoodsGift.FirstAmount(0));
    }
}
