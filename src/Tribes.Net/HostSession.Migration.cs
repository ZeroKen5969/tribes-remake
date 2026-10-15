using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

/// <summary>What a client hands over when it takes over hosting a match whose host left.</summary>
internal sealed record SuccessorState
{
    public required ITransport Transport { get; init; }

    public required ClientOptions Options { get; init; }

    public required Func<MatchStart, ILockstepSimulation> SimulationFactory { get; init; }

    public required IClock Clock { get; init; }

    public required Guid SelfId { get; init; }

    /// <summary>The room as the old host last described it.</summary>
    public required RoomState Room { get; init; }

    public required IReadOnlyList<Guid> Banned { get; init; }

    /// <summary>The ledger after every batch the client received, all of them run.</summary>
    public required MatchLedger Ledger { get; init; }

    public required MatchStart Start { get; init; }

    public required ILockstepSimulation Simulation { get; init; }

    public required Guid TimelineId { get; init; }

    public required IReadOnlyList<(long Tick, byte[] Message)> History { get; init; }

    public required IEnumerable<(long Tick, uint Hash)> Hashes { get; init; }

    public required bool Paused { get; init; }

    public required int SpeedPercent { get; init; }

    /// <summary>The client's own commands that have not run yet, with their numbers.</summary>
    public required IReadOnlyList<(long Sequence, byte[] Payload)> PendingCommands { get; init; }
}

public sealed partial class HostSession
{
    private readonly Dictionary<Guid, TimeSpan> awaitingReturn = [];

    /// <summary>
    /// After <see cref="Leave"/> handed the match over: the address of the player who hosts it
    /// now. Pass it to <see cref="ClientSession.Connect"/> (with the same player id) to keep
    /// playing as an ordinary player. Null otherwise.
    /// </summary>
    public string? SuccessorAddress { get; private set; }

