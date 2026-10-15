using System;
using System.Collections.Generic;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>A tribe went out of the match: wiped out, or its player gave up (<paramref name="Forfeited"/>).</summary>
/// <param name="Tribe">The tribe.</param>
/// <param name="Forfeited">Whether its player gave up.</param>
public sealed record TribeOut(int Tribe, bool Forfeited) : GameEvent;

/// <summary>
/// The match is decided: no two tribes still in it are enemies. <paramref name="Winners"/> are the
/// tribes still in (none when every tribe went out at once). The match ends a short while later.
/// </summary>
/// <param name="Winners">The tribes that won, in tribe order.</param>
public sealed record MatchDecided(IReadOnlyList<int> Winners) : GameEvent;

/// <summary>The match is over (its result is shown); <paramref name="Winners"/> as in <see cref="MatchDecided"/>.</summary>
/// <param name="Winners">The tribes that won, in tribe order.</param>
public sealed record MatchEnded(IReadOnlyList<int> Winners) : GameEvent;

/// <summary>How a tribe stands in a match.</summary>
public enum TribeOutcome
{
    /// <summary>Still in, the match goes on.</summary>
    Playing,

    /// <summary>Still in when the match was decided.</summary>
    Won,

    /// <summary>Wiped out: no living adult and no standing building.</summary>
    Defeated,

    /// <summary>Its player gave up.</summary>
    Forfeited,
}

/// <summary>A tribe's standing.</summary>
/// <param name="Tribe">The tribe.</param>
/// <param name="Team">Its team, or <see cref="TribeState.NoTeam"/>.</param>
/// <param name="Controller">Who plays it.</param>
/// <param name="Outcome">How it stands.</param>
/// <param name="Frame">The frame it went out (defeated, forfeited) or the match was decided (won); -1 while playing.</param>
public sealed record TribeResult(int Tribe, int Team, TribeController Controller, TribeOutcome Outcome, int Frame);

/// <summary>The standing of a match.</summary>
/// <param name="IsDecided">Whether the match is decided.</param>
/// <param name="Winners">The tribes that won (empty while it goes on, or when every tribe went out at once).</param>
/// <param name="WinningTeam">The team every winner is on, or -1 (no winner, or winners of no common team).</param>
/// <param name="Tribes">Every tribe's standing, in tribe order.</param>
public sealed record MatchResult(bool IsDecided, IReadOnlyList<int> Winners, int WinningTeam, IReadOnlyList<TribeResult> Tribes);

/// <summary>Reading a match's standing from its world.</summary>
public static class MatchStandings
{
    /// <summary>Whether <paramref name="tribe"/> is out of the match (wiped out or forfeited; see <see cref="TribeStanding"/>).</summary>
    public static bool IsDefeated(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].OutAtFrame >= 0;
    }

    /// <summary>The standing of the match of <paramref name="world"/>.</summary>
    public static MatchResult Of(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        bool decided = world.Match.IsDecided;
        var tribes = new List<TribeResult>(world.Tribes.Count);
        var winners = new List<int>();
        foreach (TribeState tribe in world.Tribes)
        {
            TribeOutcome outcome = tribe.OutAtFrame >= 0
                ? (tribe.Controller.Kind == TribeControl.Forfeited ? TribeOutcome.Forfeited : TribeOutcome.Defeated)
                : decided ? TribeOutcome.Won : TribeOutcome.Playing;
            int frame = outcome switch
            {
                TribeOutcome.Won => world.Match.DecidedAtFrame,
                TribeOutcome.Playing => -1,
                _ => tribe.OutAtFrame,
            };
            tribes.Add(new TribeResult(tribe.Index, tribe.Team, tribe.Controller, outcome, frame));
            if (outcome == TribeOutcome.Won)
            {
                winners.Add(tribe.Index);
            }
        }
        return new MatchResult(decided, winners, CommonTeam(world, winners), tribes);
    }

    /// <summary>The tribes still in the match of <paramref name="world"/>, in tribe order.</summary>
    internal static List<int> StillIn(GameWorld world)
    {
        var inMatch = new List<int>();
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.OutAtFrame < 0)
            {
                inMatch.Add(tribe.Index);
            }
        }
        return inMatch;
    }

    private static int CommonTeam(GameWorld world, List<int> winners)
    {
        if (winners.Count == 0)
        {
            return -1;
        }
        int team = world.Tribes[winners[0]].Team;
        foreach (int tribe in winners)
        {
            if (world.Tribes[tribe].Team != team)
            {
                return -1;
            }
        }
        return team;
    }
}
