using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>What one tick of a <see cref="LockstepMatch"/> did.</summary>
public sealed class LockstepTickEventArgs : EventArgs
{
    /// <summary>What the rules did.</summary>
    public required TickResult Result { get; init; }

    /// <summary>The command each order outcome came from, index-aligned with <see cref="TickResult.Outcomes"/>.</summary>
    public required IReadOnlyList<PlayerCommand> Sources { get; init; }

    /// <summary>Player table changes of the tick, in order.</summary>
    public required IReadOnlyList<PlayerChange> Changes { get; init; }

    /// <summary>Game commands dropped because their slot takes no orders (absent, finished, no tribe).</summary>
    public required int IgnoredCommands { get; init; }

    /// <summary>The match result, when it was decided on this tick.</summary>
    public MatchOutcome? Decided { get; init; }
}

/// <summary>
/// A network match as the lockstep sessions see it: the rules (<see cref="GameSimulation"/>) plus
/// the player table. Each tick first applies the system commands (players joining, leaving,
/// returning, giving up), then hands every game command of a slot that takes orders to the rules
/// as that slot's tribe's payload. Payloads the rules cannot use, and commands of slots without
/// a playing tribe, change nothing, the same way on every peer.
/// </summary>
public sealed class LockstepMatch : ILockstepSimulation
{
    private const uint SnapshotMagic = 0x4D4C5254; // "TRLM"
    /// <summary>The snapshot format this build writes; it reads every format from 1 to this.</summary>
    public const ushort SnapshotFormat = 1;
    private const int MaxSetupBytes = 256 * 1024;

    private readonly GameContent content;
    private readonly ITribeControl control;

    private LockstepMatch(GameContent content, ITribeControl control, MatchStart start, MatchPlan plan, GameSimulation simulation, PlayerTable players)
    {
        this.content = content;
        this.control = control;
        Start = start;
        Plan = plan;
        Simulation = simulation;
        Players = players;
        Outcome = control.Outcome(simulation);
    }

    /// <summary>The start the match was built from.</summary>
    public MatchStart Start { get; }

    /// <summary>The game setup.</summary>
    public MatchPlan Plan { get; private set; }

    /// <summary>The rules; a new object after <see cref="LoadSnapshot"/> (see <see cref="Reloaded"/>).</summary>
    public GameSimulation Simulation { get; private set; }

    /// <summary>The participants and where they stand.</summary>
    public PlayerTable Players { get; private set; }

    /// <summary>The result once decided, else null.</summary>
    public MatchOutcome? Outcome { get; private set; }

    /// <inheritdoc />
    public long Tick => Simulation.Tick;

    /// <summary>A tick ran (raised inside <see cref="Step"/>).</summary>
    public event EventHandler<LockstepTickEventArgs>? Stepped;

    /// <summary><see cref="LoadSnapshot"/> replaced <see cref="Simulation"/> and <see cref="Players"/>.</summary>
    public event EventHandler? Reloaded;

