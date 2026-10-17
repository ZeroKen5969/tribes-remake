using System;
using System.Collections.Generic;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>A client must not trust its host: anything out of place ends the session cleanly.</summary>
public sealed class HostileHostTests : IDisposable
{
    private static readonly RoomAdvertisement Advertisement = new() { RoomId = Guid.NewGuid(), Name = "fake", GameVersion = "1", ContentVersion = "1" };
    private static readonly byte[] RejectedSetup = [0xEE, 0xEE];

    private readonly ManualClock clock = new();
    private readonly LoopbackTransport fake;
    private readonly Guid playerId = Guid.NewGuid();
    private readonly List<NetMessage> fromClient = [];
    private readonly ClientSession client;
    private IConnection? toClient;

    public HostileHostTests()
    {
        var network = new LoopbackNetwork(clock);
        fake = new LoopbackTransport(network, "fake");
        fake.PeerConnected += (_, e) => toClient = e.Connection;
        fake.MessageReceived += (_, e) => fromClient.Add(MessageCodec.Decode(e.Data));
        fake.StartHost(Advertisement);
        client = new ClientSession(
            new LoopbackTransport(network, "client"),
            new ClientOptions
            {
                GameVersion = "1",
                ContentVersion = "1",
                PlayerId = playerId,
                PlayerName = "client",
                ReconnectAttempts = 0,
                Lockstep = new LockstepOptions { MaxSnapshotBytes = 1000 },
            },
            start => start.GameSetup.Span.SequenceEqual(RejectedSetup) ? throw new FormatException("unknown map") : new HashChainSimulation(start),
            clock);
        client.Connect("fake");
        Pump();
    }

    public static TheoryData<string> Attacks =>
    [
        "garbage", "client message", "welcome twice", "match before welcome", "batch in lobby",
        "chunk without header", "snapshot in lobby", "resume without simulation", "batch gap",
    ];

    [Fact]
    public void Hello_IntroducesThePlayer()
    {
        var hello = Assert.IsType<HelloMessage>(Assert.Single(fromClient));
        Assert.Equal(NetProtocol.Version, hello.ProtocolVersion);
        Assert.Equal(playerId, hello.PlayerId);
        Assert.Equal("client", hello.Name);
        Assert.Equal(-1, hello.ResumeTick);
        Assert.Equal(Guid.Empty, hello.TimelineId);
    }

    [Theory]
    [MemberData(nameof(Attacks))]
    public void MisbehavingHost_EndsTheSession_WithAProtocolError(string attack)
    {
        MatchStart start = Start();
        switch (attack)
        {
            case "garbage":
                SendRaw([250, 1]);
                break;
            case "client message":
                Send(new ChatMessage("hi"));
                break;
            case "welcome twice":
                Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
                Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
                break;
            case "match before welcome":
                Send(new MatchBeginMessage(start, 100, Guid.NewGuid()));
                break;
            case "batch in lobby":
                Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
                Send(new BatchMessage(new TickBatch(0, [])));
                break;
            case "chunk without header":
                Send(new WelcomeMessage(Room(RoomPhase.InGame), RoomExtras.Empty, -1, Guid.Empty, 0));
                Send(new SnapshotChunkMessage(0, new byte[] { 1 }));
                break;
            case "snapshot in lobby":
                Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
                Send(new SnapshotBeginMessage(start, 0, 1, 0, false, 1, 100, false, Guid.NewGuid(), TestData.NoLedger));
                break;
            case "resume without simulation":
                Send(new WelcomeMessage(Room(RoomPhase.InGame), RoomExtras.Empty, 5, Guid.NewGuid(), 0));
                break;
            default:
                Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
                Send(new MatchBeginMessage(start, 100, Guid.NewGuid()));
                Send(new BatchMessage(new TickBatch(3, [])));
                break;
        }

        Assert.Equal(ClientPhase.Ended, client.Phase);
        Assert.Equal(SessionEndReason.ProtocolError, client.EndReason);
        Assert.False(string.IsNullOrEmpty(client.EndMessage));
        Assert.Empty(fake.Connections);
    }

