using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>The host side: creating the room, accepting connections, and taking over a room whose host left.</summary>
public sealed partial class PhotonTransport
{
    /// <summary>
    /// Most actors in a room: every player and spectator, plus room for players whose old
    /// connection has not timed out yet when they come back. The session admits people; Photon
    /// only has to let them into the room.
    /// </summary>
    internal const int MaxRoomActors = RoomLimits.MaxPlayers + RoomLimits.MaxSpectators + 4;

    /// <summary>Photon numbers the actors of a room from 1, in the order they join.</summary>
    private const int FirstActor = 1;

    private const int MaxCreateAttempts = 5;
    private static readonly TimeSpan RehostDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxRehostDelay = TimeSpan.FromSeconds(30);
    private readonly Dictionary<int, PhotonConnection> clientsByActor = [];
    private RoomAdvertisement? advertisement;
    private byte[]? publishedAdvertisement;
    private bool hostConfirmed;
    private int createAttempts;
    private bool takingOver;
    private bool takeOverRefused;
    private RoomAdvertisement? takeOverAdvertisement;
    private TimeSpan rehostAt = TimeSpan.MaxValue;
    private TimeSpan rehostDelay = RehostDelay;
    private bool rehostWanted;

    /// <inheritdoc />
    /// <remarks>
    /// Connects to Photon and creates a room with a new code; <see cref="HostAddress"/> is set once
    /// the room exists. Failures show in <see cref="Status"/> and <see cref="LastError"/>.
    /// </remarks>
    public void StartHost(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role != TransportRole.None)
        {
            throw new InvalidOperationException("The transport is already hosting or connected.");
        }