    /// <summary>
    /// Leaves the room. During a match, on a transport with host migration, the match goes on:
    /// the first player in line takes over hosting and everyone else follows. Otherwise (in the
    /// lobby, or on the LAN) this is <see cref="Close"/>: the room ends for everyone, and a LAN
    /// match should be saved first.
    /// </summary>
    public void Leave()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closed)
        {
            return;
        }

        Successor? next = Succession().FirstOrDefault();
        if (next is null)
        {
            Close();
            return;
        }

        closed = true;
        SuccessorAddress = next.Address;
        byte[] leaving = MessageCodec.Encode(new HostLeavingMessage(next.PlayerId));
        transport.SendToMany([.. peers.Values.Where(p => p.HasJoined).Select(p => p.Connection)], leaving);
        peers.Clear();
        room.Close();
        transport.Shutdown();
        Detach();
        RoomChanged?.Invoke(this, new RoomChangedEventArgs { Room = room.State });
    }

    /// <summary>
    /// Makes a client the host of the match its host left: the room is rebuilt from the ledger
    /// (the slot table as every simulation has it), the batches it received become the history,
    /// and the transport makes it the room's host.
    /// </summary>
    /// <param name="state">What the client knows of the match.</param>
    /// <param name="otherHost">When another peer took over first, its address; the client connects to it instead.</param>
    /// <returns>The new host session, or null when another peer took over first.</returns>
    internal static HostSession? TakeOver(SuccessorState state, out string? otherHost)
    {
        var options = new HostOptions
        {
            GameVersion = state.Options.GameVersion,
            ContentVersion = state.Options.ContentVersion,
            RulesFingerprint = state.Options.RulesFingerprint,
            PlayerId = state.Options.PlayerId,
            PlayerName = state.Options.PlayerName,
            Room = state.Room.Settings,
            Lockstep = state.Options.Lockstep with
            {
                TickMilliseconds = state.Start.TickMilliseconds,
                HashIntervalTicks = state.Start.HashIntervalTicks,
            },
            MigrationGracePeriod = state.Options.MigrationGracePeriod,
        };
        HostRoom room = HostRoom.ForSuccessor(state.Room, state.Banned, state.Ledger, state.SelfId);
        var match = new HostMatch(state.Start, state.Simulation, options.Lockstep, state.Ledger, [new TimelineLink(state.TimelineId, state.Simulation.Tick)])
        {
            Paused = state.Paused,
        };
        match.SetSpeed(state.SpeedPercent);
        match.Seed(state.History, state.Hashes);
        var session = new HostSession(state.Transport, options, state.SimulationFactory, state.Clock, room) { match = match };
        otherHost = state.Transport.TakeOverHost(session.BuildAdvertisement());
        if (otherHost is not null)
        {
            session.Detach();
            return null;
        }

        session.SettleAfterTakeOver(state);
        match.Pacer.Reset(state.Clock.Now);
        session.PublishRoom();
        return session;
    }

    /// <summary>
    /// The peers that would take over, in order: players by slot, then spectators; only those
    /// connected through a transport that can move the host role.
    /// </summary>
    private List<Successor> Succession()
    {
        if (match is null || closed || !transport.SupportsHostMigration)
        {
            return [];
        }

        IReadOnlyList<PlayerInfo> order = room.State.Players;
        return [.. peers.Values
            .Where(p => p.HasJoined && p.MigrationAddress.Length > 0 && room.Find(p.PlayerId) is not null)
            .Select(p => (Peer: p, Rank: Rank(order, p.PlayerId)))
            .OrderBy(entry => entry.Rank)
            .Select(entry => new Successor(entry.Peer.PlayerId, entry.Peer.MigrationAddress))];
    }

    private static int Rank(IReadOnlyList<PlayerInfo> players, Guid id)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Id == id)
            {
                return players[i].HasSlot ? players[i].Slot : RoomLimits.MaxPlayers + i;
            }
        }

        return int.MaxValue;
    }

    private RoomExtras Extras() => new([.. room.Banned], Succession());

    /// <summary>
    /// Brings the rebuilt room in line with what the players asked the old host for: a forfeit
    /// stays final, the old host is absent, and the others have a grace period to come back.
    /// </summary>
    private void SettleAfterTakeOver(SuccessorState state)
    {
        HostMatch current = match!;
        Guid oldHost = state.Room.Players.FirstOrDefault(p => p.IsHost)?.Id ?? Guid.Empty;
        TimeSpan deadline = clock.Now + options.MigrationGracePeriod;
        foreach (PlayerInfo player in room.State.Players.ToArray())
        {
            if (player.Id == state.SelfId)
            {
                if (player.Presence == PlayerPresence.Absent)
                {
                    room.SetPresence(player.Id, PlayerPresence.Active);
                    current.QueueSystem(PlayerCommand.System(player.Slot, CommandKind.PlayerReturned));
                }

                continue;
            }

            if (player.Kind != PlayerKind.Human || !player.HasSlot)
            {
                continue;
            }

            if (state.Room.FindPlayer(player.Id)?.Presence == PlayerPresence.Forfeited && player.Presence != PlayerPresence.Forfeited)
            {
                room.SetPresence(player.Id, PlayerPresence.Forfeited);
                current.QueueSystem(PlayerCommand.System(player.Slot, CommandKind.PlayerForfeited));
            }
            else if (player.Presence == PlayerPresence.Active && player.Id == oldHost)
            {
                MarkAbsent(player, current);
            }
            else if (player.Presence == PlayerPresence.Active)
            {
                awaitingReturn[player.Id] = deadline;
            }
        }

        PlayerInfo self = room.Host;
        if (self.HasSlot)
        {
            foreach ((long sequence, byte[] payload) in state.PendingCommands)
            {
                current.Schedule(self.Slot, current.NextTick + options.Lockstep.MinInputDelayTicks, payload, sequence);
            }
        }
    }

    /// <summary>Players who have not come back to a new host within the grace period count as absent.</summary>
    private void ExpireAwaitedPlayers(TimeSpan now)
    {
        if (awaitingReturn.Count == 0 || match is null)
        {
            return;
        }

        bool changed = false;
        foreach ((Guid id, TimeSpan deadline) in awaitingReturn.ToArray())
        {
            if (now < deadline)
            {
                continue;
            }

            awaitingReturn.Remove(id);
            if (room.Find(id) is { IsConnected: false, Presence: PlayerPresence.Active, HasSlot: true } player)
            {
                MarkAbsent(player, match);
                changed = true;
            }
        }

        if (changed)
        {
            PublishRoom();
        }
    }

    private void MarkAbsent(PlayerInfo player, HostMatch current)
    {
        room.SetPresence(player.Id, PlayerPresence.Absent);
        current.DropScheduled(player.Slot);
        current.QueueSystem(PlayerCommand.System(player.Slot, CommandKind.PlayerAbsent));
    }
}
