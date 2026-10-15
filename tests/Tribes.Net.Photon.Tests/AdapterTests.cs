using System;
using System.Collections.Generic;
using Photon.Realtime;
using Tribes.Net.Photon.Link;
using Tribes.Net.Photon.Tests.Support;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The Photon SDK adapter without a network: error mapping, calls before connecting, the SDK's log.</summary>
[Collection("Photon diagnostics")]
public sealed class AdapterTests
{
    [Theory]
    [InlineData(DisconnectCause.DisconnectByClientLogic, nameof(LinkFailure.ByClient))]
    [InlineData(DisconnectCause.ApplicationQuit, nameof(LinkFailure.ByClient))]
    [InlineData(DisconnectCause.DnsExceptionOnConnect, nameof(LinkFailure.Unreachable))]
    [InlineData(DisconnectCause.ExceptionOnConnect, nameof(LinkFailure.Unreachable))]
    [InlineData(DisconnectCause.ServerAddressInvalid, nameof(LinkFailure.Unreachable))]
    [InlineData(DisconnectCause.ClientTimeout, nameof(LinkFailure.Timeout))]
    [InlineData(DisconnectCause.ServerTimeout, nameof(LinkFailure.Timeout))]
    [InlineData(DisconnectCause.ClientServiceInactivity, nameof(LinkFailure.Timeout))]
    [InlineData(DisconnectCause.InvalidAuthentication, nameof(LinkFailure.InvalidAppId))]
    [InlineData(DisconnectCause.MaxCcuReached, nameof(LinkFailure.CcuLimit))]
    [InlineData(DisconnectCause.InvalidRegion, nameof(LinkFailure.InvalidRegion))]
    [InlineData(DisconnectCause.DisconnectByServerLogic, nameof(LinkFailure.ServerError))]
    [InlineData(DisconnectCause.DisconnectByDisconnectMessage, nameof(LinkFailure.ServerError))]
    public void DisconnectCauses_MapToFailures(DisconnectCause cause, string failure) =>
        Assert.Equal(Enum.Parse<LinkFailure>(failure), RealtimeLink.FailureOf(cause));

    [Theory]
    [InlineData(ErrorCode.GameDoesNotExist, nameof(LinkFailure.RoomNotFound))]
    [InlineData(ErrorCode.GameFull, nameof(LinkFailure.RoomFull))]
    [InlineData(ErrorCode.GameClosed, nameof(LinkFailure.RoomClosed))]
    [InlineData(ErrorCode.GameIdAlreadyExists, nameof(LinkFailure.RoomExists))]
    [InlineData(ErrorCode.InternalServerError, nameof(LinkFailure.ServerError))]
    public void RoomErrors_MapToFailures(int code, string failure) =>
        Assert.Equal(Enum.Parse<LinkFailure>(failure), RealtimeLink.RoomFailureOf((short)code));

    [Fact]
    public void Link_BeforeConnecting_RefusesRoomCalls_WithoutThrowing()
    {
        using var link = new RealtimeLink();

        Assert.Equal(LinkState.Disconnected, link.State);
        Assert.Null(link.Region);
        Assert.Null(link.RoomName);
        Assert.Equal(-1, link.LocalActor);
        Assert.Equal(-1, link.MasterActor);
        Assert.Empty(link.Actors);
        Assert.Empty(link.Regions);
        Assert.Null(link.RoomProperty("h"));
        Assert.Equal(TimeSpan.Zero, link.RoundTripTime);
        Assert.False(link.JoinLobby());
        Assert.False(link.JoinRoom("ABCDEF"));
        Assert.False(link.CreateRoom("ABCDEF", new LinkRoomOptions(4, new Dictionary<string, object>(), [])));
        Assert.False(link.RaiseEvent(1, [1], [2]));
        Assert.False(link.SetRoomProperties(new Dictionary<string, object> { ["h"] = 1 }, null));
        Assert.False(link.SetMasterClient(2));
        link.Service();
        link.Disconnect();
        Assert.False(link.SendOutgoing());
        link.Dispose();
        link.Dispose();
        link.Service();
    }

    [Fact]
    public void SdkLog_ReachesTheDiagnostics_WithoutTheAppId()
    {
        const string secret = "fedcba98-7654-3210-fedc-ba9876543210";
        _ = PhotonSettings.ForAppId(secret);
        PhotonLog.InstallSdkLog();
        var lines = new List<string>();
        Action<string>? before = PhotonDiagnostics.Log;
        PhotonDiagnostics.Log = line =>
        {
            // Other tests run at the same time and log their transports' states too.
            if (line.StartsWith("PHOTON sdk", StringComparison.Ordinal))
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            }
        };
        try
        {
            Log.Error("AppId " + secret + " refused");
            Log.Warn("prefix " + secret[..8] + "***");
            Log.Info("info is not passed on");
            Log.Exception(new InvalidOperationException("boom " + secret));
        }
        finally
        {
            PhotonDiagnostics.Log = before;
        }

        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.DoesNotContain("fedcba98", l, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.StartsWith("PHOTON sdk error:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("PHOTON sdk warning:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("PHOTON sdk exception:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public void CallbacksNobodyUses_ChangeNothing()
    {
        using var rig = new TransportRig();
        RigPeer host = rig.Host();
        using var browser = new PhotonRoomBrowser(PhotonSettings.ForAppId(FakePhotonCloud.AppId), rig.Cloud.CreateLink, rig.Clock);
        browser.Start();
        for (int i = 0; i < 20; i++)
        {
            rig.Step();
            browser.Poll();
        }

        ILinkListener browsing = rig.Cloud.Links[^1].Listener!;
        browsing.OnEnterRoomFailed(LinkFailure.RoomFull);
        browsing.OnJoinedRoom();
        browsing.OnActorEntered(5);
        browsing.OnActorLeft(5);
        browsing.OnRoomPropertiesChanged(["h"]);
        browsing.OnSetPropertiesFailed();
        browsing.OnMasterChanged(5);
        browsing.OnEvent(5, 1, [1]);
        ILinkListener hosting = host.Link.Listener!;
        hosting.OnJoinedLobby();
        hosting.OnRoomList([]);
        hosting.OnRoomPropertiesChanged(["a"]);
        rig.Step();
        browser.Poll();

        Assert.Equal(PhotonStatus.Connected, browser.Status);
        Assert.Single(browser.Rooms);
        Assert.Equal(PhotonStatus.Connected, host.Transport.Status);
        Assert.NotNull(host.Transport.HostAddress);
    }
}
