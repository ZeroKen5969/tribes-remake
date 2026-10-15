using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Tribes.Net.Protocol;

namespace Tribes.Net;

/// <summary>
/// The game rules as the network layer sees them. Every peer runs its own copy; given the same
/// start and the same command batches, all copies must reach the same state, tick by tick.
/// </summary>
public interface ILockstepSimulation
{
    /// <summary>The next tick to run, which is also the number of ticks run since the match began.</summary>
    long Tick { get; }

    /// <summary>
    /// Runs tick <see cref="Tick"/> and advances <see cref="Tick"/> by one. The list holds every
    /// command scheduled for the tick, identical and in the same order on every peer: first the
    /// system commands (players joining, leaving, returning, forfeiting), then game commands.
    /// Game payloads come from remote players: a payload the rules cannot use must be ignored
    /// (the same way on every peer), never thrown on.
    /// </summary>
    /// <param name="commands">The commands of this tick; may be empty.</param>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The game is written in C#, where Step is not a keyword.")]
    void Step(IReadOnlyList<PlayerCommand> commands);

    /// <summary>
    /// A digest of the whole rules state. Peers compare it every few ticks to find the tick where
    /// they stopped agreeing; it must depend only on rules state, never on what is on screen.
    /// </summary>
    uint StateHash();

    /// <summary>The whole rules state, including <see cref="Tick"/>, for players who join late.</summary>
    byte[] SaveSnapshot();

    /// <summary>Replaces the state with one saved by <see cref="SaveSnapshot"/> on any peer.</summary>
    /// <param name="data">Bytes from <see cref="SaveSnapshot"/>.</param>
    void LoadSnapshot(byte[] data);
}

/// <summary>What a command in a tick batch is.</summary>
public enum CommandKind
{
    /// <summary>An order from a player; the payload is the game's own encoding.</summary>
    Game = 0,

    /// <summary>
    /// A new player took the slot during the match. The payload holds the player; read it with
    /// <see cref="PlayerCommand.ReadJoinedPlayer"/>.
    /// </summary>
    PlayerJoined = 1,

    /// <summary>
    /// The slot's player disconnected or left. Only a notice: the tribe gets no orders from this
    /// tick until <see cref="PlayerReturned"/>; nobody, not even the computer, plays it meanwhile.
    /// </summary>
    PlayerAbsent = 2,

    /// <summary>The slot's player came back; their orders reach the tribe again.</summary>
    PlayerReturned = 3,

    /// <summary>The slot's player gave up or was defeated; final for the match. The tribe gets no more orders, and the computer does not take it over.</summary>
    PlayerForfeited = 4,
}

/// <summary>One command of a tick batch.</summary>
public sealed class PlayerCommand
{
    /// <summary>Creates a command.</summary>
    /// <param name="slot">The tribe slot it belongs to.</param>
    /// <param name="kind">Game command or a system command.</param>
    /// <param name="payload">The command bytes.</param>
    public PlayerCommand(int slot, CommandKind kind, ReadOnlyMemory<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, RoomLimits.MaxPlayers);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a command kind.");
        }

        Slot = slot;
        Kind = kind;
        Payload = payload;
    }

    internal PlayerCommand(int slot, CommandKind kind, ReadOnlyMemory<byte> payload, long sequence)
        : this(slot, kind, payload)
    {
        Sequence = sequence;
    }

    /// <summary>The tribe slot the command belongs to.</summary>
    public int Slot { get; }

    /// <summary>
    /// For game commands, the sending player's running number (1, 2, 3...). The host schedules a
    /// player's commands in this order and drops numbers it already has, so commands can be sent
    /// again after a host change without running twice.
    /// </summary>
    internal long Sequence { get; }

    /// <summary>Game command or a system command.</summary>
    public CommandKind Kind { get; }

    /// <summary>The command bytes; empty for most system commands.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Whether the network layer, not a player, issued the command.</summary>
    public bool IsSystem => Kind != CommandKind.Game;

    /// <summary>The player of a <see cref="CommandKind.PlayerJoined"/> command.</summary>
    /// <exception cref="InvalidOperationException">The command is of another kind.</exception>
    public PlayerInfo ReadJoinedPlayer()
    {
        if (Kind != CommandKind.PlayerJoined)
        {
            throw new InvalidOperationException($"A {Kind} command does not carry a player.");
        }

        var reader = new PacketReader(Payload);
        PlayerInfo player = ModelCodec.ReadPlayer(reader);
        reader.ExpectEnd();
        return player;
    }

    internal static PlayerCommand Joined(PlayerInfo player)
    {
        var writer = new PacketWriter();
        ModelCodec.WritePlayer(writer, player);
        return new PlayerCommand(player.Slot, CommandKind.PlayerJoined, writer.ToArray());
    }

    internal static PlayerCommand System(int slot, CommandKind kind) =>
        new(slot, kind, ReadOnlyMemory<byte>.Empty);
}

