using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// Which message an endless world's news gives this player: invitations and transfers only to
/// the two tribes concerned (worded for each side), new tribes and alliances to everyone, nothing
/// about one's own new camp.
/// </summary>
public sealed class EndlessNewsTests
{
    private const int Me = 2, Ally = 5, Other = 7;

    private static (string Key, string Tribes)? For(EndlessNews news, int a, int b, int by, int local)
    {
        NewsLine? line = EndlessNewsLines.For(news, a, b, by, local);
        return line is { } l ? (l.Key, string.Join(",", l.Tribes)) : null;
    }

    private static string T(params int[] tribes) => string.Join(",", tribes);

    [Fact]
    public void A_new_tribe_is_news_for_everyone_else()
    {
        Assert.Equal(("endless.news.tribe_appeared", T(Other)), For(EndlessNews.TribeAppeared, Other, -1, -1, Me)!.Value);
        Assert.Null(For(EndlessNews.TribeAppeared, Me, -1, -1, Me));
    }

    [Fact]
    public void An_invitation_is_told_to_both_sides()
    {
        Assert.Equal(("endless.news.invited_you", T(Ally)), For(EndlessNews.Invited, Ally, Me, -1, Me)!.Value);
        Assert.Equal(("endless.news.you_invited", T(Ally)), For(EndlessNews.Invited, Me, Ally, -1, Me)!.Value);
        Assert.Null(For(EndlessNews.Invited, Ally, Other, -1, Me));
    }

    [Fact]
    public void A_declined_or_lapsed_invitation_is_told_to_both_sides()
    {
        Assert.Equal(("endless.news.declined_yours", T(Ally)), For(EndlessNews.Declined, Me, Ally, -1, Me)!.Value);
        Assert.Equal(("endless.news.you_declined", T(Ally)), For(EndlessNews.Declined, Ally, Me, -1, Me)!.Value);
        Assert.Equal(("endless.news.yours_lapsed", T(Ally)), For(EndlessNews.Lapsed, Me, Ally, -1, Me)!.Value);
        Assert.Equal(("endless.news.theirs_lapsed", T(Ally)), For(EndlessNews.Lapsed, Ally, Me, -1, Me)!.Value);
        Assert.Null(For(EndlessNews.Declined, Ally, Other, -1, Me));
        Assert.Null(For(EndlessNews.Lapsed, Ally, Other, -1, Me));
    }

    [Fact]
    public void An_alliance_is_news_for_everyone()
    {
        Assert.Equal(("endless.news.allied_with_you", T(Ally)), For(EndlessNews.Formed, Me, Ally, -1, Me)!.Value);
        Assert.Equal(("endless.news.allied_with_you", T(Ally)), For(EndlessNews.Formed, Ally, Me, -1, Me)!.Value);
        Assert.Equal(("endless.news.allied", T(Ally, Other)), For(EndlessNews.Formed, Ally, Other, -1, Me)!.Value);
    }

    [Fact]
    public void An_ended_alliance_says_who_left()
    {
        Assert.Equal(("endless.news.you_left", T(Ally)), For(EndlessNews.Ended, Me, Ally, Me, Me)!.Value);
        Assert.Equal(("endless.news.left_you", T(Ally)), For(EndlessNews.Ended, Ally, Me, Ally, Me)!.Value);
        Assert.Equal(("endless.news.left_you", T(Ally)), For(EndlessNews.Ended, Me, Ally, Ally, Me)!.Value);
        Assert.Equal(("endless.news.alliance_ended", T(Ally, Other)), For(EndlessNews.Ended, Ally, Other, Other, Me)!.Value);
    }

    [Fact]
    public void An_alliance_ended_by_a_tribe_going_out_says_so()
    {
        Assert.Equal(("endless.news.ally_out", T(Ally)), ForOut(EndlessNews.Ended, Me, Ally, Ally, Me)!.Value);
        Assert.Equal(("endless.news.ally_out", T(Ally)), ForOut(EndlessNews.Ended, Ally, Me, Ally, Me)!.Value);
        Assert.Equal(("endless.news.alliance_ended_out", T(Other, Ally)), ForOut(EndlessNews.Ended, Ally, Other, Other, Me)!.Value);
        // A player whose own tribe went out learns that from the match itself.
        Assert.Null(ForOut(EndlessNews.Ended, Me, Ally, Me, Me));
    }

    private static (string Key, string Tribes)? ForOut(EndlessNews news, int a, int b, int by, int local)
    {
        NewsLine? line = EndlessNewsLines.For(news, a, b, by, local, byIsOut: true);
        return line is { } l ? (l.Key, string.Join(",", l.Tribes)) : null;
    }

    [Fact]
    public void Goods_are_told_to_the_giver_and_the_receiver()
    {
        Assert.Equal(("endless.news.you_gave", T(Ally)), For(EndlessNews.Transferred, Me, Ally, -1, Me)!.Value);
        Assert.Equal(("endless.news.gave_you", T(Ally)), For(EndlessNews.Transferred, Ally, Me, -1, Me)!.Value);
        Assert.Null(For(EndlessNews.Transferred, Ally, Other, -1, Me));
    }

    [Fact]
    public void A_watcher_gets_only_the_public_news()
    {
        Assert.NotNull(For(EndlessNews.TribeAppeared, Other, -1, -1, -1));
        Assert.NotNull(For(EndlessNews.Formed, Ally, Other, -1, -1));
        Assert.NotNull(For(EndlessNews.Ended, Ally, Other, Ally, -1));
        Assert.Null(For(EndlessNews.Invited, Ally, Other, -1, -1));
        Assert.Null(For(EndlessNews.Transferred, Ally, Other, -1, -1));
    }
}
