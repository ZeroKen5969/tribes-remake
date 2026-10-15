using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Tribes.Net.Photon.Link;
using Tribes.Net.Photon.Tests.Support;
using Tribes.Net.Transport;
using Xunit;
using Xunit.Sdk;

namespace Tribes.Net.Photon.Tests;

/// <summary>Runs only with <c>TRIBES_PHOTON_LIVE=1</c> and an AppId embedded in the build: these tests use the real Photon Cloud.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRIBES_PHOTON_LIVE") != "1")
        {
            Skip = "Set TRIBES_PHOTON_LIVE=1 to run the checks against the Photon Cloud.";
        }
        else if (!PhotonSettings.IsConfigured)
        {
            Skip = "This build has no Photon AppId (secrets/photon.json).";
        }
    }
}

/// <summary>A real transport with what it raised.</summary>
internal sealed class LivePeer : IDisposable
{
    public LivePeer(string name, PhotonSettings settings)
    {
        Name = name;
        Transport = new PhotonTransport(settings);
        Transport.PeerConnected += (_, e) => Connected.Add(e.Connection);
        Transport.PeerDisconnected += (_, e) => Disconnected.Add((e.Connection, e.Reason));
        Transport.MessageReceived += (_, e) => Messages.Add(e.Data.ToArray());
    }

    public string Name { get; }

    public PhotonTransport Transport { get; }

    public List<IConnection> Connected { get; } = [];

    public List<(IConnection Connection, DisconnectReason Reason)> Disconnected { get; } = [];

    public List<byte[]> Messages { get; } = [];

    public void Dispose() => Transport.Dispose();
}

