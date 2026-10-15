namespace Tribes.Client.Ui.Logic;

/// <summary>What happened in an endless world (the rules' events about tribes and alliances).</summary>
public enum EndlessNews
{
    /// <summary>A tribe got a camp (a: the tribe).</summary>
    TribeAppeared,

    /// <summary>a invited b into an alliance.</summary>
    Invited,

    /// <summary>b turned down a's invitation.</summary>
    Declined,

    /// <summary>a's invitation to b lapsed.</summary>
    Lapsed,

    /// <summary>a and b became allies.</summary>
    Formed,

    /// <summary>a and b are no longer allies; by left, or went out (wiped out or gave up).</summary>
    Ended,

    /// <summary>a gave b goods.</summary>
    Transferred,
}

/// <summary>A message for the player: its text key and the tribes named in it, in order.</summary>
/// <param name="Key">The text key (its {0}, {1} are the tribes' names; transfers add the amount and the goods after them).</param>
/// <param name="Tribes">The tribes named.</param>
public readonly record struct NewsLine(string Key, int[] Tribes);

/// <summary>
/// The message an endless world's news gives the player of <c>local</c> (-1 for a watcher):
/// invitations, answers and transfers go to the two tribes concerned, worded for each side; new
/// tribes and alliances made or ended are news for everyone (an alliance also ends when one side
/// goes out); a player hears nothing of their own new camp, nor of the alliance their own tribe's
/// end ended.
/// </summary>
public static class EndlessNewsLines
{
    /// <summary>
    /// The message for <paramref name="local"/>, or null when the news is not for them;
    /// <paramref name="byIsOut"/>: the alliance ended because <paramref name="by"/> went out.
    /// </summary>
    public static NewsLine? For(EndlessNews news, int a, int b, int by, int local, bool byIsOut = false)
    {
        bool mine = local >= 0 && a == local, theirs = local >= 0 && b == local;
        if (news == EndlessNews.Ended && byIsOut)
        {
            int other = by == a ? b : a;
            return by == local ? null
                : mine || theirs ? Line("endless.news.ally_out", by)
                : Line("endless.news.alliance_ended_out", by, other);
        }
        return news switch
        {
            EndlessNews.TribeAppeared => mine ? null : Line("endless.news.tribe_appeared", a),
            EndlessNews.Invited => mine ? Line("endless.news.you_invited", b) : theirs ? Line("endless.news.invited_you", a) : null,
            EndlessNews.Declined => mine ? Line("endless.news.declined_yours", b) : theirs ? Line("endless.news.you_declined", a) : null,
            EndlessNews.Lapsed => mine ? Line("endless.news.yours_lapsed", b) : theirs ? Line("endless.news.theirs_lapsed", a) : null,
            EndlessNews.Formed => mine ? Line("endless.news.allied_with_you", b)
                : theirs ? Line("endless.news.allied_with_you", a)
                : Line("endless.news.allied", a, b),
            EndlessNews.Ended => mine || theirs
                ? (by == local ? Line("endless.news.you_left", mine ? b : a) : Line("endless.news.left_you", mine ? b : a))
                : Line("endless.news.alliance_ended", a, b),
            EndlessNews.Transferred => mine ? Line("endless.news.you_gave", b) : theirs ? Line("endless.news.gave_you", a) : null,
            _ => null,
        };
    }

    private static NewsLine Line(string key, params int[] tribes) => new(key, tribes);
}
