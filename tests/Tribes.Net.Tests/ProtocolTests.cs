using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class ProtocolTests
{
    private static readonly PlayerInfo SamplePlayer = new()
    {
        Id = Guid.NewGuid(),
        Name = "Ünïcode 名前",
        Slot = 2,
        Kind = PlayerKind.Human,
        Colour = 7,
        Team = 3,
        IsReady = true,
        Presence = PlayerPresence.Absent,
        IsConnected = false,
        IsHost = false,
        AiLevel = 0,
    };

    private static readonly RoomState SampleRoom = new()
    {
        RoomId = Guid.NewGuid(),
        Settings = new RoomSettings { Name = "Room", Mode = RoomMode.Endless, MaxPlayers = 3, AllowJoinInProgress = false, AllowSpectators = true, MaxSpectators = 2 },
        Phase = RoomPhase.InGame,
        Revision = 17,
        Players = [SamplePlayer, new PlayerInfo { Id = Guid.NewGuid(), Name = "Watcher", Presence = PlayerPresence.Spectator, IsConnected = true }],
    };

    private static readonly MatchStart SampleStart = new()
    {
        MatchId = Guid.NewGuid(),
        Mode = RoomMode.Versus,
        TickMilliseconds = 120,
        HashIntervalTicks = 8,
        Players = [SamplePlayer],
        GameSetup = new byte[] { 1, 2, 3, 4 },
    };

    public static TheoryData<string> MessageNames => [.. Messages().Keys];

    [Theory]
    [MemberData(nameof(MessageNames))]
    public void EveryMessage_SurvivesARoundTrip(string name)
    {
        NetMessage original = Messages()[name];
        byte[] encoded = MessageCodec.Encode(original);
        NetMessage decoded = MessageCodec.Decode(encoded);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(encoded, MessageCodec.Encode(decoded));
    }

    [Theory]
    [MemberData(nameof(MessageNames))]
    public void EveryTruncatedMessage_IsAProtocolError(string name)
    {
        byte[] encoded = MessageCodec.Encode(Messages()[name]);
        for (int length = 0; length < encoded.Length; length++)
        {
            byte[] cut = encoded[..length];
            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(cut));
        }

        Assert.Throws<ProtocolException>(() => MessageCodec.Decode(encoded.Append((byte)0).ToArray()));
    }

    [Fact]
    public void RoomAndPlayers_KeepEveryField()
    {
        var writer = new PacketWriter(4);
        ModelCodec.WriteRoom(writer, SampleRoom);
        RoomState room = ModelCodec.ReadRoom(new PacketReader(writer.ToArray()));

        Assert.Equal(SampleRoom.RoomId, room.RoomId);
        Assert.Equal(SampleRoom.Settings, room.Settings);
        Assert.Equal(SampleRoom.Phase, room.Phase);
        Assert.Equal(SampleRoom.Revision, room.Revision);
        Assert.Equal(SampleRoom.Players, room.Players);
        Assert.Equal(1, room.SpectatorCount);
        Assert.Null(room.PlayerInSlot(0));
        Assert.True(room.IsColourTaken(7, Guid.Empty));
        Assert.False(room.IsColourTaken(7, SamplePlayer.Id));
    }

    [Fact]
    public void JoinedCommand_CarriesThePlayer()
    {
        PlayerCommand command = PlayerCommand.Joined(SamplePlayer);

        Assert.Equal(CommandKind.PlayerJoined, command.Kind);
        Assert.True(command.IsSystem);
        Assert.Equal(SamplePlayer, command.ReadJoinedPlayer());
        Assert.Throws<InvalidOperationException>(() => PlayerCommand.System(1, CommandKind.PlayerAbsent).ReadJoinedPlayer());
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCommand(4, CommandKind.Game, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCommand(-1, CommandKind.Game, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCommand(0, (CommandKind)77, default));

        var mismatched = new TickBatch(3, [new PlayerCommand(1, CommandKind.PlayerJoined, command.Payload)]);
        byte[] encoded = MessageCodec.Encode(new BatchMessage(mismatched));
        Assert.Throws<ProtocolException>(() => MessageCodec.Decode(encoded));
    }

    [Fact]
    public void Reader_EnforcesLimitsAndValidity()
    {
        var writer = new PacketWriter();
        writer.WriteVarUInt(1000);
        Assert.Throws<ProtocolException>(() => new PacketReader(writer.ToArray()).ReadCount(10, "Count"));
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 2 }).ReadBool());
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 9 }).ReadEnum<RoomMode>("Mode"));
        Assert.Throws<ProtocolException>(() => new PacketReader(Enumerable.Repeat((byte)0xFF, 11).ToArray()).ReadVarUInt());
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F }).ReadVarUInt());
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 2, 0xC3, 0x28 }).ReadString());

        var numbers = new PacketWriter();
        numbers.WriteVarInt(long.MinValue);
        numbers.WriteVarInt(-1);
        numbers.WriteVarUInt(ulong.MaxValue);
        numbers.WriteInt64(-5);
        var reader = new PacketReader(numbers.ToArray());
        Assert.Equal(long.MinValue, reader.ReadVarInt());
        Assert.Equal(-1, reader.ReadVarInt());
        Assert.Equal(ulong.MaxValue, reader.ReadVarUInt());
        Assert.Equal(-5, reader.ReadInt64());
        reader.ExpectEnd();
        Assert.Throws<ProtocolException>(() => new PacketReader(new byte[] { 3 }).ReadInt(0, 2, "Value"));
    }

    [Fact]
    public void HelloVersion_CanBeReadWithoutTheRest()
    {
        byte[] hello = MessageCodec.Encode(new HelloMessage(513, "g", "c", Guid.NewGuid(), "n", false, -1, Guid.Empty, -1, "", ""));
        Assert.True(MessageCodec.TryReadHelloVersion(hello, out int version));
        Assert.Equal(513, version);
        Assert.False(MessageCodec.TryReadHelloVersion(new byte[] { 2, 1, 0 }, out _));
        Assert.False(MessageCodec.TryReadHelloVersion(new byte[] { 1 }, out _));
        Assert.Throws<ProtocolException>(() => MessageCodec.Decode(new byte[] { 99 }));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(5000, true)]
    [InlineData(200_000, true)]
    public void Snapshot_PacksAndAssembles(int length, bool compressible)
    {
        byte[] raw = new byte[length];
        if (compressible)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = (byte)(i % 7);
            }
        }
        else
        {
            new Random(length).NextBytes(raw);
        }

        PackedSnapshot packed = SnapshotPacker.Pack(raw, 1024);
        Assert.Equal(compressible, packed.Compressed);
        var header = new SnapshotBeginMessage(SampleStart, 9, packed.RawLength, packed.Checksum, packed.Compressed, packed.Chunks.Count, 100, false, Guid.Empty, TestData.NoLedger);
        var assembler = new SnapshotAssembler(header, 1 << 20);
        for (int i = 0; i < packed.Chunks.Count; i++)
        {
            Assert.Equal(i == packed.Chunks.Count - 1, assembler.Add(new SnapshotChunkMessage(i, packed.Chunks[i])));
        }

        Assert.Equal(raw, assembler.Complete());
    }

    [Fact]
    public void Snapshot_RejectsWrongOrder_WrongSize_AndDamage()
    {
        byte[] raw = Enumerable.Range(0, 4000).Select(i => (byte)(i % 3)).ToArray();
        PackedSnapshot packed = SnapshotPacker.Pack(raw, 512);
        SnapshotBeginMessage Header(uint checksum, int length) =>
            new(SampleStart, 0, length, checksum, packed.Compressed, packed.Chunks.Count, 100, false, Guid.Empty, TestData.NoLedger);

        Assert.Throws<ProtocolException>(() => new SnapshotAssembler(Header(packed.Checksum, 5000), 4096));
        var outOfOrder = new SnapshotAssembler(Header(packed.Checksum, packed.RawLength), 1 << 20);
        Assert.Throws<ProtocolException>(() => outOfOrder.Add(new SnapshotChunkMessage(1, packed.Chunks[0])));
        var tooLarge = new SnapshotAssembler(Header(packed.Checksum, 10), 10);
        Assert.Throws<ProtocolException>(() => tooLarge.Add(new SnapshotChunkMessage(0, new byte[11])));

        var wrongChecksum = new SnapshotAssembler(Header(packed.Checksum ^ 1, packed.RawLength), 1 << 20);
        foreach ((byte[] chunk, int index) in packed.Chunks.Select((c, i) => (c, i)))
        {
            wrongChecksum.Add(new SnapshotChunkMessage(index, chunk));
        }

        Assert.Throws<ProtocolException>(() => wrongChecksum.Complete());
        Assert.Throws<ProtocolException>(() => SnapshotPacker.Decompress([1, 2, 3, 4], 10));
        Assert.Throws<ProtocolException>(() => SnapshotPacker.Decompress(SnapshotPacker.Compress(raw), 100));
        Assert.Throws<ProtocolException>(() => SnapshotPacker.Decompress(SnapshotPacker.Compress(raw), 5000));

        var uncompressed = new SnapshotBeginMessage(SampleStart, 0, 3, Fnv1a.Hash([1, 2, 3]), false, 1, 100, false, Guid.Empty, TestData.NoLedger);
        var shortBody = new SnapshotAssembler(uncompressed, 100);
        shortBody.Add(new SnapshotChunkMessage(0, new byte[] { 1, 2 }));
        Assert.Throws<ProtocolException>(() => shortBody.Complete());
    }

    [Fact]
    public void RoomLimits_CleanText_RemovesControlCharacters_AndKeepsSurrogatePairsWhole()
    {
        Assert.Equal(string.Empty, RoomLimits.CleanText(null, 10));
        Assert.Equal("ab", RoomLimits.CleanText(" a\tb\r\n ", 10));
        Assert.Equal("ab", RoomLimits.CleanText("ab😀", 3));
        Assert.Equal("ab😀", RoomLimits.CleanText("ab😀", 4));
        Assert.True(RoomLimits.IsValidColour(0) && RoomLimits.IsValidColour(7) && !RoomLimits.IsValidColour(8));
        Assert.True(RoomLimits.IsValidTeam(3) && !RoomLimits.IsValidTeam(4));
        Assert.True(RoomLimits.IsValidSpeed(25) && RoomLimits.IsValidSpeed(400) && !RoomLimits.IsValidSpeed(401));
    }

    [Fact]
    public void Options_AreValidated()
    {
        Assert.Null(new LockstepOptions().FindProblem());
        Assert.NotNull(new LockstepOptions { TickMilliseconds = 5 }.FindProblem());
        Assert.NotNull(new LockstepOptions { MinInputDelayTicks = 5, MaxInputDelayTicks = 2 }.FindProblem());
        Assert.NotNull(new LockstepOptions { HashIntervalTicks = -1 }.FindProblem());
        Assert.NotNull(new LockstepOptions { MaxHostStepsPerUpdate = 0 }.FindProblem());
        Assert.NotNull(new LockstepOptions { SnapshotChunkBytes = 10 }.FindProblem());
        Assert.NotNull(new LockstepOptions { MaxCommandBytes = 0 }.FindProblem());
        Assert.Null(new RoomSettings().FindProblem());
        Assert.NotNull(new RoomSettings { MaxPlayers = 5 }.FindProblem());
        Assert.NotNull(new RoomSettings { MaxSpectators = 9 }.FindProblem());
        Assert.NotNull(new RoomSettings { Mode = (RoomMode)5 }.FindProblem());
    }

    [Fact]
    public void Clocks_MoveForward()
    {
        var manual = new ManualClock();
        manual.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), manual.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.Advance(TimeSpan.FromSeconds(-1)));
        TimeSpan first = SystemClock.Instance.Now;
        Assert.True(SystemClock.Instance.Now >= first);
        Assert.NotEmpty(new ProtocolException().Message);
        Assert.IsType<InvalidOperationException>(new ProtocolException("x", new InvalidOperationException()).InnerException);
    }

    private static Dictionary<string, NetMessage> Messages()
    {
        var batch = new TickBatch(42, [
            PlayerCommand.System(1, CommandKind.PlayerAbsent),
            PlayerCommand.Joined(SamplePlayer),
            new PlayerCommand(0, CommandKind.Game, new byte[] { 9, 8, 7 }),
        ]);
        NetMessage[] all =
        [
            new HelloMessage(NetProtocol.Version, "1.0", "c", Guid.NewGuid(), "Name", true, 3, Guid.NewGuid(), 77, "peer-7", "rules-1"),
            new ChatMessage("hi"),
            new ChooseColourMessage(4),
            new ChooseTeamMessage(2),
            new SetReadyMessage(true),
            new LeaveMessage(),
            new ForfeitMessage(),
            new CommandMessage(12, 3, new byte[] { 1, 2 }),
            new StateHashMessage(16, 0xCAFEBABE),
            new ControlRequestMessage(ControlKind.Speed, 150),
            new WelcomeMessage(SampleRoom, new RoomExtras([Guid.NewGuid()], [new Successor(Guid.NewGuid(), "a")]), -1, Guid.NewGuid(), 5),
            new RejectMessage(RejectReason.RoomFull, "full"),
            new RoomUpdateMessage(SampleRoom, RoomExtras.Empty),
            new ChatRelayMessage(Guid.NewGuid(), "Bob", "hello"),
            new RequestDeniedMessage(RequestKind.Colour, RequestResult.ColourTaken, "taken"),
            new MatchBeginMessage(SampleStart, 100, Guid.NewGuid()),
            new SnapshotBeginMessage(SampleStart, 30, 1000, 1234, true, 3, 200, true, Guid.NewGuid(), new LedgerState([SamplePlayer], [1, 2, 3, 4])),
            new SnapshotChunkMessage(2, new byte[] { 5, 5, 5 }),
            new BatchMessage(batch),
            new ControlStateMessage(true, 50),
            new DesyncNoticeMessage(24, 16, Guid.NewGuid(), 1, 2),
            new SessionEndMessage(SessionEndReason.Kicked, "bye"),
        ];
        return all.ToDictionary(m => m.Type.ToString());
    }
}
