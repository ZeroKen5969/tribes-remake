using System;

namespace Tribes.Net.Protocol;

/// <summary>The first byte of every session message.</summary>
internal enum MessageType : byte
{
    // Client to host.
    Hello = 1,
    Chat = 2,
    ChooseColour = 3,
    ChooseTeam = 4,
    SetReady = 5,
    Leave = 6,
    Forfeit = 7,
    Command = 8,
    StateHash = 9,
    ControlRequest = 10,

    // Host to client.
    Welcome = 32,
    Reject = 33,
    RoomUpdate = 34,
    ChatRelay = 35,
    RequestDenied = 36,
    MatchBegin = 37,
    SnapshotBegin = 38,
    SnapshotChunk = 39,
    Batch = 40,
    ControlState = 41,
    DesyncNotice = 42,
    SessionEnd = 43,
    HostLeaving = 44,
}

/// <summary>What a <see cref="ControlRequestMessage"/> asks for.</summary>
internal enum ControlKind : byte
{
    Pause = 0,
    Resume = 1,
    Speed = 2,
}

internal abstract record NetMessage
{
    public abstract MessageType Type { get; }

    public abstract void WriteBody(PacketWriter writer);
}

/// <summary>
/// A client introduces itself; the protocol version comes first so it can always be read.
/// <see cref="PlayerId"/> is the player's secret key; <see cref="TimelineId"/> and <see cref="ResumeTick"/>
/// say where its simulation stands when it reconnects (empty and -1 otherwise).
/// </summary>
internal sealed record HelloMessage(
    int ProtocolVersion,
    string GameVersion,
    string ContentVersion,
    Guid PlayerId,
    string Name,
    bool Spectate,
    int PreferredColour,
    Guid TimelineId,
    long ResumeTick,
    string MigrationAddress,
    string RulesFingerprint) : NetMessage
{
    public override MessageType Type => MessageType.Hello;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteUInt16((ushort)ProtocolVersion);
        writer.WriteString(GameVersion);
        writer.WriteString(ContentVersion);
        writer.WriteGuid(PlayerId);
        writer.WriteString(Name);
        writer.WriteBool(Spectate);
        writer.WriteVarInt(PreferredColour);
        writer.WriteGuid(TimelineId);
        writer.WriteVarInt(ResumeTick);
        writer.WriteString(MigrationAddress);
        writer.WriteString(RulesFingerprint);
    }

    public static HelloMessage Read(PacketReader reader) => new(
        reader.ReadUInt16(),
        reader.ReadString(),
        reader.ReadString(),
        reader.ReadGuid(),
        reader.ReadString(),
        reader.ReadBool(),
        reader.ReadInt(-1, RoomLimits.ColourCount - 1, "Preferred colour"),
        reader.ReadGuid(),
        reader.ReadVarInt(-1, long.MaxValue, "Resume tick"),
        reader.ReadString(),
        reader.ReadString(RoomLimits.MaxVersionLength));
}

internal sealed record ChatMessage(string Text) : NetMessage
{
    public override MessageType Type => MessageType.Chat;

    public override void WriteBody(PacketWriter writer) => writer.WriteString(Text);

    public static ChatMessage Read(PacketReader reader) => new(reader.ReadString(1024));
}

internal sealed record ChooseColourMessage(int Colour) : NetMessage
{
    public override MessageType Type => MessageType.ChooseColour;

    public override void WriteBody(PacketWriter writer) => writer.WriteVarInt(Colour);

    public static ChooseColourMessage Read(PacketReader reader) => new(reader.ReadInt(int.MinValue, int.MaxValue, "Colour"));
}

internal sealed record ChooseTeamMessage(int Team) : NetMessage
{
    public override MessageType Type => MessageType.ChooseTeam;

    public override void WriteBody(PacketWriter writer) => writer.WriteVarInt(Team);

    public static ChooseTeamMessage Read(PacketReader reader) => new(reader.ReadInt(int.MinValue, int.MaxValue, "Team"));
}

internal sealed record SetReadyMessage(bool Ready) : NetMessage
{
    public override MessageType Type => MessageType.SetReady;

    public override void WriteBody(PacketWriter writer) => writer.WriteBool(Ready);

    public static SetReadyMessage Read(PacketReader reader) => new(reader.ReadBool());
}

internal sealed record LeaveMessage : NetMessage
{
    public override MessageType Type => MessageType.Leave;

