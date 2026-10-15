using System;
using System.Linq;
using Tribes.Net.Photon.Link;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The transport against the in-memory Photon cloud: rooms, connections, messages, errors.</summary>
public sealed class TransportTests
{
    [Fact]
    public void Host_CreatesARoom_ThatNamesItsHost()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        rig.Run(TimeSpan.FromMilliseconds(100));

        string address = host.Transport.HostAddress!;
        Assert.True(PhotonAddress.TryParseRoom(address, out string? region, out string code));
        Assert.Equal("jp", region);
        Assert.Equal(host.Transport.RoomCode, code);
        Assert.Equal(PhotonStatus.Connected, host.Transport.Status);
        Assert.Equal(TransportRole.Host, host.Transport.Role);
        FakeRoom room = rig.Cloud.FindRoom("jp", code)!;
        Assert.Equal(1, room.Properties[Wire.HostProperty]);
        Assert.Contains(host.Link.Operations, o => o.StartsWith("create ", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Link.Operations, o => o.StartsWith("set h", StringComparison.Ordinal));
        Assert.Equal("Room", Wire.DecodeAdvertisement(room.Properties[Wire.AdvertisementProperty])!.Value.Room.Name);
        Assert.Equal(1, room.Master);
        Assert.Equal(PhotonTransport.MaxRoomActors, room.MaxPlayers);
        Assert.Null(host.Transport.MigrationAddress);
        Assert.True(host.StatusChanges > 0);
    }

    [Fact]
    public void ClientAndHost_ExchangeMessages_InOrder()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer client = rig.Client(host, "a");

        IConnection toHost = client.Connected[0];
        IConnection toClient = host.Connected[0];
        Assert.Equal("actor:1", toHost.RemoteAddress);
        Assert.Equal("actor:2", toClient.RemoteAddress);
        Assert.Same(toHost, Assert.Single(client.Transport.Connections));
        for (byte i = 0; i < 20; i++)
        {
            toHost.Send([i]);
            toClient.Send([(byte)(100 + i)]);
        }

