using System;
using System.Collections.Generic;

namespace Tribes.Net.Protocol;

/// <summary>One tick's commands, as the host decided them.</summary>
internal sealed class TickBatch
{
    public TickBatch(long tick, IReadOnlyList<PlayerCommand> commands)
    {
        Tick = tick;
        Commands = commands;
    }

    public long Tick { get; }

    public IReadOnlyList<PlayerCommand> Commands { get; }
}

/// <summary>Encoding of the shared model types inside messages and saves.</summary>
internal static class ModelCodec
{
    public static void WritePlayer(PacketWriter writer, PlayerInfo player)
    {
        writer.WriteGuid(player.Id);
        writer.WriteString(player.Name);
        writer.WriteVarInt(player.Slot);
        writer.WriteByte((byte)player.Kind);
        writer.WriteVarInt(player.Colour);
        writer.WriteVarInt(player.Team);
        writer.WriteBool(player.IsReady);
        writer.WriteByte((byte)player.Presence);
        writer.WriteBool(player.IsConnected);
        writer.WriteBool(player.IsHost);
        writer.WriteVarInt(player.AiLevel);
    }

    public static PlayerInfo ReadPlayer(PacketReader reader)
    {
        return new PlayerInfo
        {
            Id = reader.ReadGuid(),
            Name = reader.ReadString(),
            Slot = reader.ReadInt(-1, RoomLimits.MaxPlayers - 1, "Slot"),
            Kind = reader.ReadEnum<PlayerKind>("Player kind"),
            Colour = reader.ReadInt(-1, RoomLimits.ColourCount - 1, "Colour"),
            Team = reader.ReadInt(0, RoomLimits.MaxTeams - 1, "Team"),
            IsReady = reader.ReadBool(),
            Presence = reader.ReadEnum<PlayerPresence>("Presence"),
            IsConnected = reader.ReadBool(),
            IsHost = reader.ReadBool(),
            AiLevel = reader.ReadInt(0, 255, "AI level"),
        };
    }

    public static void WriteSettings(PacketWriter writer, RoomSettings settings)
    {
        writer.WriteString(settings.Name);
        writer.WriteByte((byte)settings.Mode);
        writer.WriteVarInt(settings.MaxPlayers);
        writer.WriteBool(settings.AllowJoinInProgress);
        writer.WriteBool(settings.AllowSpectators);
        writer.WriteVarInt(settings.MaxSpectators);
        writer.WriteBool(settings.AllowClientPause);
        writer.WriteBool(settings.AcceptSpeedRequests);
    }

    public static RoomSettings ReadSettings(PacketReader reader)
    {
        return new RoomSettings
        {
            Name = reader.ReadString(),
            Mode = reader.ReadEnum<RoomMode>("Room mode"),
            MaxPlayers = reader.ReadInt(1, RoomLimits.MaxPlayers, "Max players"),
            AllowJoinInProgress = reader.ReadBool(),
            AllowSpectators = reader.ReadBool(),
            MaxSpectators = reader.ReadInt(0, RoomLimits.MaxSpectators, "Max spectators"),
            AllowClientPause = reader.ReadBool(),
            AcceptSpeedRequests = reader.ReadBool(),
        };
    }

    public static void WriteRoom(PacketWriter writer, RoomState room)
    {
        writer.WriteGuid(room.RoomId);
        WriteSettings(writer, room.Settings);
        writer.WriteByte((byte)room.Phase);
        writer.WriteVarInt(room.Revision);
        WritePlayers(writer, room.Players);
    }

    public static RoomState ReadRoom(PacketReader reader)
    {
        return new RoomState
        {
            RoomId = reader.ReadGuid(),
            Settings = ReadSettings(reader),
            Phase = reader.ReadEnum<RoomPhase>("Room phase"),
            Revision = reader.ReadInt(0, int.MaxValue, "Revision"),
            Players = ReadPlayers(reader),
        };
    }

    public static void WritePlayers(PacketWriter writer, IReadOnlyList<PlayerInfo> players)
    {
        writer.WriteVarUInt((ulong)players.Count);
        foreach (PlayerInfo player in players)
        {
            WritePlayer(writer, player);
        }
    }

    public static PlayerInfo[] ReadPlayers(PacketReader reader)
    {
        int count = reader.ReadCount(ProtocolLimits.MaxRoomPlayers, "Player count");
        var players = new PlayerInfo[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = ReadPlayer(reader);
        }

        return players;
    }

    public static void WriteMatchStart(PacketWriter writer, MatchStart start)
    {
        writer.WriteGuid(start.MatchId);
        writer.WriteByte((byte)start.Mode);
        writer.WriteVarInt(start.TickMilliseconds);
        writer.WriteVarInt(start.HashIntervalTicks);
        WritePlayers(writer, start.Players);
        writer.WriteBytes(start.GameSetup.Span);
    }