    public override void WriteBody(PacketWriter writer)
    {
    }
}

internal sealed record ForfeitMessage : NetMessage
{
    public override MessageType Type => MessageType.Forfeit;

    public override void WriteBody(PacketWriter writer)
    {
    }
}

/// <summary>
/// A game command the client wants run at <see cref="TargetTick"/>; <see cref="Sequence"/> is
/// the player's running number of it (see <see cref="PlayerCommand.Sequence"/>).
/// </summary>
internal sealed record CommandMessage(long TargetTick, long Sequence, ReadOnlyMemory<byte> Payload) : NetMessage
{
    public override MessageType Type => MessageType.Command;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteVarInt(TargetTick);
        writer.WriteVarInt(Sequence);
        writer.WriteBytes(Payload.Span);
    }

    public static CommandMessage Read(PacketReader reader) => new(
        reader.ReadVarInt(0, long.MaxValue, "Target tick"),
        reader.ReadVarInt(1, long.MaxValue, "Command sequence"),
        reader.ReadBytes(ProtocolLimits.MaxCommandBytes));
}

internal sealed record StateHashMessage(long Tick, uint Hash) : NetMessage
{
    public override MessageType Type => MessageType.StateHash;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteVarInt(Tick);
        writer.WriteUInt32(Hash);
    }

    public static StateHashMessage Read(PacketReader reader) => new(
        reader.ReadVarInt(0, long.MaxValue, "Tick"),
        reader.ReadUInt32());
}

internal sealed record ControlRequestMessage(ControlKind Kind, int Value) : NetMessage
{
    public override MessageType Type => MessageType.ControlRequest;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteByte((byte)Kind);
        writer.WriteVarInt(Value);
    }

    public static ControlRequestMessage Read(PacketReader reader) => new(
        reader.ReadEnum<ControlKind>("Control request"),
        reader.ReadInt(int.MinValue, int.MaxValue, "Control value"));
}

/// <summary>
/// The host accepted the client. <see cref="ResumeTick"/> is -1, or the tick from which the host
/// sends the batches the client missed (it then keeps its simulation). <see cref="TimelineId"/>
/// is the host's run (empty in the lobby); <see cref="LastSequence"/> the highest command number
/// the host has from the player's slot, so a new session goes on above it.
/// </summary>
internal sealed record WelcomeMessage(RoomState Room, RoomExtras Extras, long ResumeTick, Guid TimelineId, long LastSequence) : NetMessage
{
    public override MessageType Type => MessageType.Welcome;

    public override void WriteBody(PacketWriter writer)
    {
        ModelCodec.WriteRoom(writer, Room);
        Extras.Write(writer);
        writer.WriteVarInt(ResumeTick);
        writer.WriteGuid(TimelineId);
        writer.WriteVarInt(LastSequence);
    }

    public static WelcomeMessage Read(PacketReader reader) => new(
        ModelCodec.ReadRoom(reader),
        RoomExtras.Read(reader),
        reader.ReadVarInt(-1, long.MaxValue, "Resume tick"),
        reader.ReadGuid(),
        reader.ReadVarInt(0, long.MaxValue, "Last sequence"));
}

internal sealed record RejectMessage(RejectReason Reason, string Message) : NetMessage
{
    public override MessageType Type => MessageType.Reject;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteByte((byte)Reason);
        writer.WriteString(Message);
    }

    public static RejectMessage Read(PacketReader reader) => new(
        reader.ReadEnum<RejectReason>("Reject reason"),
        reader.ReadString());
}

internal sealed record RoomUpdateMessage(RoomState Room, RoomExtras Extras) : NetMessage
{
    public override MessageType Type => MessageType.RoomUpdate;

    public override void WriteBody(PacketWriter writer)
    {
        ModelCodec.WriteRoom(writer, Room);
        Extras.Write(writer);
    }

    public static RoomUpdateMessage Read(PacketReader reader) => new(ModelCodec.ReadRoom(reader), RoomExtras.Read(reader));
}

/// <summary>The host is leaving the running match; <see cref="SuccessorId"/> takes over.</summary>
internal sealed record HostLeavingMessage(Guid SuccessorId) : NetMessage
{
    public override MessageType Type => MessageType.HostLeaving;

    public override void WriteBody(PacketWriter writer) => writer.WriteGuid(SuccessorId);

    public static HostLeavingMessage Read(PacketReader reader) => new(reader.ReadGuid());
}