/// <summary>Checks against the Photon Cloud: the adapter, the server's compare-and-swap, large messages, errors.</summary>
[Collection("Photon cloud")]
public sealed class LiveTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(40);

    static LiveTests()
    {
        // TRIBES_PHOTON_LOG=<file> keeps Photon's diagnostics (the AppId is removed from them).
        if (Environment.GetEnvironmentVariable("TRIBES_PHOTON_LOG") is { Length: > 0 } path)
        {
            var gate = new object();
            PhotonDiagnostics.Log = line =>
            {
                lock (gate)
                {
                    System.IO.File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
                }
            };
        }
    }

    private static PhotonSettings Settings => PhotonSettings.FromBuild()!.WithRegion("asia");

    private static void PollUntil(Func<bool> condition, string waitingFor, IEnumerable<LivePeer> peers, PhotonRoomBrowser? browser = null, TimeSpan? timeout = null)
    {
        var clock = Stopwatch.StartNew();
        LivePeer[] all = [.. peers];
        while (!condition())
        {
            if (clock.Elapsed > (timeout ?? Wait))
            {
                string states = string.Join("; ", all.Select(p => $"{p.Name}: {p.Transport.Role} {p.Transport.Status}/{p.Transport.LastError}"));
                throw new XunitException($"Timed out waiting for {waitingFor}. {states} {browser?.Status}/{browser?.LastError}");
            }

            foreach (LivePeer peer in all)
            {
                peer.Transport.Poll();
            }

            browser?.Poll();
            Thread.Sleep(5);
        }
    }

    [LiveFact]
    public void Host_Clients_Messages_AndATakeOver_OnThePhotonCloud()
    {
        using var host = new LivePeer("host", Settings);
        using var a = new LivePeer("a", Settings);
        using var b = new LivePeer("b", Settings);
        LivePeer[] all = [host, a, b];
        host.Transport.StartHost(TransportRig.Advert("Live check"));
        PollUntil(() => host.Transport.HostAddress is not null, "the room", all);
        string room = host.Transport.HostAddress!;
        Assert.StartsWith("asia/", room, StringComparison.Ordinal);

        a.Transport.Connect(room);
        b.Transport.Connect(room);
        PollUntil(() => a.Connected.Count == 1 && b.Connected.Count == 1 && host.Connected.Count == 2, "both clients", all);

        // Messages both ways, in order, and one of the largest a session sends.
        byte[] big = new byte[NetProtocol.MaxMessageBytes];
        new Random(1).NextBytes(big);
        for (byte i = 0; i < 10; i++)
        {
            a.Transport.Connections[0].Send([i]);
        }

        host.Transport.SendToMany(host.Transport.Connections, big);
        PollUntil(() => host.Messages.Count == 10 && a.Messages.Count == 1 && b.Messages.Count == 1, "the messages", all);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (byte)i), host.Messages.Select(m => m[0]));
        Assert.Equal(big, a.Messages[0]);
        Assert.Equal(big, b.Messages[0]);
        PollUntil(() => a.Transport.Connections[0].RoundTripTime > TimeSpan.Zero, "a round trip", all);

        // The host leaves; a takes over through the server's compare-and-swap; b follows a.
        host.Transport.Shutdown();
        PollUntil(() => a.Disconnected.Count == 1 && b.Disconnected.Count == 1, "the clients to see the host leave", all);
        Assert.Equal(DisconnectReason.HostLeft, a.Disconnected[0].Reason);
        string successor = a.Transport.MigrationAddress!;
        b.Transport.Connect(successor);
        Assert.Null(a.Transport.TakeOverHost(TransportRig.Advert("Taken over")));
        Assert.Equal(room, a.Transport.HostAddress);
        PollUntil(() => b.Connected.Count == 2 && a.Connected.Count == 2, "b to reach the new host", all);
        Assert.Equal(successor, b.Transport.RoomHostAddress);

        b.Transport.Connections[0].Send([7]);
        PollUntil(() => a.Messages.Count == 1, "a message to the new host", all);
    }

    [LiveFact]
    public void RoomList_ShowsTheRoom()
    {
        using var host = new LivePeer("host", Settings);
        host.Transport.StartHost(TransportRig.Advert("Listed room"));
        PollUntil(() => host.Transport.HostAddress is not null, "the room", [host]);
        using var browser = new PhotonRoomBrowser(Settings);
        browser.Start();
        PollUntil(() => browser.Rooms.Any(r => r.Address == host.Transport.HostAddress), "the room list", [host], browser);

        RoomInfo listed = browser.Rooms.First(r => r.Address == host.Transport.HostAddress);
        Assert.Equal("Listed room", listed.Room.Name);
        Assert.Equal(PhotonStatus.Connected, browser.Status);
        Assert.Equal("asia", browser.Region);
    }

    [LiveFact]
    public void BestRegion_SecureWebSockets_AndTheUdpFallback_Connect()
    {
        using var best = new LivePeer("best", PhotonSettings.FromBuild()!);
        best.Transport.StartHost(TransportRig.Advert());
        PollUntil(() => best.Transport.HostAddress is not null, "a room in the best region", [best]);
        Assert.NotNull(best.Transport.Region);

        using var wss = new LivePeer("wss", Settings with { UseWebSockets = true });
        wss.Transport.Connect(best.Transport.HostAddress!);
        PollUntil(() => wss.Connected.Count == 1, "a client over secure WebSockets", [best, wss]);

        RealtimeLink.UdpMasterPortOverride = 9;
        try
        {
            using var blocked = new LivePeer("blocked", Settings);
            blocked.Transport.Connect(best.Transport.HostAddress!);
            PollUntil(() => blocked.Connected.Count == 1, "a client whose UDP is blocked", [best, wss, blocked], timeout: TimeSpan.FromSeconds(60));
        }
        finally
        {
            RealtimeLink.UdpMasterPortOverride = null;
        }
    }

    [LiveFact]
    public void UnknownAppId_MissingRoom_AndBadRegion_AreReported()
    {
        using var stranger = new LivePeer("stranger", PhotonSettings.ForAppId("00000000-0000-0000-0000-000000000000"));
        stranger.Transport.Connect("asia/ABCDEF");
        using var lost = new LivePeer("lost", Settings);
        lost.Transport.Connect("asia/ZZZZZZ");
        using var nowhere = new LivePeer("nowhere", Settings.WithRegion("zz"));
        nowhere.Transport.Connect("ABCDEF");
        LivePeer[] all = [stranger, lost, nowhere];
        PollUntil(() => all.All(p => p.Disconnected.Count == 1), "the three to fail", all);

        Assert.Equal(PhotonError.InvalidAppId, stranger.Transport.LastError);
        Assert.Equal(PhotonError.RoomNotFound, lost.Transport.LastError);
        Assert.Contains(nowhere.Transport.LastError, new[] { PhotonError.RegionUnavailable, PhotonError.ServerError, PhotonError.NoInternet });
        Assert.All(all, p => Assert.Equal(PhotonStatus.Failed, p.Transport.Status));
    }
}
