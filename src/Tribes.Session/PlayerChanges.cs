using System;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>What happened to a participant.</summary>
public enum PlayerChangeKind
{
    /// <summary>Took a free slot during the match (endless mode).</summary>
    Joined,

    /// <summary>Disconnected or left; shown as 「離線」.</summary>
    Absent,

    /// <summary>Came back with the same id.</summary>
    Returned,

    /// <summary>Gave up (final).</summary>
    Forfeited,

    /// <summary>The tribe was wiped out (final).</summary>
    Out,
}

/// <summary>A change of the player table on a tick, the same on every peer.</summary>
/// <param name="Tick">The tick that carried it.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Player">The participant after the change.</param>
public sealed record PlayerChange(long Tick, PlayerChangeKind Kind, PlayerEntry Player);

/// <summary>How the system commands of a tick change the player table. Pure functions, so every peer agrees.</summary>
internal static class PlayerRules
{
    /// <summary>
    /// The state after <paramref name="kind"/> reached a participant in <paramref name="state"/>,
    /// or null when the command changes nothing. Forfeited and Out are final.
    /// </summary>
    public static PlayerState? After(PlayerState state, CommandKind kind) => (state, kind) switch
    {
        (PlayerState.Playing, CommandKind.PlayerAbsent) => PlayerState.Absent,
        (PlayerState.Absent, CommandKind.PlayerReturned) => PlayerState.Playing,
        (PlayerState.Playing or PlayerState.Absent, CommandKind.PlayerForfeited) => PlayerState.Forfeited,
        _ => null,
    };

    /// <summary>The change kind a system command stands for.</summary>
    public static PlayerChangeKind KindOf(CommandKind kind) => kind switch
    {
        CommandKind.PlayerJoined => PlayerChangeKind.Joined,
        CommandKind.PlayerAbsent => PlayerChangeKind.Absent,
        CommandKind.PlayerReturned => PlayerChangeKind.Returned,
        CommandKind.PlayerForfeited => PlayerChangeKind.Forfeited,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a system command."),
    };
}
