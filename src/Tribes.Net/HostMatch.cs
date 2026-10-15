using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

/// <summary>An earlier run of the match whose batches before <see cref="SharedUntil"/> equal this run's.</summary>
internal readonly record struct TimelineLink(Guid Timeline, long SharedUntil);

/// <summary>
/// The host's state of a running match: the simulation, the commands waiting for their tick,
/// recent batches and hashes, pause and speed.
/// </summary>
internal sealed class HostMatch
{
    private const int HashCapacity = 512;
    private readonly List<PlayerCommand> systemQueue = [];
    private readonly SortedDictionary<long, PendingTick> scheduled = [];
    private readonly long[] lastSequence = new long[RoomLimits.MaxPlayers];
    private readonly long[] orderTick = new long[RoomLimits.MaxPlayers];
    private readonly LockstepOptions options;
    private (long Tick, PackedSnapshot Snapshot)? cachedSnapshot;

    public HostMatch(MatchStart start, ILockstepSimulation simulation, LockstepOptions options, MatchLedger ledger, IReadOnlyList<TimelineLink>? ancestry = null)
    {
        Start = start;
        Simulation = simulation;
        this.options = options;
        Ledger = ledger;
        Ancestry = ancestry ?? [];
        Engine = new LockstepEngine(simulation);
        Pacer = new TickPacer(start.TickMilliseconds);
        Hashes = new HashLedger(HashCapacity);
        History = new BatchHistory(options.BatchHistoryTicks);
        for (int slot = 0; slot < RoomLimits.MaxPlayers; slot++)
        {
            lastSequence[slot] = ledger.SequenceOf(slot);
        }
    }

    public MatchStart Start { get; }

    /// <summary>Names this run of the host; a match resumed from a save or taken over by a new host gets a new one.</summary>
    public Guid TimelineId { get; } = Guid.NewGuid();

    /// <summary>Earlier runs this one continues; peers that stopped in their shared part can still resume from history.</summary>
    public IReadOnlyList<TimelineLink> Ancestry { get; }

    public ILockstepSimulation Simulation { get; }

    public LockstepEngine Engine { get; }

    public TickPacer Pacer { get; }

    public HashLedger Hashes { get; }

    public BatchHistory History { get; }

    /// <summary>The slot table and command numbers as of <see cref="NextTick"/>.</summary>
    public MatchLedger Ledger { get; }

    public bool Paused { get; set; }

    public int SpeedPercent { get; private set; } = 100;

    /// <summary>The tick the next batch is for.</summary>
    public long NextTick => Simulation.Tick;

    public IReadOnlyList<PlayerCommand> PendingSystemCommands => systemQueue;

    public IEnumerable<ScheduledCommand> ScheduledCommands =>
        scheduled.SelectMany(pair => pair.Value.Commands.Select(command => new ScheduledCommand(pair.Key, command)));

    public void SetSpeed(int speedPercent)
    {
        SpeedPercent = speedPercent;
        Pacer.SetSpeed(speedPercent);
    }

    public void QueueSystem(PlayerCommand command) => systemQueue.Add(command);

    /// <summary>The highest command number of the slot that is scheduled or has run.</summary>
    public long LastSequenceOf(int slot) => slot is >= 0 and < RoomLimits.MaxPlayers ? lastSequence[slot] : 0;

    /// <summary>
    /// Puts a player's command number <paramref name="sequence"/> into the batch of
    /// <paramref name="targetTick"/>. A late command goes into the next open batch, one aimed too
    /// far ahead is pulled closer, and a player's commands never run out of order. A number the
    /// host already has is a copy sent again after a host change and is dropped. A tick holds at
    /// most <see cref="LockstepOptions.MaxCommandsPerPlayerPerTick"/> commands per slot and
    /// <see cref="ProtocolLimits.MaxBatchPayloadBytes"/> of payload in all; a command for a full
    /// tick goes into the next one, as far as the latest tick a command may be aimed at (the
    /// commands a client sends again after a reconnect or a host change arrive all at once).
    /// </summary>
    public RequestResult Schedule(int slot, long targetTick, ReadOnlyMemory<byte> payload, long sequence)
    {
        if (sequence <= lastSequence[slot])
        {
            return RequestResult.Accepted;
        }

        long latest = NextTick + (2L * options.MaxInputDelayTicks) + 1;
        long tick = Math.Max(Math.Clamp(targetTick, NextTick, latest), orderTick[slot]);
        for (long last = Math.Max(tick, latest); tick <= last; tick++)
        {
            if (scheduled.TryGetValue(tick, out PendingTick? full)
                && (full.Commands.Count(c => c.Slot == slot) >= options.MaxCommandsPerPlayerPerTick
                    || full.PayloadBytes + payload.Length > ProtocolLimits.MaxBatchPayloadBytes))
            {
                continue;
            }

            Pending(tick).Add(new PlayerCommand(slot, CommandKind.Game, payload, sequence));
            lastSequence[slot] = sequence;
            orderTick[slot] = tick;
            return RequestResult.Accepted;
        }

        return RequestResult.TooManyCommands;
    }

    /// <summary>Forgets the slot's game commands that have not run; its player is gone.</summary>
    public void DropScheduled(int slot)
    {
        foreach (PendingTick pending in scheduled.Values)
        {
            pending.RemoveSlot(slot);
        }
    }