        rig.Run(TimeSpan.FromMilliseconds(200));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => (byte)i), host.Messages.Select(m => m.Data[0]));
        Assert.Equal(Enumerable.Range(100, 20).Select(i => (byte)i), client.Messages.Select(m => m.Data[0]));
        Assert.All(host.Messages, m => Assert.Same(toClient, m.Connection));
        Assert.Equal("actor:2", client.Transport.MigrationAddress);
        Assert.Equal("actor:1", client.Transport.RoomHostAddress);
        Assert.True(client.Transport.IsPeerPresent("actor:1"));
        Assert.True(client.Transport.IsPeerPresent(host.Transport.HostAddress!));
        Assert.False(client.Transport.IsPeerPresent("actor:9"));
        Assert.False(client.Transport.IsPeerPresent("nonsense"));
    }

    [Fact]
    public void SendToMany_ReachesEveryRecipient_WithOneEvent()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        int before = rig.Cloud.EventsSent;

        host.Transport.SendToMany([.. host.Transport.Connections, host.Transport.Connections[0]], [7, 7]);
        rig.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal([7, 7], Assert.Single(a.Messages).Data);
        Assert.Equal([7, 7], Assert.Single(b.Messages).Data);
        Assert.InRange(rig.Cloud.EventsSent - before, 2, 8);
    }

    [Fact]
    public void LargeMessages_AreSplit_AndPutBackTogether()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        byte[] big = new byte[NetProtocol.MaxMessageBytes];
        new Random(5).NextBytes(big);
        int before = rig.Cloud.EventsSent;

        host.Transport.Connections[0].Send(big);
        host.Transport.Connections[0].Send([1, 2, 3]);
        rig.RunUntil(() => a.Messages.Count == 2, "both messages");

        Assert.Equal(2, a.Messages.Count);
        Assert.Equal(big, a.Messages[0].Data);
        Assert.Equal([1, 2, 3], a.Messages[1].Data);
        Assert.True(rig.Cloud.EventsSent - before > NetProtocol.MaxMessageBytes / (64 * 1024));
        Assert.Throws<ArgumentException>(() => host.Transport.Connections[0].Send(new byte[NetProtocol.MaxMessageBytes + 1]));
        Assert.Throws<ArgumentException>(() => host.Transport.SendToMany(host.Transport.Connections, new byte[NetProtocol.MaxMessageBytes + 1]));
    }

    [Fact]
    public void LargeTransfers_AreFlowControlled_SoPhotonKeepsTheReceiver()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a", latencyMs: 60);
        a.Link.DownlinkBytesPerSecond = 300 * 1024;
        for (int i = 0; i < 96; i++)
        {
            byte[] chunk = new byte[32 * 1024];
            chunk[0] = (byte)i;
            host.Transport.Connections[0].Send(chunk);
        }

        rig.RunUntil(() => a.Messages.Count == 96 || a.Disconnected.Count > 0, "3 MB to arrive", TimeSpan.FromSeconds(60));

        Assert.Empty(a.Disconnected);
        Assert.Equal(Enumerable.Range(0, 96).Select(i => (byte)i), a.Messages.Select(m => m.Data[0]));
        // The window, plus the few bytes of the pings and credits in between.
        Assert.InRange(a.Link.PeakQueuedBytes, 1, PhotonSettings.ForAppId(FakePhotonCloud.AppId).SendWindowBytes + 1024);
    }

    [Fact]
    public void WithoutFlowControl_PhotonWouldDropTheReceiver()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Add("host", configure: s => s with { SendWindowBytes = 64 * 1024 * 1024 });
        host.Transport.StartHost(TransportRig.Advert());
        rig.RunUntil(() => host.Transport.HostAddress is not null, "the room");
        RigPeer a = rig.Client(host, "a", latencyMs: 60);
        a.Link.DownlinkBytesPerSecond = 300 * 1024;
        for (int i = 0; i < 96; i++)
        {
            host.Transport.Connections[0].Send(new byte[32 * 1024]);
        }

        rig.RunUntil(() => a.Disconnected.Count > 0, "the receiver to be dropped", TimeSpan.FromSeconds(60));
        Assert.Equal(PhotonError.ServerError, a.Transport.LastError);
    }

    [Fact]
    public void RoundTrip_IsMeasured_ThroughTheRelay()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host(latencyMs: 30);
        RigPeer a = rig.Client(host, "a", latencyMs: 50);
        rig.Run(TimeSpan.FromSeconds(3));

        // Ping: client to server to host, and back: 2 x (50 + 30) ms, give or take a frame.
        Assert.InRange(a.Transport.Connections[0].RoundTripTime.TotalMilliseconds, 150, 190);
        Assert.InRange(host.Transport.Connections[0].RoundTripTime.TotalMilliseconds, 150, 190);
    }

    [Fact]
    public void JoiningARoomThatIsNotThere_Fails_AndLeavesPhoton()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Add("a");
        a.Transport.Connect("jp/ZZZZZZ");
        rig.RunUntil(() => a.Disconnected.Count == 1, "the join to fail");

        Assert.Equal(DisconnectReason.ConnectFailed, a.Disconnected[0].Reason);
        Assert.Equal(PhotonStatus.Failed, a.Transport.Status);
        Assert.Equal(PhotonError.RoomNotFound, a.Transport.LastError);
        rig.Run(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, rig.Cloud.Connected);
        Assert.Empty(a.Transport.Connections);
    }

    [Theory]
    [InlineData(nameof(PhotonError.NoInternet))]
    [InlineData(nameof(PhotonError.InvalidAppId))]
    [InlineData(nameof(PhotonError.ServerFull))]
    [InlineData(nameof(PhotonError.RegionUnavailable))]
    public void ConnectionProblems_AreReported_NotThrown(string problem)
    {
        using var rig = new TransportRig();
        PhotonSettings settings = PhotonSettings.ForAppId(problem == nameof(PhotonError.InvalidAppId) ? "11111111-2222-3333-4444-555555555555" : FakePhotonCloud.AppId);
        if (problem == nameof(PhotonError.RegionUnavailable))
        {
            settings = settings.WithRegion("sa");
        }

        rig.Cloud.Unreachable = problem == nameof(PhotonError.NoInternet);
        rig.Cloud.CcuLimit = problem == nameof(PhotonError.ServerFull) ? 0 : 20;
        RigPeer a = rig.Add("a", settings: settings);
        a.Transport.Connect("ABCDEF");
        rig.RunUntil(() => a.Disconnected.Count == 1, "the connect to fail");

        Assert.Equal(DisconnectReason.ConnectFailed, a.Disconnected[0].Reason);
        Assert.Equal(Enum.Parse<PhotonError>(problem), a.Transport.LastError);
        Assert.Equal(PhotonStatus.Failed, a.Transport.Status);

        RigPeer host = rig.Add("host", settings: settings);
        host.Transport.StartHost(TransportRig.Advert());
        rig.RunUntil(() => host.Transport.Status == PhotonStatus.Failed, "hosting to fail");
        Assert.Equal(Enum.Parse<PhotonError>(problem), host.Transport.LastError);
        Assert.Null(host.Transport.HostAddress);
        rig.Run(TimeSpan.FromSeconds(5));
        Assert.Equal(PhotonStatus.Failed, host.Transport.Status);
    }

    [Fact]
    public void HostThatNeverAnswers_TimesOut()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Add("a", configure: s => s with { ConnectTimeout = TimeSpan.FromSeconds(2) });
        host.Frozen = true;
        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Disconnected.Count == 1, "the connect to give up");

        Assert.Equal(DisconnectReason.ConnectFailed, a.Disconnected[0].Reason);
        Assert.Equal(PhotonError.HostNotAnswering, a.Transport.LastError);

        // Still in the room, so that a host migration could go on from here.
        Assert.Equal(PhotonStatus.Connected, a.Transport.Status);
        Assert.Equal("actor:2", a.Transport.MigrationAddress);
    }

    [Fact]
    public void Disconnecting_EndsTheConnection_OnBothSides()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");

        // The host ends b's connection: b hears it from the host; b then leaves Photon itself.
        IConnection toB = host.Transport.Connections.Single(c => c.RemoteAddress == "actor:3");
        toB.Disconnect();
        toB.Send([1]);
        rig.Run(TimeSpan.FromMilliseconds(200));
        Assert.Contains((toB, DisconnectReason.Local), host.Disconnected);
        Assert.Equal(DisconnectReason.Remote, Assert.Single(b.Disconnected).Reason);
        Assert.Empty(b.Messages);

        // a leaves: the host hears it right away, and a is out of Photon.
        a.Transport.Connections[0].Disconnect();
        rig.Run(TimeSpan.FromMilliseconds(200));
        Assert.Equal(DisconnectReason.Local, Assert.Single(a.Disconnected).Reason);
        Assert.Contains(host.Disconnected, d => d.Reason == DisconnectReason.Remote && d.Connection.RemoteAddress == "actor:2");
        Assert.Equal(PhotonStatus.Offline, a.Transport.Status);
        Assert.Empty(host.Transport.Connections);
    }

    [Fact]
    public void ClientThatConnectsAgain_ReplacesItsOldConnection()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Transport.Connections[0].Disconnect();
        rig.Run(TimeSpan.FromMilliseconds(200));

        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Connected.Count == 2, "a to connect again");
        a.Transport.Connections[0].Send([9]);
        rig.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, host.Connected.Count);
        Assert.Same(host.Connected[1], host.Messages[^1].Connection);
        Assert.Equal("actor:3", a.Transport.MigrationAddress);
    }

    [Fact]
    public void HostLeaving_TellsItsClients_ThatTheHostLeft()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        host.Transport.SendToMany(host.Transport.Connections, [42]);
        host.Transport.Shutdown();
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.All([a, b], p =>
        {
            Assert.Equal([42], Assert.Single(p.Messages).Data);
            Assert.Equal(DisconnectReason.HostLeft, Assert.Single(p.Disconnected).Reason);
            Assert.Null(p.Transport.RoomHostAddress);
            Assert.False(p.Transport.IsPeerPresent("actor:1"));
            Assert.True(p.Transport.IsPeerPresent("actor:3"));
        });
        Assert.Equal(TransportRole.None, host.Transport.Role);
        Assert.Equal(PhotonStatus.Offline, host.Transport.Status);
        Assert.Equal(2, rig.Cloud.Connected);
    }

    [Fact]
    public void TakeOver_MakesTheClientTheHost_AndTheOthersFollowIt()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        host.Transport.Shutdown();
        rig.Run(TimeSpan.FromMilliseconds(200));

        // b already wants the new host before it exists: it waits for the room to name it.
        b.Transport.Connect("actor:2");
        rig.Run(TimeSpan.FromMilliseconds(200));
        Assert.Single(b.Connected);

        Assert.Null(a.Transport.TakeOverHost(TransportRig.Advert("Taken over")));
        Assert.Equal(TransportRole.Host, a.Transport.Role);
        Assert.Equal(host.Transport.RoomCode ?? a.Transport.RoomCode, a.Transport.RoomCode);
        Assert.NotNull(a.Transport.HostAddress);
        rig.RunUntil(() => b.Connected.Count == 2, "b to reach the new host");
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal("actor:2", b.Transport.Connections[0].RemoteAddress);
        Assert.Equal("actor:2", b.Transport.RoomHostAddress);
        FakeRoom room = rig.Cloud.Rooms.Values.Single();
        Assert.Equal(2, room.Properties[Wire.HostProperty]);
        Assert.Equal(2, room.Master);
        Assert.Equal("Taken over", Wire.DecodeAdvertisement(room.Properties[Wire.AdvertisementProperty])!.Value.Room.Name);
    }

    [Fact]
    public void TakeOver_OnlyOneOfTwoWins()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        host.Transport.Shutdown();
        rig.Run(TimeSpan.FromMilliseconds(200));

        // b's claim reaches the server first (lower latency is not needed: a waits for its answer
        // only after b's claim is on its way).
        b.Link.SetRoomProperties(new System.Collections.Generic.Dictionary<string, object> { [Wire.HostProperty] = 3 },
            new System.Collections.Generic.Dictionary<string, object> { [Wire.HostProperty] = 1 });
        string? winner = a.Transport.TakeOverHost(TransportRig.Advert());

        Assert.Equal("actor:3", winner);
        Assert.Equal(TransportRole.Client, a.Transport.Role);
    }

    [Fact]
    public void TakeOver_ThatIsNotConfirmedInTime_PointsToTheRoom()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Add("a", latencyMs: 2000, configure: s => s with { TakeOverTimeout = TimeSpan.FromSeconds(1) });
        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Connected.Count == 1, "a to connect", TimeSpan.FromSeconds(60));
        host.Transport.Shutdown();
        rig.RunUntil(() => a.Disconnected.Count == 1, "a to see the host leave", TimeSpan.FromSeconds(60));

        string? answer = a.Transport.TakeOverHost(TransportRig.Advert());

        Assert.Equal("jp/" + a.Transport.RoomCode, answer);
        Assert.Equal(TransportRole.Client, a.Transport.Role);

        // The claim did go through: asking again finds this peer recorded as host.
        rig.Run(TimeSpan.FromSeconds(5));
        Assert.Null(a.Transport.TakeOverHost(TransportRig.Advert()));
        Assert.Equal(TransportRole.Host, a.Transport.Role);
    }

    [Fact]
    public void ConnectingToAnActorThatIsNotTheHost_FailsOnceAnotherHostIsNamed()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        RigPeer c = rig.Client(host, "c");
        host.Transport.Shutdown();
        rig.Run(TimeSpan.FromMilliseconds(200));

        c.Transport.Connect("actor:2");
        Assert.Null(b.Transport.TakeOverHost(TransportRig.Advert()));
        rig.RunUntil(() => c.Disconnected.Count == 2, "c to give up on a");

        Assert.Equal(DisconnectReason.ConnectFailed, c.Disconnected[1].Reason);
        Assert.Equal("actor:3", c.Transport.RoomHostAddress);
    }

    [Fact]
    public void OpenToAPeerThatDoesNotHost_IsRefused()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        RigPeer b = rig.Client(host, "b");
        host.Frozen = true;
        host.Link.Crash();
        b.Link.RaiseEvent(Wire.Open, Wire.EncodeOpen(77), [2]);
        b.Link.RaiseEvent(Wire.Open, [9, 9], [2]);
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Single(a.Connected);
        Assert.Contains("event 6", a.Link.Operations);
    }

    [Fact]
    public void OpenOfAnotherTransportVersion_IsRefused()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Add("a");
        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Link.State == LinkState.Room, "a to be in the room");
        byte[] open = Wire.EncodeOpen(5);
        open[0] = Wire.Version + 1;
        a.Link.RaiseEvent(Wire.Open, open, [1]);
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Single(host.Connected);
        Assert.Contains("event 6", host.Link.Operations);
    }

    [Fact]
    public void BrokenFragments_CloseTheConnection_AsAProtocolError()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.RaiseEvent(Wire.Fragment, [0, 1, 2], [1]);
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal(DisconnectReason.ProtocolError, Assert.Single(host.Disconnected).Reason);
        Assert.Equal(DisconnectReason.Remote, Assert.Single(a.Disconnected).Reason);
    }

    [Fact]
    public void LostNetwork_IsReported_OnBothSides()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.LoseNetwork();
        rig.Run(TimeSpan.FromSeconds(4));

        Assert.Equal(DisconnectReason.ConnectionLost, Assert.Single(a.Disconnected).Reason);
        Assert.Equal(PhotonError.ConnectionLost, a.Transport.LastError);
        Assert.Equal(DisconnectReason.ConnectionLost, Assert.Single(host.Disconnected).Reason);

        // It can connect again: the old room address still works.
        a.Transport.Connect(host.Transport.HostAddress!);
        rig.RunUntil(() => a.Connected.Count == 2, "a to come back");
        Assert.Equal(2, a.Links.Count);
    }

    [Fact]
    public void ActorAddress_AfterALostLink_JoinsTheSameRoomAgain()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.LoseNetwork();
        rig.Run(TimeSpan.FromSeconds(4));

        a.Transport.Connect("actor:1");
        rig.RunUntil(() => a.Connected.Count == 2, "a to reach the host again");

        Assert.Equal("actor:1", a.Transport.Connections[0].RemoteAddress);
    }

    [Fact]
    public void HostAloneThatLosesItsLink_OpensTheRoomAgain()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        string first = host.Transport.HostAddress!;
        host.Link.LoseNetwork();
        rig.RunUntil(() => host.Transport.Status == PhotonStatus.Failed, "the host to notice");
        Assert.Null(host.Transport.HostAddress);
        Assert.False(host.Transport.HostingLost);
        rig.RunUntil(() => host.Transport.HostAddress is not null, "the room to open again");

        Assert.NotEqual(first, host.Transport.HostAddress);
        RigPeer a = rig.Client(host, "a");
        Assert.Single(a.Connected);
    }

    [Fact]
    public void HostAlone_KeepsTryingToOpenTheRoom_UntilTheNetworkIsBack()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        host.Link.LoseNetwork();
        rig.Cloud.Unreachable = true;
        rig.Run(TimeSpan.FromSeconds(40));

        Assert.Equal(PhotonStatus.Failed, host.Transport.Status);
        Assert.Equal(PhotonError.NoInternet, host.Transport.LastError);
        int attempts = host.Links.Count;
        Assert.InRange(attempts, 3, 8);

        rig.Cloud.Unreachable = false;
        rig.RunUntil(() => host.Transport.HostAddress is not null, "the room to open again", TimeSpan.FromSeconds(60));
        Assert.Equal(PhotonStatus.Connected, host.Transport.Status);

        // Once it worked, nothing more is tried.
        int links = host.Links.Count;
        rig.Run(TimeSpan.FromSeconds(40));
        Assert.Equal(links, host.Links.Count);
    }

    [Fact]
    public void Shutdown_DoesNotWait_ForAClientThatAlreadyLeft()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a", latencyMs: 60);
        a.Link.DownlinkBytesPerSecond = 50 * 1024;
        IConnection toA = host.Transport.Connections[0];
        for (int i = 0; i < 20; i++)
        {
            toA.Send(new byte[32 * 1024]);
        }

        // The host closes the connection while most of it still waits for credit; then a leaves.
        toA.Disconnect();
        a.Transport.Shutdown();
        rig.Run(TimeSpan.FromSeconds(1));
        TimeSpan before = rig.Clock.Now;
        host.Transport.Shutdown();

        Assert.True(rig.Clock.Now - before < TimeSpan.FromMilliseconds(50), $"Shutting down waited {rig.Clock.Now - before}.");
    }

    [Fact]
    public void HostWithPlayersThatLosesItsLink_HandsTheRoomToThem()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        host.Link.LoseNetwork();
        rig.Run(TimeSpan.FromSeconds(6));

        Assert.True(host.Transport.HostingLost);
        Assert.Equal(PhotonStatus.Failed, host.Transport.Status);
        Assert.Equal(DisconnectReason.ConnectionLost, Assert.Single(host.Disconnected).Reason);
        Assert.Equal(DisconnectReason.HostLeft, Assert.Single(a.Disconnected).Reason);
        Assert.Single(host.Links);
    }

    [Fact]
    public void HostThatAnotherPeerReplaced_StopsHosting()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.SetRoomProperties(new System.Collections.Generic.Dictionary<string, object> { [Wire.HostProperty] = 2 }, null);
        rig.Run(TimeSpan.FromMilliseconds(200));

        Assert.Equal(PhotonError.HostReplaced, host.Transport.LastError);
        Assert.True(host.Transport.HostingLost);
        Assert.Null(host.Transport.HostAddress);
    }

    [Fact]
    public void MasterClient_FollowsTheHost()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");
        a.Link.SetMasterClient(2);
        rig.Run(TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, rig.Cloud.Rooms.Values.Single().Master);
    }

    [Fact]
    public void Misuse_Throws()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RigPeer a = rig.Client(host, "a");

        Assert.Throws<InvalidOperationException>(() => host.Transport.Connect("jp/ABCDEF"));
        Assert.Throws<InvalidOperationException>(() => host.Transport.StartHost(TransportRig.Advert()));
        Assert.Throws<InvalidOperationException>(() => host.Transport.TakeOverHost(TransportRig.Advert()));
        Assert.Throws<InvalidOperationException>(() => a.Transport.Connect("jp/ABCDEF"));
        Assert.Throws<InvalidOperationException>(() => a.Transport.UpdateAdvertisement(TransportRig.Advert()));
        Assert.Throws<ArgumentException>(() => rig.Add("b").Transport.Connect("not a code"));
        Assert.Throws<ArgumentException>(() => new PhotonTransport(PhotonSettings.ForAppId("x") with { FragmentBytes = 10 }));
        a.Transport.Dispose();
        a.Transport.Dispose();
        Assert.Throws<ObjectDisposedException>(a.Transport.Poll);
    }

    [Fact]
    public void ActorAddress_WithoutARoom_FailsAtOnce()
    {
        using var rig = new TransportRig();
        RigPeer a = rig.Add("a");
        a.Transport.Connect("actor:3");
        rig.Step();

        Assert.Equal(DisconnectReason.ConnectFailed, Assert.Single(a.Disconnected).Reason);
        Assert.Equal(PhotonError.RoomNotFound, a.Transport.LastError);
        Assert.Equal("actor:1", a.Transport.TakeOverHost(TransportRig.Advert()));
    }

    [Fact]
    public void Advertisement_ChangesReachTheRoom_OnlyWhenTheyChange()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        RoomAdvertisement advert = TransportRig.Advert("Before");
        host.Transport.UpdateAdvertisement(advert);
        host.Transport.UpdateAdvertisement(advert);
        host.Transport.UpdateAdvertisement(advert with { Name = "After", Players = 3 });
        rig.Run(TimeSpan.FromMilliseconds(100));

        FakeRoom room = rig.Cloud.Rooms.Values.Single();
        RoomAdvertisement listed = Wire.DecodeAdvertisement(room.Properties[Wire.AdvertisementProperty])!.Value.Room;
        Assert.Equal("After", listed.Name);
        Assert.Equal(3, listed.Players);
        Assert.Equal(2, host.Link.Operations.Count(o => o.StartsWith("set a", StringComparison.Ordinal)));
    }

    [Fact]
    public void RoomCodeThatIsTaken_IsReplacedByAnother()
    {
        using var rig = new TransportRig();
        RigPeer first = rig.Host("first");
        RigPeer second = rig.Add("second");

        // The second host draws the same codes as the first (same seed) until it draws a free one.
        var occupied = new FakeRoom("jp", "AAAAAA", 4, []);
        var replica = new Random(8);
        replica.Next(1, int.MaxValue / 2);
        for (int i = 0; i < 3; i++)
        {
            rig.Cloud.Rooms[("jp", PhotonAddress.NewCode(replica))] = occupied;
        }

        second.Transport.StartHost(TransportRig.Advert());
        rig.RunUntil(() => second.Transport.HostAddress is not null || second.Transport.Status == PhotonStatus.Failed, "the second room");

        Assert.NotNull(second.Transport.HostAddress);
        Assert.NotEqual(first.Transport.RoomCode, second.Transport.RoomCode);
    }

    [Fact]
    public void WebSocketsAndRegion_ReachTheLink()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Add("host", configure: s => s.WithRegion("EU") with { UseWebSockets = true });
        host.Transport.StartHost(TransportRig.Advert());
        rig.RunUntil(() => host.Transport.HostAddress is not null, "the room");

        Assert.StartsWith("eu/", host.Transport.HostAddress, StringComparison.Ordinal);
        Assert.Equal("eu", host.Transport.Region);
        Assert.True(host.Link.Options!.UseWebSockets);
        Assert.True(host.Link.Options.AllowWebSocketFallback);
        Assert.Equal(PhotonSettings.DefaultAppVersion, host.Link.Options.AppVersion);
    }
}
