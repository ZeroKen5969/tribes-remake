using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Tribes.Net;

namespace Tribes.Net.Tests.Support;

/// <summary>
/// A deterministic stand-in for the game rules: the state is a hash chain over every tick and
/// every command, so two copies agree exactly when they ran the same batches. It also tracks
/// which slots are away (absent players: their tribes simply get no orders) and forfeited.
/// </summary>
internal sealed class HashChainSimulation : ILockstepSimulation
{
    private const uint Prime = 16777619;
    private readonly Dictionary<long, uint> hashByTick = [];
    private readonly byte[] padding;

    public HashChainSimulation(MatchStart start, int snapshotPaddingBytes = 0)
    {
        Start = start;
        Hash = Mix(2166136261, start.GameSetup.Span);
        padding = new byte[snapshotPaddingBytes];
        new Random(snapshotPaddingBytes).NextBytes(padding);
        foreach (PlayerInfo player in start.Players)
        {
            if (player.HasSlot && player.Kind == PlayerKind.Computer)
            {
                ComputerPlayerSlots.Add(player.Slot);
            }
        }
    }

    public MatchStart Start { get; }

    public long Tick { get; private set; }

    public uint Hash { get; private set; }

    /// <summary>When set, the hash after this tick is corrupted, as a non-deterministic rule would.</summary>
    public long CorruptAtTick { get; set; } = -1;

    /// <summary>Slots of computer players, from the start.</summary>
    public HashSet<int> ComputerPlayerSlots { get; } = [];

    /// <summary>Slots whose player is absent: the tribe gets no orders until the player returns.</summary>
    public HashSet<int> AwaySlots { get; } = [];

    public HashSet<int> ForfeitedSlots { get; } = [];

    public List<(long Tick, PlayerCommand Command)> Log { get; } = [];

    /// <summary>The hash after each tick this copy ran.</summary>
    public IReadOnlyDictionary<long, uint> HashByTick => hashByTick;

    public long FirstTick { get; private set; }

    public void Step(IReadOnlyList<PlayerCommand> commands)
    {
        uint hash = MixLong(Hash, Tick);
        foreach (PlayerCommand command in commands)
        {
            hash = MixLong(hash, command.Slot);
            hash = MixLong(hash, (int)command.Kind);
            hash = Mix(hash, command.Payload.Span);
            Apply(command);
            Log.Add((Tick, command));
        }

        if (Tick == CorruptAtTick)
        {
            hash ^= 0xDEADBEEF;
        }

        Hash = hash;
        hashByTick[Tick] = hash;
        Tick++;
    }

    public uint StateHash() => Hash;

    public byte[] SaveSnapshot()
    {
        var data = new byte[8 + 4 + 3 + padding.Length];
        BinaryPrimitives.WriteInt64LittleEndian(data, Tick);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), Hash);
        data[12] = SlotMask(AwaySlots);
        data[13] = SlotMask(ForfeitedSlots);
        data[14] = SlotMask(ComputerPlayerSlots);
        padding.CopyTo(data.AsSpan(15));
        return data;
    }

    public void LoadSnapshot(byte[] data)
    {
        Tick = BinaryPrimitives.ReadInt64LittleEndian(data);
        Hash = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        ReadMask(data[12], AwaySlots);
        ReadMask(data[13], ForfeitedSlots);
        ReadMask(data[14], ComputerPlayerSlots);
        FirstTick = Tick;
        hashByTick.Clear();
    }

    private void Apply(PlayerCommand command)
    {
        switch (command.Kind)
        {
            case CommandKind.PlayerAbsent:
                AwaySlots.Add(command.Slot);
                break;
            case CommandKind.PlayerReturned:
            case CommandKind.PlayerJoined:
                AwaySlots.Remove(command.Slot);
                break;
            case CommandKind.PlayerForfeited:
                ForfeitedSlots.Add(command.Slot);
                break;
        }
    }

    private static byte SlotMask(HashSet<int> slots)
    {
        int mask = 0;
        foreach (int slot in slots)
        {
            mask |= 1 << slot;
        }

        return (byte)mask;
    }

    private static void ReadMask(byte mask, HashSet<int> slots)
    {
        slots.Clear();
        for (int slot = 0; slot < 8; slot++)
        {
            if ((mask & (1 << slot)) != 0)
            {
                slots.Add(slot);
            }
        }
    }

    private static uint MixLong(uint hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return Mix(hash, bytes);
    }

    private static uint Mix(uint hash, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            hash = unchecked((hash ^ value) * Prime);
        }

        return hash;
    }
}
