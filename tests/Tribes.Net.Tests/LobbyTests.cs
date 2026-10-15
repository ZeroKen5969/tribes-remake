using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class LobbyTests
{
    [Fact]
    public void Players_JoinIntoFreeSlots_WithDistinctColours()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("Alice");
        TestPeer b = cluster.AddClient("Bob", configure: o => o with { PreferredColour = 5 });
        cluster.RunUntilJoined(a, b);
        cluster.Run(TimeSpan.FromMilliseconds(100));

        RoomState room = cluster.Host.Room;
        Assert.Equal(3, room.OccupiedSlots);
        Assert.Equal([0, 1, 2], room.Players.Select(p => p.Slot));
        Assert.Equal(3, room.Players.Select(p => p.Colour).Distinct().Count());
        Assert.Equal(5, room.FindPlayer(b.PublicId)!.Colour);
        Assert.Equal(room.Revision, a.Session.Room!.Revision);
        Assert.Equal("Alice", b.Session.Room!.FindPlayer(a.PublicId)!.Name);
        Assert.True(cluster.Host.LocalPlayer.IsHost);
    }

    [Fact]
    public void Colour_IsUnique_EvenWhenTwoPlayersAskAtOnce()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        TestPeer b = cluster.AddClient("b");
        cluster.RunUntilJoined(a, b);
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal(RequestResult.Accepted, a.Session.ChooseColour(6));
        Assert.Equal(RequestResult.Accepted, b.Session.ChooseColour(6));
        cluster.Run(TimeSpan.FromMilliseconds(200));

        RoomState room = cluster.Host.Room;
        Assert.Equal(6, room.FindPlayer(a.PublicId)!.Colour);
        Assert.NotEqual(6, room.FindPlayer(b.PublicId)!.Colour);
        RequestDeniedEventArgs denied = Assert.Single(b.Denials);
        Assert.Equal(RequestKind.Colour, denied.Request);
        Assert.Equal(RequestResult.ColourTaken, denied.Result);
        Assert.False(string.IsNullOrEmpty(denied.Message));

        // Now that the room is known, the client refuses before asking.
        Assert.Equal(RequestResult.ColourTaken, b.Session.ChooseColour(6));
        Assert.Equal(RequestResult.ColourTaken, cluster.Host.ChooseColour(6));
        Assert.Equal(RequestResult.InvalidValue, b.Session.ChooseColour(8));
        Assert.Equal(RequestResult.InvalidValue, cluster.Host.ChooseColour(-1));
        Assert.Equal(RequestResult.Accepted, cluster.Host.ChooseColour(7));
        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(7, a.Session.Room!.FindPlayer(cluster.Host.LocalPlayer.Id)!.Colour);
    }

    [Fact]
    public void FifthPlayer_IsRejected_BecauseTheRoomIsFull()
    {
        using var cluster = new LoopbackCluster();
        TestPeer[] players = [cluster.AddClient("p1"), cluster.AddClient("p2"), cluster.AddClient("p3")];
        cluster.RunUntilJoined(players);
        TestPeer fifth = cluster.AddClient("p5");
        cluster.RunUntil(() => fifth.Session.Phase == ClientPhase.Ended, "the fifth player's answer");

        Assert.Equal(SessionEndReason.Rejected, fifth.Session.EndReason);
        Assert.Equal(RejectReason.RoomFull, fifth.Session.Rejection);
        Assert.Contains("4", fifth.Session.EndMessage, StringComparison.Ordinal);
        Assert.Equal(4, cluster.Host.Room.OccupiedSlots);
        Assert.All(players, p => Assert.Equal(ClientPhase.Lobby, p.Session.Phase));
    }

    [Fact]
    public void ComputerPlayers_FillSlots_AndCountTowardsTheLimit()
    {
        using var cluster = new LoopbackCluster();
        int first = cluster.Host.AddComputerPlayer(team: 1, aiLevel: 2);
        int second = cluster.Host.AddComputerPlayer();
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(RequestResult.ColourTaken, cluster.Host.ConfigureComputerPlayer(first, cluster.Host.LocalPlayer.Colour, 1, 3));
        Assert.Equal(RequestResult.InvalidValue, cluster.Host.ConfigureComputerPlayer(first, 4, 9, 3));
        Assert.Equal(RequestResult.NotFound, cluster.Host.ConfigureComputerPlayer(3, 4, 1, 3));
        Assert.Equal(RequestResult.Accepted, cluster.Host.ConfigureComputerPlayer(first, 4, 1, 3));
        TestPeer human = cluster.AddClient("human");
        cluster.RunUntilJoined(human);
        Assert.Equal(-1, cluster.Host.AddComputerPlayer());

        TestPeer late = cluster.AddClient("late");
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.Ended, "the rejection");
        Assert.Equal(RejectReason.RoomFull, late.Session.Rejection);

        PlayerInfo computer = cluster.Host.Room.PlayerInSlot(first)!;
        Assert.Equal(PlayerKind.Computer, computer.Kind);
        Assert.Equal((4, 1, 3), (computer.Colour, computer.Team, computer.AiLevel));
        Assert.Equal(RequestResult.Accepted, cluster.Host.RemoveComputerPlayer(second));
        Assert.Equal(RequestResult.NotFound, cluster.Host.RemoveComputerPlayer(second));
        Assert.Equal(RequestResult.NotFound, cluster.Host.RemoveComputerPlayer(human.Session.LocalPlayer!.Slot));
        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(3, human.Session.Room!.OccupiedSlots);

        cluster.StartMatch();
        Assert.Contains(first, human.Simulation.ComputerPlayerSlots);
        Assert.Equal(-1, cluster.Host.AddComputerPlayer());
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.RemoveComputerPlayer(first));
    }

    [Theory]
    [InlineData("9.9.9", LoopbackCluster.ContentVersion, RejectReason.GameVersionMismatch)]
    [InlineData(LoopbackCluster.GameVersion, "other-content", RejectReason.ContentVersionMismatch)]
    public void VersionMismatch_IsRejected_WithAClearReason(string gameVersion, string contentVersion, RejectReason expected)
    {
        using var cluster = new LoopbackCluster();
        TestPeer wrong = cluster.AddClient("wrong", configure: o => o with { GameVersion = gameVersion, ContentVersion = contentVersion });
        cluster.RunUntil(() => wrong.Session.Phase == ClientPhase.Ended, "the rejection");

        Assert.Equal(SessionEndReason.Rejected, wrong.Session.EndReason);
        Assert.Equal(expected, wrong.Session.Rejection);
        string message = Assert.Single(wrong.Ends).Message;
        Assert.Contains(expected == RejectReason.GameVersionMismatch ? gameVersion : contentVersion, message, StringComparison.Ordinal);
        Assert.Contains(expected == RejectReason.GameVersionMismatch ? LoopbackCluster.GameVersion : LoopbackCluster.ContentVersion, message, StringComparison.Ordinal);
        Assert.Single(cluster.Host.Room.Players);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void ProtocolMismatch_IsRejected_EvenWhenTheHelloCannotBeRead(int version)
    {
        using var cluster = new LoopbackCluster();
        using var raw = new RawLoopbackClient(cluster);
        byte[] hello = MessageCodec.Encode(new HelloMessage(version, "x", "y", Guid.NewGuid(), "old", false, -1, Guid.Empty, -1, "", ""));
        raw.Send(version == 99 ? hello[..5] : hello);
        cluster.RunUntil(() => raw.Received.Count > 0, "the answer");

        var reject = Assert.IsType<RejectMessage>(MessageCodec.Decode(raw.Received[0]));
        Assert.Equal(RejectReason.ProtocolMismatch, reject.Reason);
        Assert.Contains(version.ToString(System.Globalization.CultureInfo.InvariantCulture), reject.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Garbage_ClosesTheConnection_WithAProtocolError()
    {
        using var cluster = new LoopbackCluster();
        using var raw = new RawLoopbackClient(cluster);
        raw.Send([200, 1, 2]);
        cluster.RunUntil(() => raw.Disconnected, "the host to close the connection");

        var end = Assert.IsType<SessionEndMessage>(MessageCodec.Decode(raw.Received.Single()));
        Assert.Equal(SessionEndReason.ProtocolError, end.Reason);

        using var second = new RawLoopbackClient(cluster, "raw2");
        second.Send(MessageCodec.Encode(new SetReadyMessage(true)));
        cluster.RunUntil(() => second.Disconnected, "the host to close the second connection");
        Assert.Single(cluster.Host.Room.Players);
    }

    [Fact]
    public void SilentConnection_IsClosed_AfterTheHelloTimeout()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { HelloTimeout = TimeSpan.FromSeconds(1) });
        using var raw = new RawLoopbackClient(cluster);
        cluster.Run(TimeSpan.FromMilliseconds(500));
        Assert.False(raw.Disconnected);
        cluster.RunUntil(() => raw.Disconnected, "the timeout");
    }

    [Fact]
    public void Start_RequiresReadyPlayers_TwoTeams_AndEnoughPlayers()
    {
        using var cluster = new LoopbackCluster();
        Assert.Equal(RequestResult.NotEnoughPlayers, cluster.Host.Start());

        TestPeer a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        Assert.Equal(RequestResult.PlayersNotReady, cluster.Host.CanStart());
        Assert.Equal(RequestResult.Accepted, a.Session.SetReady(true));
        Assert.Equal(RequestResult.Accepted, a.Session.ChooseTeam(0));
        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RequestResult.NeedTwoTeams, cluster.Host.CanStart());
        Assert.Equal(RequestResult.Accepted, cluster.Host.ChooseTeam(2));
        Assert.Equal(RequestResult.InvalidValue, cluster.Host.ChooseTeam(4));
        Assert.Equal(RequestResult.InvalidValue, a.Session.ChooseTeam(-1));
        Assert.Equal(RequestResult.Accepted, cluster.Host.CanStart());

        Assert.Equal(RequestResult.Accepted, a.Session.SetReady(false));
        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RequestResult.PlayersNotReady, cluster.Host.Start());
        Assert.Equal(RoomPhase.Lobby, cluster.Host.Room.Phase);

        cluster.StartMatch([42]);
        Assert.Equal(RoomPhase.InGame, a.Session.Room!.Phase);
        Assert.Equal(new byte[] { 42 }, a.Session.Match!.GameSetup.ToArray());
        Assert.Equal(cluster.Host.Match!.MatchId, a.Session.Match.MatchId);
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.Start());
        Assert.Equal(RequestResult.NotAllowedNow, a.Session.ChooseColour(3));
        Assert.Equal(RequestResult.NotAllowedNow, a.Session.SetReady(true));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.ChooseTeam(1));
    }

    [Fact]
    public void Endless_CanStartAlone_AndTeamsAreNotUsed()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        Assert.Equal(RequestResult.NotAllowedNow, a.Session.ChooseTeam(1));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.ChooseTeam(1));

        using var solo = new LoopbackCluster(RoomMode.Endless);
        Assert.Equal(RequestResult.Accepted, solo.Host.Start());
        solo.Run(TimeSpan.FromSeconds(1));
        Assert.True(solo.HostSimulation.Tick >= 8);
    }

    [Fact]
    public void Chat_ReachesEveryone_AndIsRateLimited()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        TestPeer b = cluster.AddClient("b");
        cluster.RunUntilJoined(a, b);

        Assert.Equal(RequestResult.Accepted, a.Session.SendChat("  hello\u0007 there  "));
        Assert.Equal(RequestResult.Accepted, cluster.Host.SendChat("welcome"));
        Assert.Equal(RequestResult.InvalidValue, a.Session.SendChat("   "));
        Assert.Equal(RequestResult.InvalidValue, cluster.Host.SendChat("\n"));
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal(["hello there", "welcome"], b.Chats.Select(c => c.Text).Order());
        Assert.Equal("a", b.Chats.Single(c => c.Text == "hello there").SenderName);
        Assert.Contains(cluster.HostChats, c => c.SenderId == a.PublicId && c.Text == "hello there");
        Assert.Equal(2, a.Chats.Count);

        for (int i = 0; i < 6; i++)
        {
            b.Session.SendChat("spam " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(RequestResult.RateLimited, Assert.Single(b.Denials).Result);
        Assert.Equal(5, a.Chats.Count(c => c.SenderId == b.PublicId));
        Assert.Equal(RoomLimits.MaxChatLength, RoomLimits.CleanText(new string('x', 500), RoomLimits.MaxChatLength).Length);
    }

    [Fact]
    public void Kick_RemovesThePlayer_AndBansThem()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);

        Assert.Equal(RequestResult.NotFound, cluster.Host.Kick(cluster.Host.LocalPlayer.Id));
        Assert.Equal(RequestResult.NotFound, cluster.Host.Kick(Guid.NewGuid()));
        Assert.Equal(RequestResult.Accepted, cluster.Host.Kick(a.PublicId));
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.Ended, "the kick");
        Assert.Equal(SessionEndReason.Kicked, a.Session.EndReason);
        Assert.Null(cluster.Host.Room.FindPlayer(a.PublicId));

        cluster.Rejoin(a);
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.Ended, "the rejection");
        Assert.Equal(RejectReason.Banned, a.Session.Rejection);
    }

    [Fact]
    public void Spectators_JoinWithoutASlot_UpToTheLimit()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Room = o.Room with { MaxSpectators = 1 } });
        TestPeer player = cluster.AddClient("player");
        TestPeer watcher = cluster.AddClient("watcher", configure: o => o with { Spectate = true });
        cluster.RunUntilJoined(player, watcher);
        TestPeer second = cluster.AddClient("second", configure: o => o with { Spectate = true });
        cluster.RunUntil(() => second.Session.Phase == ClientPhase.Ended, "the rejection");

        Assert.Equal(RejectReason.RoomFull, second.Session.Rejection);
        PlayerInfo info = watcher.Session.LocalPlayer!;
        Assert.False(info.HasSlot);
        Assert.Equal(PlayerPresence.Spectator, info.Presence);
        Assert.Equal(RequestResult.NotAPlayer, watcher.Session.SetReady(true));
        Assert.Equal(1, cluster.Host.Room.SpectatorCount);

        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        Assert.False(watcher.Session.CanSendCommands);
        LoopbackCluster.AssertInSync(10, cluster.HostSimulation, watcher.Simulation, player.Simulation);

        watcher.Session.Leave();
        cluster.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, cluster.Host.Room.SpectatorCount);

        using var closed = new LoopbackCluster(configure: o => o with { Room = o.Room with { AllowSpectators = false } });
        TestPeer refused = closed.AddClient("refused", configure: o => o with { Spectate = true });
        closed.RunUntil(() => refused.Session.Phase == ClientPhase.Ended, "the rejection");
        Assert.Equal(RejectReason.SpectatorsNotAllowed, refused.Session.Rejection);
    }

    [Fact]
    public void LeavingTheLobby_FreesTheSlot()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        TestPeer b = cluster.AddClient("b", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(20) });
        cluster.RunUntilJoined(a, b);
        a.Session.Leave();
        b.Transport.SimulateConnectionLoss();
        cluster.Run(TimeSpan.FromMilliseconds(300));

        Assert.Single(cluster.Host.Room.Players);
        Assert.Equal(SessionEndReason.ConnectionLost, b.Session.EndReason);
        Assert.Equal(RequestResult.NotAllowedNow, a.Session.SendChat("still here?"));
    }

    [Fact]
    public void HostClose_EndsEveryClient()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        cluster.Host.Close();
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.True(cluster.Host.IsClosed);
        Assert.Null(cluster.Host.Address);
        Assert.Equal(SessionEndReason.HostClosed, a.Session.EndReason);
        Assert.Equal(RoomPhase.Closed, cluster.Host.Room.Phase);
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.SendChat("anyone?"));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.Start());
        Assert.Equal(-1, cluster.Host.AddComputerPlayer());
        Assert.Equal(RequestResult.NotFound, cluster.Host.Kick(a.PublicId));
    }

    [Fact]
    public void ConnectingToNobody_EndsWithConnectionLost()
    {
        using var cluster = new LoopbackCluster();
        var transport = new LoopbackTransport(cluster.Network, "lonely");
        using var session = new ClientSession(
            transport,
            new ClientOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "x" },
            start => new HashChainSimulation(start),
            cluster.Clock);
        session.Connect("nobody");
        Assert.Throws<InvalidOperationException>(() => session.Connect("nobody"));
        for (int i = 0; i < 5; i++)
        {
            cluster.Clock.Advance(LoopbackCluster.Frame);
            session.Update();
        }

        Assert.Equal(ClientPhase.Ended, session.Phase);
        Assert.Equal(SessionEndReason.ConnectionLost, session.EndReason);
        Assert.Equal(RequestResult.NotAllowedNow, session.RequestPause());
        Assert.Equal(RequestResult.NotAllowedNow, session.Forfeit());
    }

    [Fact]
    public void RoomList_ShowsWhetherRoomsCanBeJoined()
    {
        using var versus = new LoopbackCluster();
        var endlessTransport = new LoopbackTransport(versus.Network, "endless");
        using var endless = new HostSession(
            endlessTransport,
            versus.HostOptions with { PlayerId = Guid.NewGuid(), Room = new RoomSettings { Name = "World", Mode = RoomMode.Endless } },
            start => new HashChainSimulation(start),
            versus.Clock);
        using var browser = new LoopbackRoomBrowser(versus.Network);
        int changes = 0;
        browser.RoomsChanged += (_, _) => changes++;
        browser.Start();
        browser.Poll();

        Assert.Equal(["endless", "host"], browser.Rooms.Select(r => r.Address));
        RoomInfo world = browser.Rooms[0];
        Assert.Equal("World", world.Room.Name);
        Assert.True(world.Room.IsOpen);
        Assert.True(world.IsCompatible(LoopbackCluster.GameVersion, LoopbackCluster.ContentVersion));
        Assert.False(world.IsCompatible("0.1", LoopbackCluster.ContentVersion));

        TestPeer a = versus.AddClient("a");
        versus.StartMatch();
        Assert.Equal(RequestResult.Accepted, endless.Start());
        endless.Update();
        browser.Poll();
        Assert.False(browser.Rooms[1].Room.IsOpen);
        Assert.True(browser.Rooms[1].Room.InGame);
        Assert.Equal(2, browser.Rooms[1].Room.Players);
        Assert.True(browser.Rooms[0].Room.IsOpen);
        Assert.True(browser.Rooms[0].Room.JoinableInProgress);

        endless.SetJoinInProgress(false);
        browser.Poll();
        Assert.False(browser.Rooms[0].Room.IsOpen);
        var lateSession = new ClientSession(
            new LoopbackTransport(versus.Network, "late"),
            new ClientOptions { GameVersion = LoopbackCluster.GameVersion, ContentVersion = LoopbackCluster.ContentVersion, PlayerId = Guid.NewGuid(), PlayerName = "late" },
            start => new HashChainSimulation(start),
            versus.Clock);
        lateSession.Connect("endless");
        for (int i = 0; i < 5; i++)
        {
            versus.Clock.Advance(LoopbackCluster.Frame);
            endless.Update();
            lateSession.Update();
        }

        Assert.Equal(RejectReason.MatchInProgress, lateSession.Rejection);
        int before = changes;
        endless.Close();
        browser.Poll();
        Assert.Single(browser.Rooms);
        Assert.True(changes > before);
        browser.Shutdown();
        Assert.Empty(browser.Rooms);
        lateSession.Dispose();
    }
}