/// <summary>
/// What every peer needs to build the same starting world. The host makes it when the match
/// starts; peers who join later get it together with a snapshot.
/// </summary>
public sealed record MatchStart
{
    /// <summary>Id of the match; stays the same when the host resumes it from a save.</summary>
    public required Guid MatchId { get; init; }

    /// <summary>Versus or endless.</summary>
    public required RoomMode Mode { get; init; }

    /// <summary>Length of a tick at normal speed.</summary>
    public required int TickMilliseconds { get; init; }

    /// <summary>Peers compare state hashes after every tick that is a multiple of this; 0 never.</summary>
    public required int HashIntervalTicks { get; init; }

    /// <summary>The participants when the match started.</summary>
    public required IReadOnlyList<PlayerInfo> Players { get; init; }

    /// <summary>The host game's own start data: map, seed, rules options.</summary>
    public ReadOnlyMemory<byte> GameSetup { get; init; }
}

/// <summary>Timing and limits of the lockstep. The host's tick length and hash interval apply to all peers.</summary>
public sealed record LockstepOptions
{
    /// <summary>Length of a tick at normal speed. The game rules run at 120 ms per tick.</summary>
    public int TickMilliseconds { get; init; } = 120;

    /// <summary>
    /// Fewest ticks between issuing a command and running it. The host's own commands use this
    /// delay; clients use it as the floor of their adaptive delay.
    /// </summary>
    public int MinInputDelayTicks { get; init; } = 1;

    /// <summary>Most ticks a client's adaptive input delay may grow to.</summary>
    public int MaxInputDelayTicks { get; init; } = 12;

    /// <summary>Compare state hashes after every tick that is a multiple of this; 0 turns checks off.</summary>
    public int HashIntervalTicks { get; init; } = 8;

    /// <summary>A client with more unrun batches than this runs extra ticks to catch up.</summary>
    public int CatchUpThresholdTicks { get; init; } = 3;

    /// <summary>Most ticks a client runs in one update while catching up.</summary>
    public int MaxCatchUpStepsPerUpdate { get; init; } = 60;

    /// <summary>Most ticks the host runs in one update; after a longer stall it drops the lost time.</summary>
    public int MaxHostStepsPerUpdate { get; init; } = 4;

    /// <summary>
    /// Recent batches the host keeps, so a player who reconnects quickly gets the missed batches
    /// instead of a full snapshot.
    /// </summary>
    public int BatchHistoryTicks { get; init; } = 1500;

    /// <summary>Snapshots are sent in pieces of at most this many bytes.</summary>
    public int SnapshotChunkBytes { get; init; } = 32 * 1024;

    /// <summary>Largest snapshot a client accepts, uncompressed.</summary>
    public int MaxSnapshotBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Largest command payload.</summary>
    public int MaxCommandBytes { get; init; } = 1024;

    /// <summary>Most commands one player may have in a single tick (at most 1008).</summary>
    public int MaxCommandsPerPlayerPerTick { get; init; } = 32;

    private const int MaxCommandsPerPlayerPerTickLimit =
        (ProtocolLimits.MaxCommandsPerBatch - ProtocolLimits.MaxSystemCommandsPerBatch) / RoomLimits.MaxPlayers;

    /// <summary>Whether the host sends a fresh snapshot to a peer whose state hash differs.</summary>
    public bool ResyncOnDesync { get; init; } = true;

    /// <summary>What is wrong with the options, or null when they are usable.</summary>
    internal string? FindProblem()
    {
        if (TickMilliseconds is < 10 or > 2000)
        {
            return "TickMilliseconds must be between 10 and 2000.";
        }

        if (MinInputDelayTicks < 0 || MaxInputDelayTicks < MinInputDelayTicks || MaxInputDelayTicks > 64)
        {
            return "Input delays must satisfy 0 <= MinInputDelayTicks <= MaxInputDelayTicks <= 64.";
        }

        if (HashIntervalTicks < 0 || CatchUpThresholdTicks < 0 || BatchHistoryTicks < 0)
        {
            return "HashIntervalTicks, CatchUpThresholdTicks and BatchHistoryTicks must not be negative.";
        }

        if (MaxCatchUpStepsPerUpdate < 1 || MaxHostStepsPerUpdate < 1 || MaxCommandsPerPlayerPerTick < 1)
        {
            return "Step and command limits must be at least 1.";
        }

        if (MaxCommandsPerPlayerPerTick > MaxCommandsPerPlayerPerTickLimit)
        {
            return $"MaxCommandsPerPlayerPerTick must be at most {MaxCommandsPerPlayerPerTickLimit}, so a tick's batch stays within the message limits.";
        }

        if (SnapshotChunkBytes is < 1024 or > ProtocolLimits.MaxChunkBytes)
        {
            return $"SnapshotChunkBytes must be between 1024 and {ProtocolLimits.MaxChunkBytes}.";
        }

        if (MaxSnapshotBytes < 1 || MaxCommandBytes is < 1 or > ProtocolLimits.MaxCommandBytes)
        {
            return $"MaxSnapshotBytes must be positive and MaxCommandBytes between 1 and {ProtocolLimits.MaxCommandBytes}.";
        }

        return null;
    }
}
