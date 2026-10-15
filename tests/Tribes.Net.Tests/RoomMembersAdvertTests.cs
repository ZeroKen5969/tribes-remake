using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Lan;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>
/// Room lists show running matches with what this device can do there: come back, join or watch.
/// For that the advertisement carries the people holding slots (public ids and state) and whether
/// a spectator place is free; older advertisements without them still decode.
/// </summary>
public sealed class RoomMembersAdvertTests
{
    private static readonly Guid RoomId = Guid.Parse("8a8f6a54-4b1e-4c5b-9a43-1d2c3b4a5f60");

    private static RoomAdvertisement Advert(params RoomMember[] members) => new()
    {
        RoomId = RoomId,
        Name = "Room",
        HostName = "Host",
        Mode = RoomMode.Endless,
        Players = members.Length,
        MaxPlayers = 4,
        GameVersion = "1.0",
        ContentVersion = "c",
        InGame = true,
        Members = members,
        SpectatorsWelcome = true,
    };

    [Fact]
    public void Reply_RoundTrip_KeepsMembersAndSpectatorPlace()
    {
        RoomMember[] members = [new(Guid.NewGuid(), PlayerPresence.Active), new(Guid.NewGuid(), PlayerPresence.Absent), new(Guid.NewGuid(), PlayerPresence.Forfeited)];

        DiscoveryReply? reply = DiscoveryCodec.TryDecodeReply(DiscoveryCodec.EncodeReply(5, 47801, Advert(members)));

        Assert.NotNull(reply);
        Assert.Equal(members, reply.Room.Members);
        Assert.True(reply.Room.SpectatorsWelcome);
        Assert.Equal(Advert(members), reply.Room);
    }

    [Fact]
    public void Reply_FromAnOlderHost_HasNoMembersOrSpectatorPlace()
    {
        byte[] full = DiscoveryCodec.EncodeReply(5, 47801, Advert(new RoomMember(Guid.NewGuid(), PlayerPresence.Active)));
        byte[] older = full[..OlderReplyLength(full)];

        DiscoveryReply? reply = DiscoveryCodec.TryDecodeReply(older);

        Assert.NotNull(reply);
        Assert.Equal("Room", reply.Room.Name);
        Assert.True(reply.Room.InGame);
        Assert.Null(reply.Room.Members);
        Assert.Null(reply.Room.SpectatorsWelcome);
    }

    [Fact]
    public void Reply_FromANewerHost_KeepsWhatItKnowsAndIgnoresTheRest()
    {
        RoomMember member = new(Guid.NewGuid(), PlayerPresence.Absent);
        byte[] full = DiscoveryCodec.EncodeReply(5, 47801, Advert(member));
        int extension = OlderReplyLength(full);
        byte[] newer = [.. full, 0xAB, 0xCD, 0xEF];
        newer[extension] = 9; // a later extension version, with more fields after ours

        DiscoveryReply? reply = DiscoveryCodec.TryDecodeReply(newer);

        Assert.NotNull(reply);
        Assert.Equal([member], reply.Room.Members);
    }

    [Fact]
    public void Reply_WithTooManyMembers_IsIgnored()
    {
        RoomMember[] crowd = Enumerable.Range(0, ProtocolLimits.MaxRoomPlayers + 1).Select(_ => new RoomMember(Guid.NewGuid(), PlayerPresence.Active)).ToArray();

        Assert.Null(DiscoveryCodec.TryDecodeReply(DiscoveryCodec.EncodeReply(5, 47801, Advert(crowd))));
    }

    [Fact]
    public void Adverts_WithEqualMembers_AreEqual()
    {
        Guid id = Guid.NewGuid();

        Assert.Equal(Advert(new RoomMember(id, PlayerPresence.Absent)), Advert(new RoomMember(id, PlayerPresence.Absent)));
        Assert.Equal(Advert(new RoomMember(id, PlayerPresence.Absent)).GetHashCode(), Advert(new RoomMember(id, PlayerPresence.Absent)).GetHashCode());
        Assert.NotEqual(Advert(new RoomMember(id, PlayerPresence.Absent)), Advert(new RoomMember(id, PlayerPresence.Active)));
    }

    [Fact]
    public void PresenceOf_FindsThisDevicesPlayerByItsKey()
    {
        Guid myKey = Guid.NewGuid();
        Guid otherKey = Guid.NewGuid();
        var room = new RoomInfo { Address = "h", Room = Advert(new RoomMember(SessionShared.PublicId(myKey, RoomId), PlayerPresence.Absent)) };

        Assert.Equal(PlayerPresence.Absent, room.PresenceOf(myKey));
        Assert.Null(room.PresenceOf(otherKey));
        Assert.Null((room with { Room = room.Room with { Members = null } }).PresenceOf(myKey));
    }

    [Fact]
    public void HostAdvert_ListsThePeopleInTheMatch_WithTheirState()
    {
        using var cluster = new LoopbackCluster(RoomMode.Versus);
        TestPeer stays = cluster.AddClient("stays");
        TestPeer leaves = cluster.AddClient("leaves");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        leaves.Session.Leave();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        RoomInfo room = BrowseOne(cluster);

        Assert.True(room.Room.InGame);
        Assert.False(room.Room.IsOpen);
        Assert.Equal(PlayerPresence.Absent, room.PresenceOf(leaves.Id));
        Assert.Equal(PlayerPresence.Active, room.PresenceOf(stays.Id));
        Assert.Null(room.PresenceOf(Guid.NewGuid()));
        Assert.True(room.Room.SpectatorsWelcome);
        Assert.Equal(3, room.Room.Members!.Count);
    }

    [Fact]
    public void HostAdvert_SaysWhenNoSpectatorPlaceIsLeft()
    {
        using var cluster = new LoopbackCluster(RoomMode.Versus, o => o with { Room = o.Room with { AllowSpectators = false } });
        cluster.AddClient("player");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(1));

        Assert.False(BrowseOne(cluster).Room.SpectatorsWelcome);
    }

    private static RoomInfo BrowseOne(LoopbackCluster cluster)
    {
        using var browser = new LoopbackRoomBrowser(cluster.Network);
        browser.Start();
        browser.Poll();
        return Assert.Single(browser.Rooms);
    }

    /// <summary>Where the extension starts: a reply written without members and spectator place.</summary>
    private static int OlderReplyLength(byte[] full)
    {
        DiscoveryReply reply = DiscoveryCodec.TryDecodeReply(full)!;
        return DiscoveryCodec.EncodeReply(reply.Nonce, reply.TcpPort, reply.Room with { Members = null, SpectatorsWelcome = null }).Length;
    }
}