    [Fact]
    public void HostMessages_ReachTheGame_AsEvents()
    {
        var denied = new List<RequestDeniedEventArgs>();
        var chats = new List<ChatReceivedEventArgs>();
        var ended = new List<SessionEndedEventArgs>();
        client.RequestDenied += (_, e) => denied.Add(e);
        client.ChatReceived += (_, e) => chats.Add(e);
        client.Ended += (_, e) => ended.Add(e);

        Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
        Send(new RequestDeniedMessage(RequestKind.Team, RequestResult.InvalidValue, "no"));
        Send(new ChatRelayMessage(Guid.NewGuid(), "host", "hello"));
        Send(new SessionEndMessage(SessionEndReason.Kicked, "bye"));

        Assert.Equal((RequestKind.Team, RequestResult.InvalidValue, "no"), (denied[0].Request, denied[0].Result, denied[0].Message));
        Assert.Equal(("host", "hello"), (chats[0].SenderName, chats[0].Text));
        Assert.Equal(SessionEndReason.Kicked, Assert.Single(ended).Reason);
        Assert.Equal("bye", client.EndMessage);
    }

    [Fact]
    public void OversizedSnapshot_EndsTheSession_AsAWorldTooLarge()
    {
        Send(new WelcomeMessage(Room(RoomPhase.InGame), RoomExtras.Empty, -1, Guid.Empty, 0));
        Send(new SnapshotBeginMessage(Start(), 0, 5000, 0, false, 1, 100, false, Guid.NewGuid(), TestData.NoLedger));

        Assert.Equal(ClientPhase.Ended, client.Phase);
        Assert.Equal(SessionEndReason.WorldTooLarge, client.EndReason);
        Assert.Contains("5000", client.EndMessage, StringComparison.Ordinal);
        Assert.Null(client.SnapshotDownload);
        Assert.Empty(fake.Connections);
    }

    [Fact]
    public void SnapshotOfAnotherTick_EndsTheSession_WithAProtocolError()
    {
        Send(new WelcomeMessage(Room(RoomPhase.InGame), RoomExtras.Empty, -1, Guid.Empty, 0));
        byte[] snapshot = new HashChainSimulation(Start()).SaveSnapshot();
        PackedSnapshot packed = SnapshotPacker.Pack(snapshot, 1024);
        Send(new SnapshotBeginMessage(Start(), 7, packed.RawLength, packed.Checksum, packed.Compressed, 1, 100, false, Guid.NewGuid(), TestData.NoLedger));
        Send(new SnapshotChunkMessage(0, packed.Chunks[0]));

        Assert.Equal(SessionEndReason.ProtocolError, client.EndReason);
        Assert.Contains("tick 7", client.EndMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DataTheGameCannotLoad_EndsTheSession_WithAProtocolError()
    {
        Send(new WelcomeMessage(Room(RoomPhase.Lobby), RoomExtras.Empty, -1, Guid.Empty, 0));
        Send(new MatchBeginMessage(Start() with { GameSetup = RejectedSetup }, 100, Guid.NewGuid()));

        Assert.Equal(SessionEndReason.ProtocolError, client.EndReason);
        Assert.Contains("unknown map", client.EndMessage, StringComparison.Ordinal);
        Assert.Null(client.Simulation);
    }

    public void Dispose()
    {
        client.Dispose();
        fake.Dispose();
    }

    private MatchStart Start() => new()
    {
        MatchId = Guid.NewGuid(),
        Mode = RoomMode.Versus,
        TickMilliseconds = 120,
        HashIntervalTicks = 8,
        Players = Room(RoomPhase.InGame).Players,
    };

    private RoomState Room(RoomPhase phase) => new()
    {
        RoomId = Advertisement.RoomId,
        Settings = new RoomSettings(),
        Phase = phase,
        Players = [new PlayerInfo { Id = playerId, Name = "client", Slot = 1, Colour = 1, Presence = PlayerPresence.Active, IsConnected = true }],
    };

    private void Send(NetMessage message) => SendRaw(MessageCodec.Encode(message));

    private void SendRaw(byte[] data)
    {
        if (toClient is { IsConnected: true })
        {
            toClient.Send(data);
        }

        Pump();
    }

    private void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            fake.Poll();
            client.Update();
        }
    }
}
