using System;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Lan;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>
/// The rules fingerprint: two builds with the same version numbers but different rules would
/// desync, so the host refuses players (and spectators) whose fingerprint differs, and room
/// lists mark such rooms.
/// </summary>
public sealed class FingerprintTests
{
    private static LoopbackCluster Cluster(string fingerprint) =>
        new(configure: o => o with { RulesFingerprint = fingerprint });

    [Fact]
    public void Host_RefusesPlayersAndSpectators_WithOtherRules()
    {
        using LoopbackCluster cluster = Cluster("rules-a");
        TestPeer same = cluster.AddClient("same", configure: o => o with { RulesFingerprint = "rules-a" });
        TestPeer other = cluster.AddClient("other", configure: o => o with { RulesFingerprint = "rules-b" });
        TestPeer watcher = cluster.AddClient("watcher", configure: o => o with { RulesFingerprint = "rules-b", Spectate = true });
        TestPeer unknown = cluster.AddClient("unknown");
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Equal(ClientPhase.Lobby, same.Session.Phase);
        Assert.All([other, watcher, unknown], p =>
        {
            Assert.Equal(SessionEndReason.Rejected, p.Session.EndReason);
            Assert.Equal(RejectReason.RulesMismatch, p.Session.Rejection);
        });
    }

    [Fact]
    public void Host_WithoutFingerprint_DoesNotCompare()
    {
        using LoopbackCluster cluster = Cluster(string.Empty);
        TestPeer a = cluster.AddClient("a", configure: o => o with { RulesFingerprint = "anything" });
        cluster.RunUntilJoined(a);

        Assert.Equal(ClientPhase.Lobby, a.Session.Phase);
    }

    [Fact]
    public void RoomLists_SeeTheHostsFingerprint_AndMarkOtherRules()
    {
        var advert = new RoomAdvertisement
        {
            RoomId = Guid.NewGuid(),
            Name = "Room",
            GameVersion = "1.0.0",
            ContentVersion = "content-r0",
            RulesFingerprint = "rules-a",
            Members = [],
            SpectatorsWelcome = false,
        };
        var room = new RoomInfo { Address = "x", Room = advert };

        Assert.True(room.IsCompatible("1.0.0", "content-r0"));
        Assert.True(room.IsCompatible("1.0.0", "content-r0", "rules-a"));
        Assert.False(room.IsCompatible("1.0.0", "content-r0", "rules-b"));
        Assert.False(room.IsCompatible("1.0.0", "content-r0", string.Empty));
        Assert.True((room with { Room = advert with { RulesFingerprint = null } }).IsCompatible("1.0.0", "content-r0", "rules-b"));

        // The fingerprint travels in the advertisement, in LAN discovery replies as well.
        var writer = new PacketWriter();
        AdvertisementCodec.Write(writer, advert);
        Assert.Equal(advert, AdvertisementCodec.Read(new PacketReader(writer.ToArray())));
        DiscoveryReply reply = DiscoveryCodec.TryDecodeReply(DiscoveryCodec.EncodeReply(7, 47801, advert))!;
        Assert.Equal("rules-a", reply.Room.RulesFingerprint);
    }

    [Fact]
    public void AdvertisementsOfTheFirstExtension_HaveNoFingerprint()
    {
        // Version 1 of the extension: a spectator place, then the members, nothing after them.
        var writer = new PacketWriter();
        AdvertisementCodec.Write(writer, new RoomAdvertisement { RoomId = Guid.Empty, Name = "Old", GameVersion = "1", ContentVersion = "c" });
        writer.WriteByte(1);
        writer.WriteBool(true);
        writer.WriteVarUInt(0);
        RoomAdvertisement read = AdvertisementCodec.Read(new PacketReader(writer.ToArray()));

        Assert.True(read.SpectatorsWelcome);
        Assert.Null(read.RulesFingerprint);
    }

    [Fact]
    public void Hello_CarriesTheFingerprint_AndOptionsCheckItsLength()
    {
        var hello = new HelloMessage(NetProtocol.Version, "1", "c", Guid.NewGuid(), "a", false, -1, Guid.Empty, -1, string.Empty, "rules-a");
        var decoded = (HelloMessage)MessageCodec.Decode(MessageCodec.Encode(hello));
        Assert.Equal("rules-a", decoded.RulesFingerprint);

        string tooLong = new('f', RoomLimits.MaxVersionLength + 1);
        Assert.Throws<ArgumentException>(() => new LoopbackCluster(configure: o => o with { RulesFingerprint = tooLong }));
        using LoopbackCluster cluster = Cluster("rules-a");
        Assert.Throws<ArgumentException>(() => cluster.AddClient("long", configure: o => o with { RulesFingerprint = tooLong }));
    }
}