        Role = TransportRole.Host;
        this.advertisement = advertisement;
        HostingLost = false;
        BeginHosting(settings.Region);
    }

    /// <inheritdoc />
    public void UpdateAdvertisement(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        if (Role != TransportRole.Host)
        {
            throw new InvalidOperationException("Only a hosting transport has an advertisement.");
        }

        this.advertisement = advertisement;
        PublishAdvertisement();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Asks Photon to change the room property <c>h</c> from the old host to this actor, only if it
    /// still names the old host, and waits up to <see cref="PhotonSettings.TakeOverTimeout"/> for
    /// the answer (one round trip). When the answer does not come (or this peer is not in the room),
    /// the room's address is returned: the session connects to whoever hosts the room, and tries
    /// again if that turns out to be nobody.
    /// </remarks>
    public string? TakeOverHost(RoomAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Role != TransportRole.Client)
        {
            throw new InvalidOperationException("Only a client in a room can take over hosting it.");
        }

        string fallback = RoomAddress ?? PhotonAddress.Actor(1);
        if (link is not { State: LinkState.Room } current)
        {
            return fallback;
        }

        int self = current.LocalActor;
        int previous = RecordedHost();
        if (previous == self)
        {
            BecomeHost(advertisement);
            return null;
        }

        if (previous > 0 && IsActorPresent(previous))
        {
            return PhotonAddress.Actor(previous);
        }

        byte[] encoded = Wire.EncodeAdvertisement(advertisement);
        var claim = new Dictionary<string, object> { [Wire.HostProperty] = self, [Wire.AdvertisementProperty] = encoded };
        Dictionary<string, object>? expected = previous > 0 ? new() { [Wire.HostProperty] = previous } : null;
        takeOverRefused = false;
        takingOver = true;
        takeOverAdvertisement = advertisement;
        try
        {
            if (!current.SetRoomProperties(claim, expected))
            {
                return fallback;
            }

            TimeSpan deadline = clock.Now + settings.TakeOverTimeout;
            while (true)
            {
                ServiceLink(current);
                if (Role == TransportRole.Host)
                {
                    return null;
                }

                if (!ReferenceEquals(link, current) || current.State != LinkState.Room)
                {
                    return fallback;
                }

                int recorded = RecordedHost();
                if (recorded != previous && recorded > 0 && IsActorPresent(recorded))
                {
                    return PhotonAddress.Actor(recorded);
                }

                if (takeOverRefused || clock.Now >= deadline)
                {
                    return fallback;
                }

                wait(PumpStep);
            }
        }
        finally
        {
            takingOver = false;
            takeOverAdvertisement = null;
        }
    }

    private void BeginHosting(string? region)
    {
        roomCode = PhotonAddress.NewCode(random);
        roomRegion = region;
        createAttempts = 0;
        hostConfirmed = false;
        publishedAdvertisement = null;
        SetStatus(PhotonStatus.Connecting, PhotonError.None);
        StartLink(region);
    }

    private void CreateRoomNow()
    {
        if (link is not { } current || roomCode is null || advertisement is null)
        {
            return;
        }

        // The creator is the first actor of a new room: the room names its host from the start,
        // so that a take-over always has a value to compare.
        byte[] encoded = Wire.EncodeAdvertisement(advertisement);
        var properties = new Dictionary<string, object> { [Wire.HostProperty] = FirstActor, [Wire.AdvertisementProperty] = encoded };
        if (!current.CreateRoom(roomCode, new LinkRoomOptions(MaxRoomActors, properties, [Wire.AdvertisementProperty])))
        {
            HostFailed(PhotonError.ServerError);
            return;
        }

        publishedAdvertisement = encoded;
    }

    private void OnCreateRoomFailed(LinkFailure cause)
    {
        if (cause == LinkFailure.RoomExists && ++createAttempts < MaxCreateAttempts)
        {
            roomCode = PhotonAddress.NewCode(random);
            CreateRoomNow();
            return;
        }

        HostFailed(ErrorOf(cause, wasConnected: false));
    }

    private void HostFailed(PhotonError error)
    {
        DropLink(flush: false);
        hostConfirmed = false;
        ScheduleRehost(error);
        SetStatus(PhotonStatus.Failed, error);
    }

    /// <summary>
    /// After this host lost a room it had alone, it opens one again: the next attempt after a
    /// pause that grows with every failure, until it works or the problem cannot go away by waiting.
    /// </summary>
    private void ScheduleRehost(PhotonError error)
    {
        if (!rehostWanted)
        {
            return;
        }

        if (error is PhotonError.InvalidAppId or PhotonError.RegionUnavailable)
        {
            rehostWanted = false;
            return;
        }

        rehostAt = clock.Now + rehostDelay;
        rehostDelay = rehostDelay * 2 > MaxRehostDelay ? MaxRehostDelay : rehostDelay * 2;
    }

    /// <summary>The room exists: record this actor as its host and make it the master client.</summary>
    private void OnHostRoomJoined()
    {
        if (link is not { } current)
        {
            return;
        }

        if (current.LocalActor != FirstActor)
        {
            current.SetRoomProperties(
                new Dictionary<string, object> { [Wire.HostProperty] = current.LocalActor },
                new Dictionary<string, object> { [Wire.HostProperty] = FirstActor });
        }

        hostConfirmed = true;
        rehostWanted = false;
        rehostDelay = RehostDelay;
        ClaimMaster(current);
        PublishAdvertisement();
        PhotonLog.Write($"PHOTON transport: hosting room {current.Region}/{roomCode}");
        SetStatus(PhotonStatus.Connected, PhotonError.None);
    }

    private void PublishAdvertisement()
    {
        if (!hostConfirmed || advertisement is null || link is not { State: LinkState.Room } current)
        {
            return;
        }

        byte[] encoded = Wire.EncodeAdvertisement(advertisement);
        if (publishedAdvertisement is not null && encoded.AsSpan().SequenceEqual(publishedAdvertisement))
        {
            return;
        }

        publishedAdvertisement = encoded;
        current.SetRoomProperties(new Dictionary<string, object> { [Wire.AdvertisementProperty] = encoded }, null);
    }

    /// <summary>The take-over went through: from now on this peer hosts the room.</summary>
    private void BecomeHost(RoomAdvertisement newAdvertisement)
    {
        if (Role == TransportRole.Host || link is not { } current)
        {
            return;
        }

        // An attempt to reach the old host is over; it ends like any connection this side closes.
        if (pending is { } abandoned)
        {
            pending = null;
            abandoned.Connection.ClosedByThisSide = true;
            abandoned.Connection.State = ConnectionState.Closed;
            Queue(TransportEventKind.Disconnected, abandoned.Connection, DisconnectReason.Local);
        }

        Role = TransportRole.Host;
        advertisement = newAdvertisement;
        publishedAdvertisement = Wire.EncodeAdvertisement(newAdvertisement);
        hostConfirmed = true;
        HostingLost = false;
        ClaimMaster(current);
        PublishAdvertisement();
        PhotonLog.Write($"PHOTON transport: took over hosting room {current.Region}/{roomCode} as actor {current.LocalActor}");
        SetStatus(PhotonStatus.Connected, PhotonError.None);
    }

    /// <summary>Only the master client may change some room settings; the host keeps that role.</summary>
    private static void ClaimMaster(IPhotonLink current)
    {
        if (current.MasterActor != current.LocalActor)
        {
            current.SetMasterClient(current.LocalActor);
        }
    }

    private void OnMasterChanged(int actor)
    {
        if (Role == TransportRole.Host && hostConfirmed && link is { State: LinkState.Room } current && actor != current.LocalActor)
        {
            current.SetMasterClient(current.LocalActor);
        }
    }

    private void OnHostPropertyChanged()
    {
        if (link is not { State: LinkState.Room } current)
        {
            return;
        }

        int recorded = RecordedHost();
        if (recorded != silentHost)
        {
            silentHost = 0;
        }

        if (Role == TransportRole.Client)
        {
            if (takingOver && recorded == current.LocalActor && takeOverAdvertisement is { } claimed)
            {
                // Become the host right inside the callback, so that the Open events right
                // behind this change are already answered as host.
                BecomeHost(claimed);
                return;
            }

            // Another peer took the room over while this client still talked to the old host.
            if (hostConnection is { IsConnected: true } toHost && recorded > 0 && recorded != toHost.Actor)
            {
                CloseConnection(toHost, DisconnectReason.HostLeft);
            }

            AdvanceConnect();
            return;
        }

        if (Role == TransportRole.Host && hostConfirmed && recorded > 0 && recorded != current.LocalActor)
        {
            // Another peer hosts the room now; this one cannot go on hosting it.
            hostConfirmed = false;
            HostingLost = true;
            foreach (PhotonConnection client in OpenConnections())
            {
                CloseConnection(client, DisconnectReason.ConnectionLost);
            }

            SetStatus(PhotonStatus.Failed, PhotonError.HostReplaced);
        }
    }

    /// <summary>An actor asks for a connection: accepted while this peer hosts, refused otherwise.</summary>
    private void OnOpen(int sender, byte[]? content)
    {
        if (!Wire.TryDecodeOpen(content, out byte version, out int nonce))
        {
            return;
        }

        if (Role != TransportRole.Host || !hostConfirmed || version != Wire.Version)
        {
            SendControl(Wire.Close, Wire.EncodeNonce(nonce), sender);
            return;
        }

        if (clientsByActor.TryGetValue(sender, out PhotonConnection? previous))
        {
            // The same actor connects again: its earlier connection is gone.
            CloseConnection(previous, DisconnectReason.Remote);
        }

        PhotonConnection connection = NewConnection(nonce, sender, PhotonAddress.Actor(sender), ConnectionState.Pending);
        clientsByActor[sender] = connection;
        SendControl(Wire.Accept, Wire.EncodeNonce(nonce), sender);
        OpenConnection(connection);
    }

    /// <summary>
    /// The hosting link was lost. Alone in the room, the transport opens a new room after a moment
    /// (with a new code), so the game can still be joined. With others in the room, they carry on
    /// with a new host: <see cref="HostingLost"/> tells the game.
    /// </summary>
    private void OnHostLinkLost(PhotonError error, bool othersWereThere)
    {
        bool wasHosting = hostConfirmed;
        hostConfirmed = false;
        if (wasHosting && othersWereThere)
        {
            HostingLost = true;
            SetStatus(PhotonStatus.Failed, error);
            return;
        }

        rehostWanted |= wasHosting;
        ScheduleRehost(error);
        SetStatus(PhotonStatus.Failed, error);
    }

    private void CheckRehost(TimeSpan now)
    {
        if (Role != TransportRole.Host || link is not null || HostingLost || now < rehostAt)
        {
            return;
        }

        rehostAt = TimeSpan.MaxValue;
        PhotonLog.Write("PHOTON transport: opening the room again after the connection was lost");
        BeginHosting(roomRegion ?? settings.Region);
    }

    private void ResetHosting()
    {
        clientsByActor.Clear();
        draining.Clear();
        advertisement = null;
        publishedAdvertisement = null;
        hostConfirmed = false;
        HostingLost = false;
        rehostAt = TimeSpan.MaxValue;
        rehostWanted = false;
        rehostDelay = RehostDelay;
        pending = null;
        hostConnection = null;
    }
}
