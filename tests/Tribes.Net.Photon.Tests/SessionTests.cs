using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Tests.Support;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The unchanged host and client sessions over the Photon transport.</summary>
public sealed class SessionTests
{
    [Fact]
    public void Lobby_Match_AndHashes_WorkOverPhoton()
    {
        using var cluster = new PhotonCluster(RoomMode.Versus, seed: 3);
        PhotonNode a = cluster.AddClient("a", latencyMs: 30);
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b", latencyMs: 60);
        PhotonNode c = cluster.AddClient("c", latencyMs: 90);
        cluster.StartMatch([5]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(20));

        Assert.True(cluster.HostSimulation.Tick > 150, "The match should run.");
        Assert.Empty(cluster.HostDesyncs);
        Assert.All([a, b, c], p => Assert.Empty(p.Desyncs));
        PhotonCluster.AssertInSync(140, cluster.HostSimulation, a.Simulation, b.Simulation, c.Simulation);

        // The far players got a longer input delay than the near one, measured through Photon.
        Assert.True(c.Session.InputDelayTicks >= a.Session.InputDelayTicks);
        Assert.InRange(c.Transport.Connections[0].RoundTripTime.TotalMilliseconds, 200, 260);
    }

    [Fact]
    public void RoomList_ShowsTheHostedRoom_WithItsMembers()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless);
        PhotonNode a = cluster.AddClient("a");
        cluster.StartMatch();
        using var browser = new PhotonRoomBrowser(cluster.Settings, cluster.Cloud.CreateLink, cluster.Clock);
        browser.Start();
        cluster.RunUntil(() =>
        {
            browser.Poll();
            return browser.Rooms.Count == 1 && browser.Rooms[0].Room.InGame;
        }, "the room list");

        var room = browser.Rooms[0];
        Assert.Equal(cluster.RoomAddress, room.Address);
        Assert.Equal("Test room", room.Room.Name);
        Assert.True(room.IsCompatible(PhotonCluster.GameVersion, PhotonCluster.ContentVersion));
        Assert.Equal(PlayerPresence.Active, room.PresenceOf(a.Id));
        Assert.Null(room.PresenceOf(Guid.NewGuid()));
    }

    [Fact]
    public void EndlessRoom_LetsAPlayerJoinTheRunningMatch()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless, seed: 4);
        PhotonNode a = cluster.AddClient("a");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        cluster.Host.SetJoinInProgress(true);
        PhotonNode late = cluster.AddClient("late", latencyMs: 40);
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.InGame, "the late player to join");
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        Assert.True(late.MatchStarts[^1].FromSnapshot);
        Assert.True(late.Session.CanSendCommands);
        Assert.Contains(cluster.HostPresence, p => p.Change == CommandKind.PlayerJoined);
        PhotonCluster.AssertInSync(40, cluster.HostSimulation, a.Simulation, late.Simulation);
    }

    [Fact]
    public void PlayerWhoLeaves_ComesBackToTheSameSlot()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless, seed: 5);
        PhotonNode a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        int slot = b.Session.LocalPlayer!.Slot;

        b.Session.Leave();
        cluster.RunUntil(() => cluster.Host.Room.PlayerInSlot(slot)!.Presence == PlayerPresence.Absent, "b to be away");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        cluster.Rejoin(b);
        cluster.RunUntil(() => b.Session.Phase == ClientPhase.InGame, "b to come back");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Equal(slot, b.Session.LocalPlayer!.Slot);
        Assert.Equal(PlayerPresence.Active, cluster.Host.Room.PlayerInSlot(slot)!.Presence);
        Assert.Contains(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned && p.Slot == slot);
        PhotonCluster.AssertInSync(20, cluster.HostSimulation, a.Simulation, b.Simulation);
    }

    [Fact]
    public void PlayerWhoseNetworkFails_ReconnectsByItself_AndCatchesUp()
    {
        using var cluster = new PhotonCluster(RoomMode.Versus, seed: 6);
        PhotonNode a = cluster.AddClient("a", configure: o => o with { ReconnectDelay = TimeSpan.FromSeconds(1) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        a.Link.LoseNetwork();
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.Reconnecting, "a to notice");
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.InGame, "a to be back", TimeSpan.FromSeconds(30));
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Equal(2, a.Links.Count);
        Assert.Empty(cluster.HostDesyncs);
        Assert.Empty(a.Desyncs);
        PhotonCluster.AssertInSync(30, cluster.HostSimulation, a.Simulation);
    }

    [Fact]
    public void BigWorlds_ReachLatePlayers_InPieces()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless, snapshotPadding: 900_000);
        PhotonNode a = cluster.AddClient("a");
        cluster.StartMatch(new byte[200_000]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        cluster.Host.SetJoinInProgress(true);

        PhotonNode late = cluster.AddClient("late");
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.InGame, "the snapshot to arrive", TimeSpan.FromSeconds(30));
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        PhotonCluster.AssertInSync(10, cluster.HostSimulation, a.Simulation, late.Simulation);
    }

    [Fact]
    public void HostClosingTheLobby_EndsEveryClient()
    {
        using var cluster = new PhotonCluster();
        PhotonNode a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        cluster.Host.Close();
        cluster.Run(TimeSpan.FromMilliseconds(300));

        Assert.Equal(SessionEndReason.HostClosed, a.Session.EndReason);
        Assert.Equal(1, cluster.Cloud.Connected);
    }

    [Fact]
    public void ClientThatCannotReachTheRoom_EndsWithConnectionLost()
    {
        using var cluster = new PhotonCluster();
        PhotonNode a = cluster.AddClient("a", address: "jp/QQQQQQ");
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.Ended, "a to give up");

        Assert.Equal(PhotonError.RoomNotFound, a.Transport.LastError);
        Assert.Contains(a.Ends, e => e.Reason == SessionEndReason.ConnectionLost);
    }
}