    /// <summary>
    /// Closes the batch of <paramref name="tick"/>: system commands first (at most
    /// <see cref="ProtocolLimits.MaxSystemCommandsPerBatch"/>; the rest wait for the next tick),
    /// then game commands in arrival order.
    /// </summary>
    public TickBatch TakeBatch(long tick)
    {
        int systemCount = Math.Min(systemQueue.Count, ProtocolLimits.MaxSystemCommandsPerBatch);
        var commands = new List<PlayerCommand>(systemQueue.GetRange(0, systemCount));
        systemQueue.RemoveRange(0, systemCount);
        if (scheduled.Remove(tick, out PendingTick? game))
        {
            commands.AddRange(game.Commands);
        }

        cachedSnapshot = null;
        return new TickBatch(tick, commands);
    }

    /// <summary>
    /// The batches a reconnecting peer at <paramref name="tick"/> of run <paramref name="timelineId"/>
    /// missed, or null when it needs a snapshot. A peer of an earlier run can resume only if it
    /// stopped within the part that run shares with this one.
    /// </summary>
    public List<byte[]>? MissedBatches(Guid timelineId, long tick)
    {
        if (tick < 0 || tick > NextTick || timelineId == Guid.Empty)
        {
            return null;
        }

        if (timelineId != TimelineId && !Ancestry.Any(link => link.Timeline == timelineId && tick <= link.SharedUntil))
        {
            return null;
        }

        return History.Range(tick, NextTick);
    }

    /// <summary>A snapshot of the state before <see cref="NextTick"/>; reused until the next tick runs.</summary>
    public (long Tick, PackedSnapshot Snapshot) TakeSnapshot()
    {
        if (cachedSnapshot is { } cached && cached.Tick == NextTick)
        {
            return cached;
        }

        PackedSnapshot packed = SnapshotPacker.Pack(Simulation.SaveSnapshot(), options.SnapshotChunkBytes);
        cachedSnapshot = (NextTick, packed);
        return cachedSnapshot.Value;
    }

    /// <summary>Puts back the commands of a save; a tick that would overflow a batch passes the rest to the next tick.</summary>
    public void Restore(IEnumerable<PlayerCommand> system, IEnumerable<ScheduledCommand> commands)
    {
        systemQueue.AddRange(system);
        foreach (ScheduledCommand command in commands)
        {
            long tick = Math.Max(command.Tick, NextTick);
            while (!Pending(tick).Fits(command.Command))
            {
                tick++;
            }

            Pending(tick).Add(command.Command);
            int slot = command.Command.Slot;
            lastSequence[slot] = Math.Max(lastSequence[slot], command.Command.Sequence);
            orderTick[slot] = Math.Max(orderTick[slot], tick);
        }
    }

    /// <summary>A new host starts with the batches and hashes it received as a client, so others can still resume.</summary>
    public void Seed(IEnumerable<(long Tick, byte[] Message)> batches, IEnumerable<(long Tick, uint Hash)> hashes)
    {
        foreach ((long tick, byte[] message) in batches)
        {
            History.Add(tick, message);
        }

        foreach ((long tick, uint hash) in hashes)
        {
            Hashes.Record(tick, hash);
        }
    }

    private PendingTick Pending(long tick)
    {
        if (!scheduled.TryGetValue(tick, out PendingTick? pending))
        {
            pending = new PendingTick();
            scheduled.Add(tick, pending);
        }

        return pending;
    }

    /// <summary>The game commands waiting for one tick.</summary>
    private sealed class PendingTick
    {
        private const int MaxGameCommands = ProtocolLimits.MaxCommandsPerBatch - ProtocolLimits.MaxSystemCommandsPerBatch;

        public List<PlayerCommand> Commands { get; } = [];

        public int PayloadBytes { get; private set; }

        public bool Fits(PlayerCommand command) =>
            Commands.Count < MaxGameCommands && PayloadBytes + command.Payload.Length <= ProtocolLimits.MaxBatchPayloadBytes;

        public void Add(PlayerCommand command)
        {
            Commands.Add(command);
            PayloadBytes += command.Payload.Length;
        }

        public void RemoveSlot(int slot)
        {
            Commands.RemoveAll(c => c.Slot == slot);
            PayloadBytes = Commands.Sum(c => c.Payload.Length);
        }
    }
}

/// <summary>The host's view of one connection.</summary>
internal sealed class RemotePeer
{
    private static readonly TimeSpan ChatWindow = TimeSpan.FromSeconds(5);
    private const int ChatLinesPerWindow = 5;
    private readonly Queue<TimeSpan> recentChat = new();

    public RemotePeer(IConnection connection, TimeSpan connectedAt)
    {
        Connection = connection;
        ConnectedAt = connectedAt;
    }

    public IConnection Connection { get; }

    public TimeSpan ConnectedAt { get; }

    public Guid PlayerId { get; set; }

    /// <summary>Where the others reach this peer if it takes over hosting; empty when it cannot.</summary>
    public string MigrationAddress { get; set; } = string.Empty;

    /// <summary>Whether the hello was accepted.</summary>
    public bool HasJoined { get; set; }

    /// <summary>Whether the peer receives the match's batches.</summary>
    public bool InMatch { get; set; }

    public long LastMatchingHashTick { get; set; } = -1;

    /// <summary>Hashes of earlier ticks come from a state that a snapshot has since replaced.</summary>
    public long IgnoreHashesBefore { get; set; }

    public bool DesyncReported { get; set; }

    /// <summary>Counts a chat line; false when the peer sends too many.</summary>
    public bool AllowChat(TimeSpan now)
    {
        while (recentChat.Count > 0 && now - recentChat.Peek() > ChatWindow)
        {
            recentChat.Dequeue();
        }

        if (recentChat.Count >= ChatLinesPerWindow)
        {
            return false;
        }

        recentChat.Enqueue(now);
        return true;
    }
}
