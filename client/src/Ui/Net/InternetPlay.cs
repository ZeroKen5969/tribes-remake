using System.Runtime.CompilerServices;
using Godot;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Photon;
using Tribes.Session;

namespace Tribes.Client.Ui.Net;

/// <summary>
/// Internet play over Photon (docs/NETWORK.md): the settings of this build (the AppId embedded at
/// build time and the region chosen in the 「網際網路」 tab), the texts of Photon's errors, and the
/// link between a session and its <see cref="PhotonTransport"/>.
/// </summary>
public static class InternetPlay
{
    private static readonly ConditionalWeakTable<object, PhotonTransport> Transports = new();

    static InternetPlay()
    {
        // Photon's own warnings and errors go to the game log; the AppId is removed from them.
        PhotonDiagnostics.Log = line => GD.Print(line);
        Region = PhotonRegions.Normalize(DevOptions.Get("net-region"));
    }

    /// <summary>Whether this build can play over the internet (it was built with the Photon AppId).</summary>
    public static bool IsAvailable => PhotonSettings.IsConfigured;

    /// <summary>The region rooms are listed and created in for this run; null for the nearest one.</summary>
    public static string? Region { get; set; }

    /// <summary>The settings for <see cref="Region"/>, or null when the build has no AppId.</summary>
    public static PhotonSettings? Settings() => PhotonSettings.FromBuild()?.WithRegion(Region);

    /// <summary>Remembers which transport <paramref name="session"/> uses (a host or client session).</summary>
    public static void Register(object session, PhotonTransport transport) => Transports.AddOrUpdate(session, transport);

    /// <summary>The Photon transport of <paramref name="session"/>, or null for other transports.</summary>
    public static PhotonTransport? TransportOf(object? session) =>
        session is not null && Transports.TryGetValue(session, out PhotonTransport? transport) ? transport : null;

    /// <summary>
    /// When the match of <paramref name="runner"/> runs over Photon: if this device hosts it (also
    /// after taking over from the first host) and loses the room while the others play on, the
    /// runner stops and saves (<see cref="NetworkMatchRunner.ReportHostingLost"/>); the player can
    /// come back from the room list.
    /// </summary>
    public static void Watch(NetworkMatchRunner runner, object session)
    {
        if (TransportOf(session) is not { } transport)
        {
            return;
        }

        transport.StatusChanged += (_, _) =>
        {
            if (transport.HostingLost && runner.IsHost)
            {
                runner.ReportHostingLost();
            }
        };
    }

    /// <summary>What to tell the player about <paramref name="error"/>.</summary>
    public static string ErrorText(PhotonError error) => UiText.T(error switch
    {
        PhotonError.NotConfigured => "multiplayer.internet.unavailable",
        PhotonError.NoInternet => "multiplayer.internet.error.no_internet",
        PhotonError.Timeout => "multiplayer.internet.error.timeout",
        PhotonError.InvalidAppId => "multiplayer.internet.error.invalid_app",
        PhotonError.ServerFull => "multiplayer.internet.error.server_full",
        PhotonError.RegionUnavailable => "multiplayer.internet.error.region",
        PhotonError.RoomNotFound => "multiplayer.internet.error.room_not_found",
        PhotonError.RoomFull => "multiplayer.internet.error.room_full",
        PhotonError.RoomClosed => "multiplayer.internet.error.room_closed",
        PhotonError.HostNotAnswering => "multiplayer.internet.error.host_not_answering",
        PhotonError.ConnectionLost => "multiplayer.internet.error.connection_lost",
        PhotonError.HostReplaced => "multiplayer.internet.error.host_replaced",
        _ => "multiplayer.internet.error.server",
    });

    /// <summary>The name of a region for the region list (its code when the game has no name for it).</summary>
    public static string RegionName(string? code)
    {
        if (code is null)
        {
            return UiText.T("multiplayer.internet.region_best");
        }

        string key = "multiplayer.internet.region." + code;
        string name = UiText.T(key);
        return name == key ? code : name;
    }

    /// <summary>This player's client settings for an internet room.</summary>
    internal static ClientOptions ClientOptions(int preferredColour, bool spectate)
    {
        (string game, string content) = LobbyConnection.Versions();
        return new ClientOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = LobbyConnection.PlayerKey(),
            PlayerName = LobbyConnection.PlayerName(),
            PreferredColour = preferredColour,
            Spectate = spectate,
        };
    }
}
