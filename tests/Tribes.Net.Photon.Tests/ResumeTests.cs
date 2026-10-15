using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>A host's saved match opens again as an internet room; the players come back by their keys.</summary>
public sealed class ResumeTests
{
    [Fact]
    public void SavedMatch_ResumesAsAnInternetRoom_PlayersComeBack_AndTheForfeitedWatch()
    {
        using var cluster = new PhotonCluster(RoomMode.Endless, seed: 41);
        PhotonNode a = cluster.AddClient("a");
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b");
        cluster.StartMatch([3]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        int slotA = a.Session.LocalPlayer!.Slot;
        Assert.Equal(RequestResult.Accepted, b.Session.Forfeit());
        cluster.RunUntil(() => cluster.Host.Room.FindPlayer(b.PublicId)!.Presence == PlayerPresence.Forfeited, "b's forfeit");
        cluster.Run(TimeSpan.FromSeconds(1));

        // The host saves and closes the room; everybody's session ends.
        byte[] save = cluster.Host.SaveMatch();
        long savedTick = cluster.HostSimulation.Tick;
        uint savedHash = cluster.HostSimulation.Hash;
        string oldRoom = cluster.RoomAddress;
        cluster.Host.Close();
        cluster.Run(TimeSpan.FromMilliseconds(500));
        Assert.All([a, b], p => Assert.Equal(ClientPhase.Ended, p.Session.Phase));

        // Later: the same host opens the save as a new internet room.
        HostSession resumed = cluster.ResumeHost(save);
        Assert.NotEqual(oldRoom, cluster.RoomAddress);
        Assert.True(resumed.IsPaused);
        Assert.Equal(savedTick, cluster.HostSimulation.Tick);
        Assert.Equal(savedHash, cluster.HostSimulation.Hash);

        // Its room list entry shows the returning player their match.
        using var browser = new PhotonRoomBrowser(cluster.Settings, cluster.Cloud.CreateLink, cluster.Clock);
        browser.Start();
        cluster.RunUntil(() =>
        {
            browser.Poll();
            return browser.Rooms.Any(r => r.Address == cluster.RoomAddress);
        }, "the resumed room in the list");
        RoomInfo listed = browser.Rooms.Single(r => r.Address == cluster.RoomAddress);
        Assert.Equal(PlayerPresence.Absent, listed.PresenceOf(a.Id));
        Assert.Equal(PlayerPresence.Forfeited, listed.PresenceOf(b.Id));

        a.Transport.Shutdown();
        b.Transport.Shutdown();
        cluster.JoinAs(a, listed.Address);
        cluster.JoinAs(b, listed.Address);
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.InGame && b.Session.Phase == ClientPhase.InGame, "both to be back");
        Assert.Equal(slotA, a.Session.LocalPlayer!.Slot);
        Assert.True(a.Session.CanSendCommands);
        Assert.False(b.Session.CanSendCommands);
        Assert.Equal(PlayerPresence.Forfeited, b.Session.LocalPlayer!.Presence);

        Assert.Equal(RequestResult.Accepted, resumed.Resume());
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));
        Assert.True(cluster.HostSimulation.Tick > savedTick + 20);
        Assert.Empty(cluster.HostDesyncs);
        PhotonCluster.AssertInSync(20, cluster.HostSimulation, a.Simulation, b.Simulation);
    }
}
