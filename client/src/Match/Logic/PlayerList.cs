using System;
using System.Collections.Generic;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// Which entries of the player table the in-match player list shows: the people first (players
/// and watchers, in table order), then the computer tribes, at most <see cref="MaxRows"/> rows; when
/// there are more, the last row counts the ones left out instead, so a world with dozens of
/// computer tribes still gets a short list in its corner.
/// </summary>
public static class PlayerList
{
    /// <summary>Most rows the list shows, the row that counts the others included.</summary>
    public const int MaxRows = 8;

    /// <summary>
    /// The entries to show (indices into <paramref name="isComputer"/>) and how many are left out
    /// (shown as one more row when not 0).
    /// </summary>
    public static (IReadOnlyList<int> Shown, int Left) Pick(IReadOnlyList<bool> isComputer, int maxRows = MaxRows)
    {
        ArgumentNullException.ThrowIfNull(isComputer);
        if (maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), maxRows, "the list shows at least one row");
        }
        var ordered = new List<int>(isComputer.Count);
        for (int i = 0; i < isComputer.Count; i++)
        {
            if (!isComputer[i])
            {
                ordered.Add(i);
            }
        }
        for (int i = 0; i < isComputer.Count; i++)
        {
            if (isComputer[i])
            {
                ordered.Add(i);
            }
        }
        if (ordered.Count <= maxRows)
        {
            return (ordered, 0);
        }
        // One row goes to the count of the others.
        int shown = maxRows - 1;
        return (ordered.GetRange(0, shown), ordered.Count - shown);
    }
}
