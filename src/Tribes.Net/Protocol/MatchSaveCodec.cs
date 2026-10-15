using System;
using System.Collections.Generic;

namespace Tribes.Net.Protocol;

/// <summary>A command waiting for its tick.</summary>
internal readonly record struct ScheduledCommand(long Tick, PlayerCommand Command);

/// <summary>Everything the host needs to continue a match later.</summary>
internal sealed record SavedMatch
{
    public required string GameVersion { get; init; }

    public required string ContentVersion { get; init; }

    public required RoomState Room { get; init; }

    public required IReadOnlyList<Guid> Banned { get; init; }

    public required MatchStart Start { get; init; }

    public required long Tick { get; init; }

    public required int SpeedPercent { get; init; }

    public required byte[] Snapshot { get; init; }

    public required IReadOnlyList<PlayerCommand> SystemQueue { get; init; }

    public required IReadOnlyList<ScheduledCommand> Scheduled { get; init; }

    /// <summary>The ledger at <see cref="Tick"/>.</summary>
    public required LedgerState Ledger { get; init; }
}

/// <summary>
/// The save file format: the magic <c>TRBS</c>, a format version, then the fields of
/// <see cref="SavedMatch"/> in order. The snapshot is stored compressed.
/// </summary>
internal static class MatchSaveCodec
{
    private const ushort FormatVersion = 1;

    private static ReadOnlySpan<byte> Magic => "TRBS"u8;

    public static byte[] Write(SavedMatch save)
    {
        var writer = new PacketWriter(save.Snapshot.Length / 2 + 256);
        writer.WriteRaw(Magic);
        writer.WriteUInt16(FormatVersion);
        writer.WriteString(save.GameVersion);
        writer.WriteString(save.ContentVersion);
        ModelCodec.WriteRoom(writer, save.Room);
        writer.WriteVarUInt((ulong)save.Banned.Count);
        foreach (Guid id in save.Banned)
        {
            writer.WriteGuid(id);
        }

        ModelCodec.WriteMatchStart(writer, save.Start);
        writer.WriteVarInt(save.Tick);
        writer.WriteVarInt(save.SpeedPercent);
        writer.WriteVarInt(save.Snapshot.Length);
        writer.WriteBytes(SnapshotPacker.Compress(save.Snapshot));
        ModelCodec.WriteCommands(writer, save.SystemQueue);
        writer.WriteVarUInt((ulong)save.Scheduled.Count);
        foreach (ScheduledCommand scheduled in save.Scheduled)
        {
            writer.WriteVarInt(scheduled.Tick);
            ModelCodec.WriteCommand(writer, scheduled.Command);
        }

        save.Ledger.Write(writer);

        return writer.ToArray();
    }

    public static SavedMatch Read(ReadOnlySpan<byte> data)
    {
        var reader = new PacketReader(data.ToArray());
        ReadHeader(reader);
        string gameVersion = reader.ReadString();
        string contentVersion = reader.ReadString();
        RoomState room = ModelCodec.ReadRoom(reader);
        int bannedCount = reader.ReadCount(ProtocolLimits.MaxBannedPlayers, "Banned players");
        var banned = new Guid[bannedCount];
        for (int i = 0; i < bannedCount; i++)
        {
            banned[i] = reader.ReadGuid();
        }

        MatchStart start = ModelCodec.ReadMatchStart(reader);
        long tick = reader.ReadVarInt(0, long.MaxValue, "Tick");
        int speed = reader.ReadInt(RoomLimits.MinSpeedPercent, RoomLimits.MaxSpeedPercent, "Speed");
        int rawLength = reader.ReadInt(0, int.MaxValue, "Snapshot length");
        byte[] snapshot = SnapshotPacker.Decompress(reader.ReadBytes(int.MaxValue).ToArray(), rawLength);
        PlayerCommand[] systemQueue = ModelCodec.ReadCommands(reader, ProtocolLimits.MaxCommandsPerBatch);
        int scheduledCount = reader.ReadCount(ProtocolLimits.MaxSavedCommands, "Scheduled commands");
        var scheduled = new ScheduledCommand[scheduledCount];
        for (int i = 0; i < scheduledCount; i++)
        {
            scheduled[i] = new ScheduledCommand(reader.ReadVarInt(0, long.MaxValue, "Tick"), ModelCodec.ReadCommand(reader));
        }

        LedgerState ledger = LedgerState.Read(reader);
        reader.ExpectEnd();
        return new SavedMatch
        {
            GameVersion = gameVersion,
            ContentVersion = contentVersion,
            Room = room,
            Banned = banned,
            Start = start,
            Tick = tick,
            SpeedPercent = speed,
            Snapshot = snapshot,
            SystemQueue = systemQueue,
            Scheduled = scheduled,
            Ledger = ledger,
        };
    }

    private static void ReadHeader(PacketReader reader)
    {
        if (reader.Remaining < Magic.Length + 2)
        {
            throw new ProtocolException("The data is too short to be a match save.");
        }

        Span<byte> magic = stackalloc byte[4];
        for (int i = 0; i < magic.Length; i++)
        {
            magic[i] = reader.ReadByte();
        }

        if (!magic.SequenceEqual(Magic))
        {
            throw new ProtocolException("The data is not a match save.");
        }

        ushort version = reader.ReadUInt16();
        if (version != FormatVersion)
        {
            throw new ProtocolException($"The match save has format {version}; this build reads format {FormatVersion}.");
        }
    }
}
