using System;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Photon;

namespace Tribes.Client.Ui.Net;

/// <summary>Rooms on the internet (Photon): hosting and joining, as the LAN ones but over <see cref="PhotonTransport"/>.</summary>
public sealed partial class LobbyConnection
{
    /// <summary>The Photon transport when the room is on the internet, or null.</summary>
    public PhotonTransport? Internet { get; private set; }

    /// <summary>
    /// Hosts an internet room and makes it <see cref="Current"/>. Creating the room takes a moment:
    /// <see cref="Address"/> (the room code) is set once Photon made it; failures show in
    /// <see cref="Internet"/>'s status.
    /// </summary>
    /// <exception cref="InvalidOperationException">This build has no Photon AppId.</exception>
    public static LobbyConnection HostInternet(RoomMode mode, int preferredColour = -1)
    {
        CloseCurrent();
        PhotonSettings settings = InternetPlay.Settings() ?? throw new InvalidOperationException("This build has no Photon AppId.");
        (string game, string content) = Versions();
        string name = PlayerName();
        var transport = new PhotonTransport(settings);
        HostSession host;
        try
        {
            host = new HostSession(transport, new HostOptions
            {
                GameVersion = game,
                ContentVersion = content,
                RulesFingerprint = BuildFingerprint.Current,
                PlayerId = PlayerKey(),
                PlayerName = name,
                PreferredColour = preferredColour,
                Room = new RoomSettings
                {
                    Name = UiText.F("lobby.room_name", name),
                    Mode = mode,
                    AllowJoinInProgress = mode == RoomMode.Endless,
                },
            }, MatchSimulations.Create);
        }
        catch
        {
            transport.Dispose();
            throw;
        }

        // A hosted room holds a multicast lease like a LAN one: whoever closes the session gives it back.
        MulticastLease.Acquire();
        InternetPlay.Register(host, transport);
        Current = new LobbyConnection(host, null) { lastRoom = host.Room, Internet = transport };
        GD.Print($"LOBBY hosting on the internet ({settings.Region ?? "best region"}) mode={mode}");
        return Current;
    }

    /// <summary>
    /// Joins the internet room at <paramref name="address"/> (<c>region/CODE</c>, or a code in the
    /// chosen region) and makes it <see cref="Current"/>; <paramref name="spectate"/> joins as a new
    /// spectator. A player who holds a slot there gets it back either way.
    /// </summary>
    /// <exception cref="InvalidOperationException">This build has no Photon AppId.</exception>
    /// <exception cref="ArgumentException"><paramref name="address"/> is not a room code.</exception>
    public static LobbyConnection JoinInternet(string address, int preferredColour = -1, bool spectate = false)
    {
        CloseCurrent();
        PhotonSettings settings = InternetPlay.Settings() ?? throw new InvalidOperationException("This build has no Photon AppId.");
        var transport = new PhotonTransport(settings);
        var client = new ClientSession(transport, InternetPlay.ClientOptions(preferredColour, spectate), MatchSimulations.Create);

        // Once the session is over, nothing uses the transport: it leaves Photon. When this
        // player took over hosting, the new host session goes on with it.
        client.Ended += (_, e) =>
        {
            if (e.Reason != SessionEndReason.BecameHost)
            {
                transport.Shutdown();
            }
        };
        InternetPlay.Register(client, transport);
        Current = new LobbyConnection(null, client) { Internet = transport };
        try
        {
            client.Connect(address);
        }
        catch
        {
            CloseCurrent();
            transport.Dispose();
            throw;
        }

        GD.Print($"LOBBY joining {address} on the internet");
        return Current;
    }
}