internal sealed record ChatRelayMessage(Guid SenderId, string SenderName, string Text) : NetMessage
{
    public override MessageType Type => MessageType.ChatRelay;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteGuid(SenderId);
        writer.WriteString(SenderName);
        writer.WriteString(Text);
    }

    public static ChatRelayMessage Read(PacketReader reader) => new(
        reader.ReadGuid(),
        reader.ReadString(),
        reader.ReadString(1024));
}

internal sealed record RequestDeniedMessage(RequestKind Request, RequestResult Result, string Message) : NetMessage
{
    public override MessageType Type => MessageType.RequestDenied;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteByte((byte)Request);
        writer.WriteByte((byte)Result);
        writer.WriteString(Message);
    }

    public static RequestDeniedMessage Read(PacketReader reader) => new(
        reader.ReadEnum<RequestKind>("Request kind"),
        reader.ReadEnum<RequestResult>("Request result"),
        reader.ReadString());
}

/// <summary>
/// The match starts. <see cref="TimelineId"/> names this run of the host: it changes when the
/// host resumes from a save, so a client never resumes into batches of another run.
/// </summary>
internal sealed record MatchBeginMessage(MatchStart Start, int SpeedPercent, Guid TimelineId) : NetMessage
{
    public override MessageType Type => MessageType.MatchBegin;

    public override void WriteBody(PacketWriter writer)
    {
        ModelCodec.WriteMatchStart(writer, Start);
        writer.WriteVarInt(SpeedPercent);
        writer.WriteGuid(TimelineId);
    }

    public static MatchBeginMessage Read(PacketReader reader) => new(
        ModelCodec.ReadMatchStart(reader),
        reader.ReadInt(RoomLimits.MinSpeedPercent, RoomLimits.MaxSpeedPercent, "Speed"),
        reader.ReadGuid());
}

/// <summary>
/// Header of a snapshot; <see cref="ChunkCount"/> chunk messages follow, then the batches from
/// <see cref="Tick"/> on. <see cref="Ledger"/> is the host's ledger at that tick.
/// </summary>
internal sealed record SnapshotBeginMessage(
    MatchStart Start,
    long Tick,
    int RawLength,
    uint Checksum,
    bool Compressed,
    int ChunkCount,
    int SpeedPercent,
    bool Paused,
    Guid TimelineId,
    LedgerState Ledger) : NetMessage
{
    public override MessageType Type => MessageType.SnapshotBegin;

    public override void WriteBody(PacketWriter writer)
    {
        ModelCodec.WriteMatchStart(writer, Start);
        writer.WriteVarInt(Tick);
        writer.WriteVarInt(RawLength);
        writer.WriteUInt32(Checksum);
        writer.WriteBool(Compressed);
        writer.WriteVarInt(ChunkCount);
        writer.WriteVarInt(SpeedPercent);
        writer.WriteBool(Paused);
        writer.WriteGuid(TimelineId);
        Ledger.Write(writer);
    }

    public static SnapshotBeginMessage Read(PacketReader reader) => new(
        ModelCodec.ReadMatchStart(reader),
        reader.ReadVarInt(0, long.MaxValue, "Snapshot tick"),
        reader.ReadInt(0, int.MaxValue, "Snapshot length"),
        reader.ReadUInt32(),
        reader.ReadBool(),
        reader.ReadInt(1, int.MaxValue, "Chunk count"),
        reader.ReadInt(RoomLimits.MinSpeedPercent, RoomLimits.MaxSpeedPercent, "Speed"),
        reader.ReadBool(),
        reader.ReadGuid(),
        LedgerState.Read(reader));
}

internal sealed record SnapshotChunkMessage(int Index, ReadOnlyMemory<byte> Data) : NetMessage
{
    public override MessageType Type => MessageType.SnapshotChunk;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteVarInt(Index);
        writer.WriteBytes(Data.Span);
    }

    public static SnapshotChunkMessage Read(PacketReader reader) => new(
        reader.ReadInt(0, int.MaxValue, "Chunk index"),
        reader.ReadBytes(ProtocolLimits.MaxChunkBytes));
}

internal sealed record BatchMessage(TickBatch Batch) : NetMessage
{
    public override MessageType Type => MessageType.Batch;

    public override void WriteBody(PacketWriter writer) => ModelCodec.WriteBatch(writer, Batch);

    public static BatchMessage Read(PacketReader reader) => new(ModelCodec.ReadBatch(reader));
}

