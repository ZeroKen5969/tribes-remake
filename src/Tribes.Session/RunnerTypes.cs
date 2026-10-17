using System;
using System.Collections.Generic;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;

namespace Tribes.Session;

/// <summary>Where a match runner is.</summary>
public enum MatchPhase
{
    /// <summary>A resumed hosted match waits (paused) for its players to come back; the host continues it.</summary>
    WaitingForPlayers,

    /// <summary>The match runs.</summary>
    Running,

    /// <summary>The result is decided; the world still runs, so players can look around.</summary>
    Over,

    /// <summary>The runner stopped (see <see cref="IMatchRunner.StopInfo"/>); nothing runs any more.</summary>
    Stopped,
}

/// <summary>What the local player is in the match.</summary>
public enum LocalRole
{
    /// <summary>Plays a tribe.</summary>
    Player,

    /// <summary>Gave up; watches.</summary>
    Forfeited,

    /// <summary>The tribe was wiped out; watches.</summary>
    Out,

    /// <summary>Watches without a tribe.</summary>
    Spectator,
}

/// <summary>Why a runner stopped.</summary>
public enum StopReason
{
    /// <summary>The local player quit (the host saved first).</summary>
    Quit,

    /// <summary>The host left or closed the match; the host can resume it from the save.</summary>
    HostLeft,

    /// <summary>The connection to the host was lost and could not be restored.</summary>
    ConnectionLost,

    /// <summary>The host removed the local player.</summary>
    Kicked,

    /// <summary>The peers' states differed (see <see cref="MatchStop.Desync"/>); the match was stopped.</summary>
    Desync,

    /// <summary>Something else went wrong (see <see cref="MatchStop.Message"/>).</summary>
    Error,

    /// <summary>The game goes on in another runner (a game played alone was opened to the network).</summary>
    HandedOver,

    /// <summary>The world grew too large for the host to send it to this player again.</summary>
    WorldTooLarge,
}

/// <summary>A difference between the host's state and a peer's.</summary>
/// <param name="Tick">The first compared tick whose hashes differ.</param>
/// <param name="LastMatchingTick">The last compared tick whose hashes matched, or -1.</param>
/// <param name="PlayerId">The peer whose state differs from the host's.</param>
/// <param name="ExpectedHash">The host's hash.</param>
/// <param name="ReportedHash">The peer's hash.</param>
public sealed record DesyncInfo(long Tick, long LastMatchingTick, Guid PlayerId, uint ExpectedHash, uint ReportedHash)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"tick {Tick} (last matching {LastMatchingTick}) player {PlayerId}: host 0x{ExpectedHash:x8}, peer 0x{ReportedHash:x8}";
}

/// <summary>Why and where a runner stopped.</summary>
/// <param name="Reason">Why.</param>
/// <param name="Tick">The match tick when it stopped.</param>
/// <param name="Message">Details for the log (English).</param>
/// <param name="Desync">For <see cref="StopReason.Desync"/>, the hashes.</param>
public sealed record MatchStop(StopReason Reason, long Tick, string Message, DesyncInfo? Desync = null);

/// <summary>Things the players should be told about.</summary>
public enum MatchNoticeKind
{
    /// <summary>A player joined the running match.</summary>
    PlayerJoined,

    /// <summary>A player disconnected or left (「離線」).</summary>
    PlayerAbsent,

    /// <summary>A player came back.</summary>
    PlayerReturned,

    /// <summary>A player gave up.</summary>
    PlayerForfeited,

    /// <summary>A player's tribe was wiped out.</summary>
    PlayerOut,

    /// <summary>The connection dropped; reconnecting.</summary>
    Reconnecting,

    /// <summary>Another player hosts the match now.</summary>
    HostChanged,

    /// <summary>The host saved the match automatically.</summary>
    Autosaved,

    /// <summary>Saving failed.</summary>
    SaveFailed,

    /// <summary>The host turned down a pause or speed request.</summary>
    RequestDenied,

    /// <summary>An order was not sent or not accepted.</summary>
    OrderDropped,

    /// <summary>A save asked for with <see cref="IMatchRunner.Save"/> was written.</summary>
    Saved,

    /// <summary>
    /// The world is too large to send to a player (named in <see cref="MatchNotice.Detail"/>), who
    /// could not join or come back, or was let go when the state had to be sent again.
    /// </summary>
    WorldTooLarge,
}

/// <summary>A notice for the players.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Tick">The match tick.</param>
/// <param name="Player">The participant concerned, if any.</param>
/// <param name="Detail">Details for the log (English), or empty.</param>
public sealed record MatchNotice(MatchNoticeKind Kind, long Tick, PlayerEntry? Player = null, string Detail = "");

/// <summary>A tick the runner ran.</summary>
public sealed class MatchTickEventArgs : EventArgs
{
    /// <summary>What the rules did.</summary>
    public required TickResult Result { get; init; }

    /// <summary>
    /// For each outcome in <see cref="TickResult.Outcomes"/>, the local order it answers, or null
    /// for other players' orders.
    /// </summary>
    public required IReadOnlyList<GameOrder?> LocalOrders { get; init; }

    /// <summary>Player table changes of the tick.</summary>
    public required IReadOnlyList<PlayerChange> Changes { get; init; }
}

/// <summary>A notice was raised.</summary>
public sealed class MatchNoticeEventArgs : EventArgs
{
    /// <summary>The notice.</summary>
    public required MatchNotice Notice { get; init; }
}

/// <summary>The match result was decided.</summary>
public sealed class MatchOverEventArgs : EventArgs
{
    /// <summary>The result.</summary>
    public required MatchOutcome Outcome { get; init; }

    /// <summary>Whether the local player's tribe won (false for spectators).</summary>
    public required bool LocalVictory { get; init; }
}

/// <summary>The runner stopped.</summary>
public sealed class MatchStoppedEventArgs : EventArgs
{
    /// <summary>Why.</summary>
    public required MatchStop Stop { get; init; }
}

/// <summary>What became of a submitted order.</summary>
public enum OrderSubmitResult
{
    /// <summary>Queued; it runs on a coming tick (the outcome arrives with that tick).</summary>
    Queued,

    /// <summary>The local player has no tribe to order now (spectator, absent, gave up, wiped out, match stopped).</summary>
    NotAPlayer,

    /// <summary>The order is for another tribe than the local player's.</summary>
    WrongTribe,

    /// <summary>The order cannot be sent (too many this tick, or not encodable).</summary>
    Refused,
}

/// <summary>Where the match screen sends the local player's orders.</summary>
public interface IOrderSink
{
    /// <summary>Whether orders are taken now.</summary>
    bool CanSubmit { get; }

    /// <summary>The tribe orders must be for, or -1.</summary>
    int Tribe { get; }

    /// <summary>Queues <paramref name="order"/> (its <see cref="GameOrder.Tribe"/> must be <see cref="Tribe"/>).</summary>
    OrderSubmitResult Submit(GameOrder order);
}
