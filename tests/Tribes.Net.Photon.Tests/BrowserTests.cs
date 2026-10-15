using System;
using System.Linq;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The room browser against the in-memory Photon cloud.</summary>
public sealed class BrowserTests
{
    private static PhotonRoomBrowser Browser(TransportRig rig, PhotonSettings? settings = null) =>
        new(settings ?? PhotonSettings.ForAppId(FakePhotonCloud.AppId), rig.Cloud.CreateLink, rig.Clock);

    private static void Run(TransportRig rig, PhotonRoomBrowser browser, TimeSpan duration)
    {
        for (long frames = duration.Ticks / TransportRig.Frame.Ticks; frames > 0; frames--)
        {
            rig.Step();
            browser.Poll();
        }
    }

    [Fact]
    public void Lists_Rooms_AndFollowsTheirChanges()
    {
        using var rig = new TransportRig();
        RigPeer first = rig.Host("first", advert: TransportRig.Advert("Beta"));
        using PhotonRoomBrowser browser = Browser(rig);
        int changes = 0;
        int statuses = 0;
        browser.RoomsChanged += (_, _) => changes++;
        browser.StatusChanged += (_, _) => statuses++;
        browser.Start();
        browser.Start();
        Run(rig, browser, TimeSpan.FromMilliseconds(300));

        Assert.Equal(PhotonStatus.Connected, browser.Status);
        Assert.Equal("jp", browser.Region);
        Assert.NotEmpty(browser.RegionPings);
        RoomInfo listed = Assert.Single(browser.Rooms);
        Assert.Equal(first.Transport.HostAddress, listed.Address);
        Assert.Equal("Beta", listed.Room.Name);
        Assert.Equal(NetProtocol.Version, listed.ProtocolVersion);
        Assert.True(listed.Ping > TimeSpan.Zero);

        // A second room, a change, and a room that goes away.
        RigPeer second = rig.Host("second", advert: TransportRig.Advert("Alpha"));
        second.Transport.UpdateAdvertisement(TransportRig.Advert("Alpha", inGame: true));
        Run(rig, browser, TimeSpan.FromMilliseconds(300));
        Assert.Equal(["Alpha", "Beta"], browser.Rooms.Select(r => r.Room.Name));
        Assert.True(browser.Rooms[0].Room.InGame);
        first.Transport.Shutdown();
        Run(rig, browser, TimeSpan.FromMilliseconds(300));
        Assert.Equal("Alpha", Assert.Single(browser.Rooms).Room.Name);
        Assert.True(changes >= 3);
        Assert.True(statuses >= 2);

        browser.Shutdown();
        browser.Poll();
        Assert.Empty(browser.Rooms);
        Assert.Equal(PhotonStatus.Offline, browser.Status);
    }

    [Fact]
    public void RoomsThatAreNotOurs_AreLeftOut()
    {
        using var rig = new TransportRig();
        using PhotonRoomBrowser browser = Browser(rig);
        browser.Start();

        // Rooms of another game on the same app: no advertisement, or a name that is not a code.
        var raw = (FakeLink)rig.Cloud.CreateLink();
        var other = (FakeLink)rig.Cloud.CreateLink();
        raw.Connect(new Link.LinkConnectOptions(FakePhotonCloud.AppId, "x", null, false, true));
        other.Connect(new Link.LinkConnectOptions(FakePhotonCloud.AppId, "x", null, false, true));
        for (int i = 0; i < 20; i++)
        {
            Run(rig, browser, TransportRig.Frame);
            raw.Service();
            other.Service();
        }

        raw.CreateRoom("QQQQQQ", new Link.LinkRoomOptions(4, new System.Collections.Generic.Dictionary<string, object> { [Wire.AdvertisementProperty] = "nonsense" }, [Wire.AdvertisementProperty]));
        other.CreateRoom("lowercase", new Link.LinkRoomOptions(4, new System.Collections.Generic.Dictionary<string, object> { [Wire.AdvertisementProperty] = Wire.EncodeAdvertisement(TransportRig.Advert()) }, [Wire.AdvertisementProperty]));
        Run(rig, browser, TimeSpan.FromMilliseconds(300));

        Assert.Equal(2, rig.Cloud.Rooms.Count);
        Assert.Empty(browser.Rooms);
    }

    [Fact]
    public void LostConnection_IsRetried_ButARefusedAppIdIsNot()
    {
        using var rig = new TransportRig();
        rig.Host();
        using PhotonRoomBrowser browser = Browser(rig);
        browser.Start();
        Run(rig, browser, TimeSpan.FromMilliseconds(300));
        Assert.Single(browser.Rooms);

        rig.Cloud.Links[^1].LoseNetwork();
        Run(rig, browser, TimeSpan.FromSeconds(3));
        Assert.Equal(PhotonStatus.Failed, browser.Status);
        Assert.Equal(PhotonError.ConnectionLost, browser.LastError);
        Assert.Empty(browser.Rooms);
        Run(rig, browser, TimeSpan.FromSeconds(6));
        Assert.Equal(PhotonStatus.Connected, browser.Status);
        Assert.Single(browser.Rooms);

        using PhotonRoomBrowser refused = Browser(rig, PhotonSettings.ForAppId("99999999-9999-9999-9999-999999999999"));
        refused.Refresh();
        Run(rig, refused, TimeSpan.FromSeconds(10));
        Assert.Equal(PhotonError.InvalidAppId, refused.LastError);
        int links = rig.Cloud.Links.Count;
        Run(rig, refused, TimeSpan.FromSeconds(10));
        Assert.Equal(links, rig.Cloud.Links.Count);
        refused.Refresh();
        Assert.Equal(links + 1, rig.Cloud.Links.Count);
        refused.Dispose();
        Assert.Throws<ObjectDisposedException>(refused.Poll);
    }

    [Fact]
    public void NoInternet_IsReported()
    {
        using var rig = new TransportRig();
        rig.Cloud.Unreachable = true;
        using PhotonRoomBrowser browser = Browser(rig);
        browser.Start();
        Run(rig, browser, TimeSpan.FromSeconds(2));

        Assert.Equal(PhotonStatus.Failed, browser.Status);
        Assert.Equal(PhotonError.NoInternet, browser.LastError);
        Assert.Throws<ArgumentException>(() => new PhotonRoomBrowser(PhotonSettings.ForAppId("x") with { PingInterval = TimeSpan.Zero }));
    }
}