internal sealed record ControlStateMessage(bool Paused, int SpeedPercent) : NetMessage
{
    public override MessageType Type => MessageType.ControlState;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteBool(Paused);
        writer.WriteVarInt(SpeedPercent);
    }

    public static ControlStateMessage Read(PacketReader reader) => new(
        reader.ReadBool(),
        reader.ReadInt(RoomLimits.MinSpeedPercent, RoomLimits.MaxSpeedPercent, "Speed"));
}

internal sealed record DesyncNoticeMessage(long Tick, long LastMatchingTick, Guid PlayerId, uint ExpectedHash, uint ReportedHash) : NetMessage
{
    public override MessageType Type => MessageType.DesyncNotice;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteVarInt(Tick);
        writer.WriteVarInt(LastMatchingTick);
        writer.WriteGuid(PlayerId);
        writer.WriteUInt32(ExpectedHash);
        writer.WriteUInt32(ReportedHash);
    }

    public static DesyncNoticeMessage Read(PacketReader reader) => new(
        reader.ReadVarInt(0, long.MaxValue, "Tick"),
        reader.ReadVarInt(-1, long.MaxValue, "Last matching tick"),
        reader.ReadGuid(),
        reader.ReadUInt32(),
        reader.ReadUInt32());
}

internal sealed record SessionEndMessage(SessionEndReason Reason, string Message) : NetMessage
{
    public override MessageType Type => MessageType.SessionEnd;

    public override void WriteBody(PacketWriter writer)
    {
        writer.WriteByte((byte)Reason);
        writer.WriteString(Message);
    }

    public static SessionEndMessage Read(PacketReader reader) => new(
        reader.ReadEnum<SessionEndReason>("End reason"),
        reader.ReadString());
}

/// <summary>Turns messages into bytes and back.</summary>
internal static class MessageCodec
{
    public static byte[] Encode(NetMessage message)
    {
        var writer = new PacketWriter();
        writer.WriteByte((byte)message.Type);
        message.WriteBody(writer);
        return writer.ToArray();
    }

    public static NetMessage Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new PacketReader(data);
        var type = (MessageType)reader.ReadByte();
        NetMessage message = type switch
        {
            MessageType.Hello => HelloMessage.Read(reader),
            MessageType.Chat => ChatMessage.Read(reader),
            MessageType.ChooseColour => ChooseColourMessage.Read(reader),
            MessageType.ChooseTeam => ChooseTeamMessage.Read(reader),
            MessageType.SetReady => SetReadyMessage.Read(reader),
            MessageType.Leave => new LeaveMessage(),
            MessageType.Forfeit => new ForfeitMessage(),
            MessageType.Command => CommandMessage.Read(reader),
            MessageType.StateHash => StateHashMessage.Read(reader),
            MessageType.ControlRequest => ControlRequestMessage.Read(reader),
            MessageType.Welcome => WelcomeMessage.Read(reader),
            MessageType.Reject => RejectMessage.Read(reader),
            MessageType.RoomUpdate => RoomUpdateMessage.Read(reader),
            MessageType.ChatRelay => ChatRelayMessage.Read(reader),
            MessageType.RequestDenied => RequestDeniedMessage.Read(reader),
            MessageType.MatchBegin => MatchBeginMessage.Read(reader),
            MessageType.SnapshotBegin => SnapshotBeginMessage.Read(reader),
            MessageType.SnapshotChunk => SnapshotChunkMessage.Read(reader),
            MessageType.Batch => BatchMessage.Read(reader),
            MessageType.ControlState => ControlStateMessage.Read(reader),
            MessageType.DesyncNotice => DesyncNoticeMessage.Read(reader),
            MessageType.SessionEnd => SessionEndMessage.Read(reader),
            MessageType.HostLeaving => HostLeavingMessage.Read(reader),
            _ => throw new ProtocolException($"Unknown message type {(int)type}."),
        };
        reader.ExpectEnd();
        return message;
    }

    /// <summary>
    /// The protocol version of a hello, read without decoding the rest; a client of another
    /// version may lay the rest out differently.
    /// </summary>
    public static bool TryReadHelloVersion(ReadOnlySpan<byte> data, out int version)
    {
        version = 0;
        if (data.Length < 3 || data[0] != (byte)MessageType.Hello)
        {
            return false;
        }

        version = data[1] | (data[2] << 8);
        return true;
    }
}