    /// <summary>A match built from <paramref name="start"/>.</summary>
    /// <param name="content">The game content (the same on every peer).</param>
    /// <param name="start">The match start from the session.</param>
    /// <param name="control">The rules' tribe handling; <see cref="MultiTribeControl"/> when null.</param>
    /// <exception cref="InvalidDataException">The game setup is not usable.</exception>
    public static LockstepMatch Create(GameContent content, MatchStart start, ITribeControl? control = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(start);
        ITribeControl tribes = control ?? MultiTribeControl.Instance;
        MatchPlan plan = MatchPlan.Read(start.GameSetup.Span);
        MatchWorld world;
        try
        {
            world = tribes.Start(content, plan, start.Players);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
        var entries = new List<PlayerEntry>();
        foreach (PlayerInfo player in start.Players)
        {
            entries.Add(PlayerEntry.From(player, player.HasSlot ? world.TribeOfSlot[player.Slot] : -1));
        }
        PlayerTable table = WithWorldTribes(PlayerTable.Of(entries), world.Simulation);
        foreach (PlayerEntry entry in table)
        {
            if (entry is { IsComputer: false, Tribe: >= 0, State: not PlayerState.Playing })
            {
                tribes.SetPlayerState(world.Simulation, entry.Tribe, entry.Slot, entry.State);
            }
        }
        return new LockstepMatch(content, tribes, start, plan, world.Simulation, table);
    }

    /// <summary>The factory for <see cref="HostSession"/> and <see cref="ClientSession"/>.</summary>
    public static Func<MatchStart, ILockstepSimulation> Factory(GameContent content, ITribeControl? control = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return start => Create(content, start, control);
    }

    /// <inheritdoc />
    public void Step(IReadOnlyList<PlayerCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        long tick = Tick;
        var changes = new List<PlayerChange>();
        foreach (PlayerCommand command in commands)
        {
            if (command.IsSystem)
            {
                ApplySystem(command, tick, changes);
            }
        }
        var orders = new List<PlayerOrder>();
        var sources = new List<PlayerCommand>();
        int ignored = 0;
        foreach (PlayerCommand command in commands)
        {
            if (command.IsSystem)
            {
                continue;
            }
            if (Players.BySlot(command.Slot) is { TakesOrders: true } entry)
            {
                orders.Add(PlayerOrder.FromPayload(entry.Tribe, command.Payload));
                sources.Add(command);
            }
            else
            {
                ignored++;
            }
        }
        TickResult result = Simulation.Step(new TickInput { Orders = orders });
        Players = WithWorldTribes(Players, Simulation);
        MarkDefeated(tick, changes);
        MatchOutcome? decided = null;
        if (Outcome is null && control.Outcome(Simulation) is { } outcome)
        {
            Outcome = decided = outcome;
        }
        Stepped?.Invoke(this, new LockstepTickEventArgs
        {
            Result = result,
            Sources = sources,
            Changes = changes,
            IgnoredCommands = ignored,
            Decided = decided,
        });
    }

    /// <inheritdoc />
    public uint StateHash()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Simulation.StateHash());
            writer.Write(Tick);
            writer.Write(Players.Digest);
        }
        uint hash = Fnv.Hash(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        LastHashMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HashCount++;
        return hash;
    }

    /// <summary>How many state hashes were taken (for frame-time checks).</summary>
    public int HashCount { get; private set; }

    /// <summary>How long the last state hash took, in milliseconds.</summary>
    public double LastHashMilliseconds { get; private set; }

    /// <inheritdoc />
    public byte[] SaveSnapshot()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SnapshotMagic);
            writer.Write(SnapshotFormat);
            writer.Write(Tick);
            byte[] setup = Plan.ToBytes();
            writer.Write(setup.Length);
            writer.Write(setup);
            Players.Write(writer);
            byte[] world = Simulation.Save();
            writer.Write(world.Length);
            writer.Write(world);
        }
        return stream.ToArray();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">The data is not a snapshot of this format.</exception>
    public void LoadSnapshot(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
            if (reader.ReadUInt32() != SnapshotMagic)
            {
                throw new InvalidDataException("Not a match snapshot.");
            }
            ushort version = reader.ReadUInt16();
            if (version > SnapshotFormat)
            {
                throw new InvalidDataException($"Match snapshot format {version} is newer than this game reads ({SnapshotFormat}).");
            }
            if (version < 1)
            {
                throw new InvalidDataException($"Match snapshot format {version} is not supported.");
            }
            long tick = reader.ReadInt64();
            MatchPlan plan = MatchPlan.Read(ReadBlock(reader, MaxSetupBytes));
            PlayerTable players = PlayerTable.Read(reader);
            byte[] world = ReadBlock(reader, int.MaxValue);
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The match snapshot has bytes left over.");
            }
            GameSimulation simulation = GameSimulation.Load(content, world);
            if (simulation.Tick != tick)
            {
                throw new InvalidDataException($"The match snapshot is of tick {tick} but its world of tick {simulation.Tick}.");
            }
            Plan = plan;
            Simulation = simulation;
            Players = players;
            Outcome = control.Outcome(simulation);
        }
        catch (Exception e) when (e is IOException or ArgumentException or FormatException or IndexOutOfRangeException or KeyNotFoundException)
        {
            throw new InvalidDataException("The match snapshot is damaged: " + e.Message, e);
        }
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The table with an entry for every computer tribe of the world that no slot plays (an
    /// endless world's computer tribes, tribes the rules add later), so their fate is tracked too.
    /// </summary>
    internal static PlayerTable WithWorldTribes(PlayerTable table, GameSimulation simulation)
    {
        ListView<TribeState> tribes = simulation.World.Tribes;
        var added = new List<PlayerEntry>();
        for (int t = 0; t < tribes.Count; t++)
        {
            if (!tribes[t].Controller.IsHumanRuled && !table.HasTribe(t))
            {
                added.Add(new PlayerEntry { Id = TribeId(t), Name = "", Tribe = t, IsComputer = true });
            }
        }
        return table.WithAll(added);
    }

    /// <summary>The id of a computer tribe no participant brought (the same on every peer).</summary>
    internal static Guid TribeId(int tribe) =>
        new(0x54524942, 0, 0, 0, 0, 0, 0, (byte)(tribe >> 24), (byte)(tribe >> 16), (byte)(tribe >> 8), (byte)tribe);

    private static byte[] ReadBlock(BinaryReader reader, int limit)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException($"A block of {length} bytes does not fit the match snapshot.");
        }
        return reader.ReadBytes(length);
    }

    private void ApplySystem(PlayerCommand command, long tick, List<PlayerChange> changes)
    {
        if (command.Kind == CommandKind.PlayerJoined)
        {
            Join(command, tick, changes);
            return;
        }
        if (Players.BySlot(command.Slot) is not { } entry || PlayerRules.After(entry.State, command.Kind) is not { } state)
        {
            return;
        }
        PlayerEntry changed = entry with { State = state };
        Players = Players.With(changed);
        if (changed is { IsComputer: false, Tribe: >= 0 })
        {
            control.SetPlayerState(Simulation, changed.Tribe, changed.Slot, state);
        }
        changes.Add(new PlayerChange(tick, PlayerRules.KindOf(command.Kind), changed));
    }

    private void Join(PlayerCommand command, long tick, List<PlayerChange> changes)
    {
        PlayerInfo player;
        try
        {
            player = command.ReadJoinedPlayer();
        }
        catch (ProtocolException)
        {
            return;
        }
        if (player.Slot != command.Slot || Players.BySlot(command.Slot) is not null || Players.ById(player.Id) is { HasSlot: true })
        {
            return;
        }
        int tribe = control.AddTribe(Simulation, command.Slot, player.Team);
        PlayerEntry entry = PlayerEntry.From(player with { Presence = PlayerPresence.Active }, tribe);
        Players = Players.With(entry);
        changes.Add(new PlayerChange(tick, PlayerChangeKind.Joined, entry));
    }

    private void MarkDefeated(long tick, List<PlayerChange> changes)
    {
        var defeated = new List<PlayerEntry>();
        foreach (PlayerEntry entry in Players)
        {
            if (entry is { Tribe: >= 0, State: PlayerState.Playing or PlayerState.Absent } && control.IsDefeated(Simulation, entry.Tribe))
            {
                PlayerEntry changed = entry with { State = PlayerState.Out };
                defeated.Add(changed);
                if (!changed.IsComputer)
                {
                    control.SetPlayerState(Simulation, changed.Tribe, changed.Slot, PlayerState.Out);
                }
                changes.Add(new PlayerChange(tick, PlayerChangeKind.Out, changed));
            }
        }
        Players = Players.WithAll(defeated);
    }
}

/// <summary>32-bit FNV-1a, the digest of match hashes and save files.</summary>
internal static class Fnv
{
    private const uint Offset = 2166136261;
    private const uint Prime = 16777619;

    public static uint Hash(ReadOnlySpan<byte> data)
    {
        uint hash = Offset;
        foreach (byte b in data)
        {
            hash = (hash ^ b) * Prime;
        }
        return hash;
    }
}
