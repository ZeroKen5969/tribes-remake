using System;
using System.Linq;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The Photon events, room properties and addresses.</summary>
public sealed class WireTests
{
    [Fact]
    public void ControlEvents_RoundTrip_AndRejectWrongSizes()
    {
        Assert.True(Wire.TryDecodeOpen(Wire.EncodeOpen(-5), out byte version, out int nonce));
        Assert.Equal((Wire.Version, -5), (version, nonce));
        Assert.False(Wire.TryDecodeOpen([1, 2], out _, out _));
        Assert.False(Wire.TryDecodeOpen(null, out _, out _));
        Assert.True(Wire.TryDecodeNonce(Wire.EncodeNonce(123456), out int back));
        Assert.Equal(123456, back);
        Assert.False(Wire.TryDecodeNonce([1], out _));
        Assert.True(Wire.TryDecodeStamp(Wire.EncodeStamp(TimeSpan.FromSeconds(7.5)), out TimeSpan stamp));
        Assert.Equal(TimeSpan.FromSeconds(7.5), stamp);
        Assert.False(Wire.TryDecodeStamp([1, 2, 3], out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(5000)]
    public void Split_ThenAssemble_GivesTheMessageBack(int length)
    {
        byte[] message = new byte[length];
        new Random(length).NextBytes(message);
        var parts = Wire.Split(message, 1024).ToList();
        var assembler = new FragmentAssembler(NetProtocol.MaxMessageBytes);

        byte[]? result = null;
        foreach ((byte code, byte[] content) in parts)
        {
            result = code == Wire.Data ? content : assembler.Add(content);
        }

        Assert.Equal(length <= 1024 ? 1 : (length + 1023) / 1024, parts.Count);
        Assert.Equal(message, result);
        Assert.False(assembler.IsAssembling);
    }

    [Fact]
    public void Assembler_RejectsBrokenSeries()
    {
        var assembler = new FragmentAssembler(100);
        Assert.Throws<ProtocolException>(() => assembler.Add([0, 1]));
        Assert.Throws<ProtocolException>(() => assembler.Add([]));
        Assert.Throws<ProtocolException>(() => assembler.Add([2]));
        Assert.Throws<ProtocolException>(() => assembler.Add([1, 0xFF, 0xFF, 0, 0]));
        Assert.Throws<ProtocolException>(() => assembler.Add([1, 0, 0, 0, 0]));
        Assert.Throws<ProtocolException>(() => assembler.Add([1, 1]));
        Assert.Null(assembler.Add([1, 4, 0, 0, 0, 9]));
        Assert.Throws<ProtocolException>(() => assembler.Add([1, 4, 0, 0, 0]));
        assembler.Reset();
        Assert.Null(assembler.Add([1, 2, 0, 0, 0, 9]));
        Assert.Throws<ProtocolException>(() => assembler.Add([0, 8, 8]));
    }

    [Fact]
    public void Advertisement_RoundTrips_WithMembers()
    {
        var advert = new RoomAdvertisement
        {
            RoomId = Guid.NewGuid(),
            Name = "房間",
            HostName = "主機",
            Mode = RoomMode.Endless,
            Players = 3,
            MaxPlayers = 4,
            Spectators = 2,
            GameVersion = "1.2.0",
            ContentVersion = "content-7",
            InGame = true,
            IsOpen = true,
            JoinableInProgress = true,
            SpectatorsWelcome = true,
            Members = [new RoomMember(Guid.NewGuid(), PlayerPresence.Absent)],
        };

        (int version, RoomAdvertisement back) = Wire.DecodeAdvertisement(Wire.EncodeAdvertisement(advert))!.Value;

        Assert.Equal(NetProtocol.Version, version);
        Assert.Equal(advert, back);
        Assert.Null(Wire.DecodeAdvertisement(null));
        Assert.Null(Wire.DecodeAdvertisement("text"));
        Assert.Null(Wire.DecodeAdvertisement(new byte[] { 1 }));
    }

    [Theory]
    [InlineData("jp/K7QM2P", "jp", "K7QM2P")]
    [InlineData(" JP / k7qm2p ", "jp", "K7QM2P")]
    [InlineData("k7qm2p", null, "K7QM2P")]
    [InlineData("usw/ABCDEF", "usw", "ABCDEF")]
    public void RoomAddresses_AreRead_Forgivingly(string text, string? region, string code)
    {
        Assert.True(PhotonAddress.TryParseRoom(text, out string? readRegion, out string readCode));
        Assert.Equal(region, readRegion);
        Assert.Equal(code, readCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("K7QM2")]
    [InlineData("K7QM2PX")]
    [InlineData("K7QM0P")]
    [InlineData("j/K7QM2P")]
    [InlineData("jp1/K7QM2P")]
    [InlineData("actor:3")]
    public void RoomAddresses_ThatAreNotCodes_AreRefused(string? text)
    {
        Assert.False(PhotonAddress.TryParseRoom(text, out string? region, out string code));
        Assert.Null(region);
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void ActorAddresses_AndNewCodes()
    {
        Assert.Equal("actor:12", PhotonAddress.Actor(12));
        Assert.True(PhotonAddress.TryParseActor("actor:12", out int actor));
        Assert.Equal(12, actor);
        Assert.False(PhotonAddress.TryParseActor("actor:0", out _));
        Assert.False(PhotonAddress.TryParseActor("actor:-1", out _));
        Assert.False(PhotonAddress.TryParseActor("jp/ABCDEF", out _));
        Assert.False(PhotonAddress.TryParseActor(null, out _));
        Assert.Equal("jp/ABCDEF", PhotonAddress.Room("jp", "ABCDEF"));
        Assert.Throws<ArgumentException>(() => PhotonAddress.Room("", "ABCDEF"));
        var random = new Random(3);
        Assert.All(Enumerable.Range(0, 200).Select(_ => PhotonAddress.NewCode(random)), code => Assert.True(PhotonAddress.IsCode(code)));
        Assert.Throws<ArgumentNullException>(() => PhotonAddress.IsCode(null!));
    }

    [Fact]
    public void Regions_AreNormalized()
    {
        Assert.Null(PhotonRegions.Normalize(null));
        Assert.Null(PhotonRegions.Normalize(" Best "));
        Assert.Null(PhotonRegions.Normalize("auto"));
        Assert.Equal("eu", PhotonRegions.Normalize(" EU "));
        Assert.Equal("not a region", PhotonRegions.Normalize("not a region"));
        Assert.Contains("jp", PhotonRegions.Known);
        Assert.All(PhotonRegions.Known, r => Assert.True(PhotonRegions.IsRegionCode(r)));
        Assert.False(PhotonRegions.IsRegionCode("toolongregion"));
        Assert.Throws<ArgumentNullException>(() => PhotonRegions.IsRegionCode(null!));
    }
}
