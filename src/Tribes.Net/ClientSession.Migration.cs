using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

public sealed partial class ClientSession
{
    private static readonly TimeSpan MigrationRetryDelay = TimeSpan.FromMilliseconds(200);
    private readonly HashSet<Guid> unreachableSuccessors = [];
    private Guid? designatedSuccessor;
    private Successor? migrationTarget;
    private TimeSpan migrationDeadline;
    private TimeSpan migrationRetryAt;

    /// <summary>
    /// Whether the end of the host connection means the host left the match (so the host role
    /// moves on) rather than a broken link of this client (so it reconnects to the same host).
    /// </summary>
    private bool HostIsGone(DisconnectReason reason)
    {
        if (!transport.SupportsHostMigration)
        {
            return false;
        }

        if (Phase == ClientPhase.Migrating || reason == DisconnectReason.HostLeft || designatedSuccessor is not null)
        {
            return true;
        }

        // A client reconnecting after a broken link of its own finds the host gone from the room.
        return Phase == ClientPhase.Reconnecting && reason == DisconnectReason.ConnectFailed
            && address is not null && !transport.IsPeerPresent(address);
    }

    private void OnHostGone()
    {
        if (Phase == ClientPhase.Migrating && migrationTarget is { } target)
        {
            TimeSpan now = clock.Now;
            if (FollowRoomHost())
            {
                return;
            }

            if (now < migrationDeadline && transport.IsPeerPresent(target.Address))
            {
                migrationRetryAt = now + MigrationRetryDelay;
                return;
            }

            unreachableSuccessors.Add(target.PlayerId);
        }

        BeginMigration();
    }

    /// <summary>
    /// Picks who hosts the match now: the first peer in the host's succession list (the one the
    /// leaving host named comes first) that is still in the room. Every peer has the same list
    /// and asks the same room, so they all pick the same one.
    /// </summary>
    private void BeginMigration()
    {
        if (FollowRoomHost())
        {
            return;
        }

        Successor? next = ChooseSuccessor();
        if (next is null)
        {
            End(SessionEndReason.HostClosed, "The host left and nobody else can host the match.", RejectReason.None);
            return;
        }

        if (next.PlayerId == LocalPlayerId)
        {
            BecomeHost();
        }
        else
        {
            MigrateTo(next);
        }
    }

    private Successor? ChooseSuccessor()
    {
        IEnumerable<Successor> line = extras.Succession;
        if (designatedSuccessor is { } named)
        {
            line = line.Where(s => s.PlayerId == named).Concat(line);
        }

        foreach (Successor candidate in line)
        {
            if (unreachableSuccessors.Contains(candidate.PlayerId))
            {
                continue;
            }

            if (candidate.PlayerId == LocalPlayerId)
            {
                if (engine is not null && ledger is not null)
                {
                    return candidate;
                }

                continue;
            }

            if (transport.IsPeerPresent(candidate.Address))
            {
                return candidate;
            }
        }

        return null;
    }

    private void MigrateTo(Successor successor)
    {
        migrationTarget = successor;
        migrationDeadline = clock.Now + options.MigrationTimeout;
        address = successor.Address;
        assembler = null;
        earlyBatches.Clear();
        engine?.ClearBuffered();
        receivedBatches.DropFrom(engine?.Simulation.Tick ?? 0);
        SetPhase(ClientPhase.Migrating);
        ConnectToSuccessor();
    }

    private void ConnectToSuccessor()
    {
        migrationRetryAt = TimeSpan.MaxValue;
        try
        {
            transport.Connect(address!);
        }
        catch (InvalidOperationException ex)
        {
            // The transport is busy with another connection (for example the game shares it).
            End(SessionEndReason.ConnectionLost, ex.Message, RejectReason.None);
        }
    }

    /// <summary>Retries the successor until it answers; gives up on it after the migration timeout.</summary>
    private void ContinueMigration(TimeSpan now)
    {
        if (now >= migrationDeadline && migrationTarget is { } target)
        {
            IConnection? stale = connection;
            connection = null;
            stale?.Disconnect();
            unreachableSuccessors.Add(target.PlayerId);
            BeginMigration();
            return;
        }

        if (connection is null && now >= migrationRetryAt && !FollowRoomHost())
        {
            ConnectToSuccessor();
        }
    }

    /// <summary>
    /// When the room already records a new host other than the one this client is trying, goes
    /// to that one: the room knows better than a list that may be out of date.
    /// </summary>
    private bool FollowRoomHost()
    {
        if (transport.RoomHostAddress is not { } current
            || string.Equals(current, transport.MigrationAddress, StringComparison.Ordinal)
            || string.Equals(current, migrationTarget?.Address, StringComparison.Ordinal))
        {
            return false;
        }

        Guid id = extras.Succession.FirstOrDefault(s => string.Equals(s.Address, current, StringComparison.Ordinal))?.PlayerId ?? Guid.Empty;
        MigrateTo(new Successor(id, current));
        return true;
    }

    private void FinishMigration(bool migrated)
    {
        designatedSuccessor = null;
        if (!migrated)
        {
            return;
        }

        migrationTarget = null;
        unreachableSuccessors.Clear();
        if (Room?.Players.FirstOrDefault(p => p.IsHost) is { } host)
        {
            HostChanged?.Invoke(this, new HostChangedEventArgs { HostId = host.Id });
        }
    }

    /// <summary>
    /// This peer is next in line: it runs the batches it already has and turns into the host,
    /// keeping its simulation. If another peer won the room first, it follows that one instead.
    /// </summary>
    private void BecomeHost()
    {
        LockstepEngine current = engine!;
        while (current.TryStep(out TickBatch? batch))
        {
            AfterStep(current, batch);
        }

        var state = new SuccessorState
        {
            Transport = transport,
            Options = options,
            SimulationFactory = simulationFactory,
            Clock = clock,
            SelfId = LocalPlayerId,
            Room = Room!,
            Banned = extras.Banned,
            Ledger = ledger!,
            Start = Match!,
            Simulation = current.Simulation,
            TimelineId = timelineId,
            History = [.. receivedBatches.Entries],
            Hashes = [.. ownHashes.Entries],
            Paused = IsPaused,
            SpeedPercent = SpeedPercent,
            PendingCommands = UnrunCommands(),
        };
        HostSession? host = HostSession.TakeOver(state, out string? otherHost);
        if (host is null)
        {
            MigrateTo(new Successor(Guid.Empty, otherHost!));
            return;
        }

        Detach();
        BecameHost?.Invoke(this, new BecameHostEventArgs { Host = host });
        End(SessionEndReason.BecameHost, "This player hosts the match now.", RejectReason.None);
    }
}
