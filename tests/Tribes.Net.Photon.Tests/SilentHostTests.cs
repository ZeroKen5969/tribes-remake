using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>
/// A host that stops answering (crashed, hung, or cut off) while the room still lists it: after
/// <see cref="PhotonSettings.HostSilenceTimeout"/> without any traffic from it, the clients treat
/// it as gone and take over, instead of waiting for Photon's own timeout.
/// </summary>
public sealed class SilentHostTests
{
    [Fact]
    public void SilentHost_CountsAsGone_WhileTheServerStillAnswers()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        TimeSpan frozenAt = rig.Clock.Now;
        host.Frozen = true;
        rig.RunUntil(() => a.Disconnected.Count == 1, "a to give up on the host", TimeSpan.FromSeconds(10));

        Assert.Equal(DisconnectReason.HostLeft, a.Disconnected[0].Reason);
        Assert.InRange((rig.Clock.Now - frozenAt).TotalSeconds, 3, 4);

        // Photon still lists the old host, but for this client it is gone.
        Assert.Contains(1, a.Link.Actors);
        Assert.Null(a.Transport.RoomHostAddress);
        Assert.False(a.Transport.IsPeerPresent("actor:1"));
        Assert.False(a.Transport.IsPeerPresent(host.Transport.HostAddress!));
        Assert.Null(a.Transport.TakeOverHost(TransportRig.Advert("Taken over")));
        Assert.Equal(TransportRole.Host, a.Transport.Role);
    }

    [Fact]
    public void QuietHost_ThatStillPings_IsNotSilent()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");

        // No messages for a long time (a paused match): the transport's pings still go both ways.
        rig.Run(TimeSpan.FromSeconds(15));

        Assert.Empty(a.Disconnected);
        Assert.Equal("actor:1", a.Transport.RoomHostAddress);
    }

    [Fact]
    public void ClientWhoseOwnLinkIsDown_DoesNotBlameTheHost()
    {
        using var rig = new TransportRig();
        rig.Cloud.ClientTimeout = TimeSpan.FromSeconds(8);
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.LoseNetwork();
        rig.RunUntil(() => a.Disconnected.Count == 1, "a to notice", TimeSpan.FromSeconds(20));

        Assert.Equal(DisconnectReason.ConnectionLost, a.Disconnected[0].Reason);
    }

    [Fact]
    public void AnotherHost_EndsTheConnectionToTheOldOne_AtOnce()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        b.Link.SetRoomProperties(new Dictionary<string, object> { [Wire.HostProperty] = 3 }, null);
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal(DisconnectReason.HostLeft, Assert.Single(a.Disconnected).Reason);
        Assert.Equal("actor:3", a.Transport.RoomHostAddress);
    }

    [Fact]
    public void SilenceTimeout_CanBeTurnedOff()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Add("a", configure: s => s with { HostSilenceTimeout = TimeSpan.Zero });
        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Connected.Count == 1, "a to connect");
        host.Frozen = true;
        rig.Run(TimeSpan.FromSeconds(10));

        Assert.Empty(a.Disconnected);
        Assert.NotNull((PhotonSettings.ForAppId(FakePhotonCloud.AppId) with { HostSilenceTimeout = TimeSpan.FromSeconds(-1) }).FindProblem());
    }

    [Fact]
    public void HungHost_IsReplacedWithinSeconds_AndTheMatchGoesOn()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;

        // The host's game hangs: its device stays in the room (Photon keeps it), but nothing comes from it.
        TimeSpan hungAt = cluster.Clock.Now;
        cluster.HostNode.Frozen = true;
        while (cluster.Host == oldHost)
        {
            cluster.RunWithCommands(TimeSpan.FromMilliseconds(100), chancePerFrame: 0.5);
            Assert.True(cluster.Clock.Now - hungAt < TimeSpan.FromSeconds(10), "Nobody took over.");
        }

        TimeSpan takeover = cluster.Clock.Now - hungAt;
        cluster.RunUntil(() => b.Session.Phase == ClientPhase.InGame && c.Session.Phase == ClientPhase.InGame, "the others to follow");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        cluster.Run(TimeSpan.FromSeconds(2));

        Assert.Same(a.Hosting, cluster.Host);
        Assert.InRange(takeover.TotalSeconds, 3, 5);
        Assert.All([b, c], p => Assert.Contains(ClientPhase.Migrating, p.Phases));
        Assert.Empty(cluster.HostDesyncs);
        PhotonCluster.AssertInSync(20, a.Simulation, b.Simulation, c.Simulation);
        AssertEveryCommandRanOnce(a.Simulation, b, c);
    }

    [Fact]
    public void HungHostThatWakesUp_StepsDown_WithoutForkingTheMatch()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession oldHost = cluster.Host;
        HashChainSimulation oldSimulation = cluster.HostSimulation;
        cluster.HostNode.Frozen = true;
        cluster.RunUntil(() => cluster.Host != oldHost && b.Session.Phase == ClientPhase.InGame && c.Session.Phase == ClientPhase.InGame,
            "the match to move on", TimeSpan.FromSeconds(20));

        // The old host's game runs again: it still believes it hosts, but the room says otherwise.
        cluster.HostNode.Frozen = false;
        for (int frame = 0; frame < 300; frame++)
        {
            oldHost.Update();
            cluster.Step();
        }

        Assert.True(cluster.HostNode.Transport.HostingLost);
        Assert.Equal(PhotonError.HostReplaced, cluster.HostNode.Transport.LastError);
        Assert.Empty(cluster.HostNode.Transport.Connections);
        Assert.Empty(cluster.HostDesyncs);
        Assert.All([a, b, c], p => Assert.Single(p.Simulations));
        PhotonCluster.AssertInSync(20, a.Simulation, b.Simulation, c.Simulation);
        Assert.True(oldSimulation.Tick > 0);
    }

    [Fact]
    public void PausedMatch_KeepsItsHost()
    {
        (PhotonCluster cluster, PhotonNode a, PhotonNode b, PhotonNode c) = Match();
        using PhotonCluster owned = cluster;
        HostSession host = cluster.Host;
        Assert.Equal(RequestResult.Accepted, host.Pause());
        cluster.Run(TimeSpan.FromSeconds(12));
        Assert.Equal(RequestResult.Accepted, host.Resume());
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Same(host, cluster.Host);
        Assert.All([a, b, c], p => Assert.DoesNotContain(ClientPhase.Migrating, p.Phases));
    }

    private static (PhotonCluster Cluster, PhotonNode A, PhotonNode B, PhotonNode C) Match()
    {
        var cluster = new PhotonCluster(RoomMode.Endless, seed: 31);
        PhotonNode a = cluster.AddClient("a", 20);
        cluster.RunUntilJoined(a);
        PhotonNode b = cluster.AddClient("b", 40);
        cluster.RunUntilJoined(b);
        PhotonNode c = cluster.AddClient("c", 60);
        cluster.StartMatch([1]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        return (cluster, a, b, c);
    }

    private static void AssertEveryCommandRanOnce(HashChainSimulation finalHost, params PhotonNode[] clients)
    {
        foreach (PhotonNode client in clients)
        {
            int slot = client.Session.LocalPlayer!.Slot;
            long[] ran = [.. finalHost.Log
                .Where(e => e.Command.Slot == slot && !e.Command.IsSystem)
                .Select(e => e.Command.Sequence)];
            Assert.Equal(Enumerable.Range(1, client.CommandsSent).Select(i => (long)i), ran.Order());
        }
    }
}
