using System;
using System.Collections.Generic;

namespace Tribes.Net;

/// <summary>The room changed: players, colours, teams, readiness or presence.</summary>
public sealed class RoomChangedEventArgs : EventArgs
{
    /// <summary>The new state.</summary>
    public required RoomState Room { get; init; }
}

/// <summary>A chat line arrived.</summary>
public sealed class ChatReceivedEventArgs : EventArgs
{
    /// <summary>Persistent id of the sender.</summary>
    public required Guid SenderId { get; init; }

    /// <summary>Name of the sender when the line was sent.</summary>
    public required string SenderName { get; init; }

    /// <summary>The line, already cleaned of control characters.</summary>
    public required string Text { get; init; }
}

/// <summary>A match began on this peer: the simulation exists and ticks will follow.</summary>
public sealed class MatchStartedEventArgs : EventArgs
{
    /// <summary>The start data the simulation was built from.</summary>
    public required MatchStart Start { get; init; }

    /// <summary>The simulation the session drives.</summary>
    public required ILockstepSimulation Simulation { get; init; }

    /// <summary>Whether the peer joined a running match (or was resynchronised) from a snapshot.</summary>
    public bool FromSnapshot { get; init; }
}

/// <summary>
/// A slot's player joined, left, returned or forfeited. Raised on every peer right after the tick
/// that carried the change, so all peers see it at the same tick.
/// </summary>
public sealed class PlayerPresenceChangedEventArgs : EventArgs
{
    /// <summary>The tick whose batch carried the change.</summary>
    public required long Tick { get; init; }

    /// <summary>The slot.</summary>
    public required int Slot { get; init; }

    /// <summary>Which system command it was.</summary>
    public required CommandKind Change { get; init; }

    /// <summary>The slot's state from that tick on.</summary>
    public required PlayerPresence Presence { get; init; }

    /// <summary>The player, when known.</summary>
    public PlayerInfo? Player { get; init; }
}

/// <summary>A tick ran.</summary>
public sealed class TickSteppedEventArgs : EventArgs
{
    /// <summary>The tick that ran; the simulation is now at the next one.</summary>
    public required long Tick { get; init; }

    /// <summary>The commands it ran with.</summary>
    public required IReadOnlyList<PlayerCommand> Commands { get; init; }
}

/// <summary>A peer's state hash differs from the host's: the simulations no longer agree.</summary>
public sealed class DesyncDetectedEventArgs : EventArgs
{
    /// <summary>The first compared tick whose hashes differ.</summary>
    public required long Tick { get; init; }

    /// <summary>The last compared tick whose hashes matched, or -1; the divergence happened after it.</summary>
    public required long LastMatchingTick { get; init; }

    /// <summary>The peer whose state differs from the host's.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>The host's hash at <see cref="Tick"/>.</summary>
    public required uint ExpectedHash { get; init; }

    /// <summary>The peer's hash at <see cref="Tick"/>.</summary>
    public required uint ReportedHash { get; init; }
}

/// <summary>
/// The world grew too large to send to a player (<see cref="LockstepOptions.MaxSnapshotBytes"/>): a
/// player joining or coming back was turned down, or one whose state had to be sent again was let go.
/// </summary>
public sealed class WorldTooLargeEventArgs : EventArgs
{
    /// <summary>The player.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>The player's name.</summary>
    public required string PlayerName { get; init; }

    /// <summary>The world's size, unpacked.</summary>
    public required long WorldBytes { get; init; }

    /// <summary>The largest world that can be sent.</summary>
    public required int LimitBytes { get; init; }
}

/// <summary>The host turned down a request of this client.</summary>
public sealed class RequestDeniedEventArgs : EventArgs
{
    /// <summary>The request.</summary>
    public required RequestKind Request { get; init; }

    /// <summary>Why.</summary>
    public required RequestResult Result { get; init; }

    /// <summary>A sentence to show the player.</summary>
    public required string Message { get; init; }
}

/// <summary>The session is over.</summary>
public sealed class SessionEndedEventArgs : EventArgs
{
    /// <summary>Why.</summary>
    public required SessionEndReason Reason { get; init; }

    /// <summary>For <see cref="SessionEndReason.Rejected"/>, the host's reason.</summary>
    public RejectReason Rejection { get; init; }

    /// <summary>A sentence to show the player.</summary>
    public required string Message { get; init; }
}

/// <summary>The match was paused or resumed, or its speed changed.</summary>
public sealed class ControlStateChangedEventArgs : EventArgs
{
    /// <summary>Whether the match is paused.</summary>
    public required bool Paused { get; init; }

    /// <summary>Game speed in percent of normal.</summary>
    public required int SpeedPercent { get; init; }
}

/// <summary>A player asked the host for another game speed.</summary>
public sealed class SpeedRequestedEventArgs : EventArgs
{
    /// <summary>Who asked.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>The speed asked for, in percent of normal.</summary>
    public required int SpeedPercent { get; init; }
}

/// <summary>The host dropped a command a player sent.</summary>
public sealed class CommandRejectedEventArgs : EventArgs
{
    /// <summary>Who sent it.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>Why it was dropped.</summary>
    public required RequestResult Result { get; init; }
}

/// <summary>A client session moved to another phase.</summary>
public sealed class ClientPhaseChangedEventArgs : EventArgs
{
    /// <summary>The new phase.</summary>
    public required ClientPhase Phase { get; init; }
}

/// <summary>
/// The host left and this client took over: the match goes on in <see cref="Host"/>, with the
/// same simulation. From now on the game calls <see cref="HostSession.Update"/> instead of the
/// client session's.
/// </summary>
public sealed class BecameHostEventArgs : EventArgs
{
    /// <summary>The session this peer hosts the match with now.</summary>
    public required HostSession Host { get; init; }
}

/// <summary>The host left and another player took over; this client is connected to the new host.</summary>
public sealed class HostChangedEventArgs : EventArgs
{
    /// <summary>The player who hosts the match now.</summary>
    public required Guid HostId { get; init; }
}