    public static MatchStart ReadMatchStart(PacketReader reader)
    {
        return new MatchStart
        {
            MatchId = reader.ReadGuid(),
            Mode = reader.ReadEnum<RoomMode>("Room mode"),
            TickMilliseconds = reader.ReadInt(1, 60_000, "Tick length"),
            HashIntervalTicks = reader.ReadInt(0, 1_000_000, "Hash interval"),
            Players = ReadPlayers(reader),
            GameSetup = reader.ReadBytes(ProtocolLimits.MaxSetupBytes),
        };
    }

    public static void WriteCommand(PacketWriter writer, PlayerCommand command)
    {
        writer.WriteVarUInt((ulong)command.Slot);
        writer.WriteByte((byte)command.Kind);
        writer.WriteVarInt(command.Sequence);
        writer.WriteBytes(command.Payload.Span);
    }

    public static PlayerCommand ReadCommand(PacketReader reader)
    {
        int slot = reader.ReadCount(RoomLimits.MaxPlayers - 1, "Command slot");
        CommandKind kind = reader.ReadEnum<CommandKind>("Command kind");
        long sequence = reader.ReadVarInt(0, long.MaxValue, "Command sequence");
        ReadOnlyMemory<byte> payload = reader.ReadBytes(ProtocolLimits.MaxCommandBytes);
        var command = new PlayerCommand(slot, kind, payload, sequence);
        if (kind == CommandKind.PlayerJoined && command.ReadJoinedPlayer().Slot != slot)
        {
            throw new ProtocolException("A joined player's slot differs from its command's slot.");
        }

        return command;
    }

    public static void WriteCommands(PacketWriter writer, IReadOnlyList<PlayerCommand> commands)
    {
        writer.WriteVarUInt((ulong)commands.Count);
        foreach (PlayerCommand command in commands)
        {
            WriteCommand(writer, command);
        }
    }

    public static PlayerCommand[] ReadCommands(PacketReader reader, int maxCount)
    {
        int count = reader.ReadCount(maxCount, "Command count");
        var commands = new PlayerCommand[count];
        for (int i = 0; i < count; i++)
        {
            commands[i] = ReadCommand(reader);
        }

        return commands;
    }

    public static void WriteBatch(PacketWriter writer, TickBatch batch)
    {
        writer.WriteVarInt(batch.Tick);
        WriteCommands(writer, batch.Commands);
    }

    public static TickBatch ReadBatch(PacketReader reader)
    {
        long tick = reader.ReadVarInt(0, long.MaxValue, "Tick");
        return new TickBatch(tick, ReadCommands(reader, ProtocolLimits.MaxCommandsPerBatch));
    }
}

/// <summary>A peer that can take over hosting, and the address the others reach it at then.</summary>
internal sealed record Successor(Guid PlayerId, string Address);

/// <summary>What the host tells every peer besides the room: whom it banned and who takes over if it leaves.</summary>
internal sealed record RoomExtras(IReadOnlyList<Guid> Banned, IReadOnlyList<Successor> Succession)
{
    public static readonly RoomExtras Empty = new([], []);

    public void Write(PacketWriter writer)
    {
        writer.WriteVarUInt((ulong)Banned.Count);
        foreach (Guid id in Banned)
        {
            writer.WriteGuid(id);
        }

        writer.WriteVarUInt((ulong)Succession.Count);
        foreach (Successor successor in Succession)
        {
            writer.WriteGuid(successor.PlayerId);
            writer.WriteString(successor.Address);
        }
    }

    public static RoomExtras Read(PacketReader reader)
    {
        var banned = new Guid[reader.ReadCount(ProtocolLimits.MaxBannedPlayers, "Banned players")];
        for (int i = 0; i < banned.Length; i++)
        {
            banned[i] = reader.ReadGuid();
        }

        var succession = new Successor[reader.ReadCount(ProtocolLimits.MaxRoomPlayers, "Successors")];
        for (int i = 0; i < succession.Length; i++)
        {
            succession[i] = new Successor(reader.ReadGuid(), reader.ReadString());
        }

        return new RoomExtras(banned, succession);
    }
}

/// <summary>A <see cref="MatchLedger"/> on the wire: the slot holders, then each slot's last command number.</summary>
internal sealed record LedgerState(IReadOnlyList<PlayerInfo> Roster, IReadOnlyList<long> Sequences)
{
    public void Write(PacketWriter writer)
    {
        ModelCodec.WritePlayers(writer, Roster);
        foreach (long sequence in Sequences)
        {
            writer.WriteVarInt(sequence);
        }
    }

    public static LedgerState Read(PacketReader reader)
    {
        PlayerInfo[] roster = ModelCodec.ReadPlayers(reader);
        if (Array.Exists(roster, p => !p.HasSlot))
        {
            throw new ProtocolException("A ledger lists a participant without a slot.");
        }

        var sequences = new long[RoomLimits.MaxPlayers];
        for (int i = 0; i < sequences.Length; i++)
        {
            sequences[i] = reader.ReadVarInt(0, long.MaxValue, "Command sequence");
        }

        return new LedgerState(roster, sequences);
    }
}
