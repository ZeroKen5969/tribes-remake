using System;
using System.Linq;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>The client side: joining a room and reaching the actor that hosts it.</summary>
public sealed partial class PhotonTransport
{
    private PendingConnect? pending;
    private PhotonConnection? hostConnection;
    private string? roomCode;
    private string? roomRegion;

    /// <inheritdoc />
    /// <remarks>
    /// <c>actor:N</c>: whether that actor is in the room. A room address: whether the actor the
    /// room records as host is in it (and is not this peer).
    /// </remarks>
    public bool IsPeerPresent(string migrationAddress)
    {
        if (PhotonAddress.TryParseActor(migrationAddress, out int actor))
        {
            return IsActorPresent(actor);
        }

        if (!PhotonAddress.TryParseRoom(migrationAddress, out _, out string code) || !InRoom(code))
        {
            return false;
        }

        int host = RecordedHost();
        return host > 0 && host != link!.LocalActor && IsActorPresent(host);
    }

    private void StartConnect(string address)
    {
        int targetActor = 0;
        string? region;
        string code;
        if (PhotonAddress.TryParseActor(address, out int actor))
        {
            if (roomCode is null)
            {
                // No room to find the actor in.
                Role = TransportRole.Client;
                PhotonConnection lost = NewConnection(NextNonce(), 0, address, ConnectionState.Closed);
                Queue(TransportEventKind.Disconnected, lost, DisconnectReason.ConnectFailed);
                SetStatus(PhotonStatus.Failed, PhotonError.RoomNotFound);
                return;
            }

            targetActor = actor;
            code = roomCode;
            region = roomRegion;
        }
        else if (!PhotonAddress.TryParseRoom(address, out region, out code))
        {
            throw new ArgumentException($"\"{address}\" is neither a room code (region/CODE) nor an actor address.", nameof(address));
        }

        Role = TransportRole.Client;
        if (code != roomCode)
        {
            silentHost = 0;
        }

        PhotonConnection connection = NewConnection(NextNonce(), targetActor, address, ConnectionState.Pending);
        pending = new PendingConnect(connection, code, targetActor, clock.Now + settings.ConnectTimeout);
        SetStatus(PhotonStatus.Connecting, PhotonError.None);
        if (!InRoom(code) || (region is not null && link!.Region != region))
        {
            bool reusable = link is { State: LinkState.Master or LinkState.Lobby } current && (region is null || current.Region == region);
            if (!reusable)
            {
                roomRegion = region;
                StartLink(region ?? link?.Region ?? settings.Region);
            }
        }

        roomCode = code;
        AdvanceConnect();
    }

    /// <summary>Takes the pending connection one step further: join the room, then open the connection to its host.</summary>
    private void AdvanceConnect()
    {
        if (pending is not { } attempt || link is not { } current)
        {
            return;
        }

        if (current.State is LinkState.Master or LinkState.Lobby && !attempt.JoinRequested)
        {
            attempt.JoinRequested = true;
            if (!current.JoinRoom(attempt.Code))
            {
                FailPending(PhotonError.ServerError);
            }

            return;
        }

        if (current.State != LinkState.Room || current.RoomName != attempt.Code || attempt.OpenedTo != 0)
        {
            return;
        }

        int host = RecordedHost();
        if (host > 0 && host == current.LocalActor)
        {
            // The room says this peer hosts it (a take-over it did not hear back about in time).
            FailPending(PhotonError.HostNotAnswering);
            return;
        }

        if (host <= 0 || !IsActorPresent(host))
        {
            // Nobody hosts the room right now (a host migration is under way): wait for it.
            return;
        }

        if (attempt.TargetActor > 0 && host != attempt.TargetActor)
        {
            // Another peer took over: the session follows RoomHostAddress instead.
            FailPending(PhotonError.HostNotAnswering);
            return;
        }

        attempt.OpenedTo = host;
        attempt.Connection.BindActor(host);
        current.RaiseEvent(Wire.Open, Wire.EncodeOpen(attempt.Connection.Nonce), [host]);
    }

    /// <summary>The pending connection failed. Without a room the link is dropped (a room keeps it for a host migration).</summary>
    private void FailPending(PhotonError error)
    {
        if (pending is not { } attempt)
        {
            return;
        }

        pending = null;
        attempt.Connection.State = ConnectionState.Closed;
        Queue(TransportEventKind.Disconnected, attempt.Connection, DisconnectReason.ConnectFailed);
        if (InRoom(attempt.Code))
        {
            SetStatus(PhotonStatus.Connected, error);
        }
        else
        {
            DropLink(flush: false);
            SetStatus(PhotonStatus.Failed, error);
        }
    }

    private void CheckPendingTimeout(TimeSpan now)
    {
        if (pending is { } attempt && now >= attempt.Deadline)
        {
            FailPending(InRoom(attempt.Code) ? PhotonError.HostNotAnswering : PhotonError.Timeout);
        }
    }

    private void OnAccept(int sender, byte[]? content)
    {
        if (Role != TransportRole.Client || pending is not { } attempt || attempt.OpenedTo != sender
            || !Wire.TryDecodeNonce(content, out int nonce) || nonce != attempt.Connection.Nonce)
        {
            return;
        }

        pending = null;
        hostConnection = attempt.Connection;
        hostHeardAt = clock.Now;
        OpenConnection(attempt.Connection);
        SetStatus(PhotonStatus.Connected, PhotonError.None);
    }

    private void OnActorLeftClient(int actor)
    {
        if (hostConnection is { IsConnected: true } toHost && toHost.Actor == actor)
        {
            CloseConnection(toHost, DisconnectReason.HostLeft);
        }

        if (pending is { } attempt && (attempt.OpenedTo == actor || attempt.TargetActor == actor))
        {
            FailPending(PhotonError.HostNotAnswering);
        }
    }

    /// <summary>
    /// The host sent nothing (not even a ping) for <see cref="PhotonSettings.HostSilenceTimeout"/>
    /// while the server keeps answering this client: the host's game or device is gone although
    /// Photon still lists it. The connection ends as if the host had left, and the host counts as
    /// gone for this client from now on, so the session takes the host role over at once.
    /// </summary>
    private void CheckHostSilence(TimeSpan now)
    {
        TimeSpan limit = settings.HostSilenceTimeout;
        if (limit <= TimeSpan.Zero || Role != TransportRole.Client || hostConnection is not { IsConnected: true } toHost
            || link is not { State: LinkState.Room } current || now - hostHeardAt < limit
            || current.TimeSinceServerHeard >= limit / 2)
        {
            return;
        }

        silentHost = toHost.Actor;
        PhotonLog.Write($"PHOTON transport: the host (actor {silentHost}) sent nothing for {limit.TotalSeconds:0.#} s; treating it as gone");
        CloseConnection(toHost, DisconnectReason.HostLeft);
    }

    /// <summary>A pending connection of a client: which room, which actor, and how far it got.</summary>
    private sealed class PendingConnect(PhotonConnection connection, string code, int targetActor, TimeSpan deadline)
    {
        public PhotonConnection Connection { get; } = connection;

        /// <summary>The room to be in.</summary>
        public string Code { get; } = code;

        /// <summary>The actor to connect to, or 0 for whoever hosts the room.</summary>
        public int TargetActor { get; } = targetActor;

        public TimeSpan Deadline { get; } = deadline;

        public bool JoinRequested { get; set; }

        /// <summary>The actor the Open was sent to, or 0.</summary>
        public int OpenedTo { get; set; }
    }
}
