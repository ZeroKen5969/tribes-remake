using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>What the link reports: connection changes, room membership and the events of other actors.</summary>
public sealed partial class PhotonTransport
{
    /// <summary>The link lost its connection (or never got one).</summary>
    private void OnLinkLost(LinkFailure cause)
    {
        PhotonError error = ErrorOf(cause, linkReachedServer);
        if (error == PhotonError.None)
        {
            error = PhotonError.ConnectionLost;
        }

        bool othersWereThere = othersInRoom > 0 || clientsByActor.Count > 0;
        DropLink(flush: false);
        if (pending is not null)
        {
            FailPending(error);
        }

        foreach (PhotonConnection connection in OpenConnections())
        {
            CloseConnection(connection, DisconnectReason.ConnectionLost);
        }

        foreach (PhotonConnection connection in draining.ToArray())
        {
            DropQueued(connection);
        }

        if (Role == TransportRole.Host)
        {
            OnHostLinkLost(error, othersWereThere);
        }
        else
        {
            SetStatus(PhotonStatus.Failed, error);
        }
    }

    private void OnConnectedToMaster()
    {
        linkReachedServer = true;
        if (Role == TransportRole.Host)
        {
            CreateRoomNow();
        }
        else
        {
            AdvanceConnect();
        }
    }

    private void OnJoinedRoom()
    {
        roomRegion = link?.Region ?? roomRegion;
        CountOthers();
        if (Role == TransportRole.Host)
        {
            OnHostRoomJoined();
        }
        else
        {
            AdvanceConnect();
        }
    }

    private void OnEnterRoomFailed(LinkFailure cause)
    {
        if (Role == TransportRole.Host)
        {
            OnCreateRoomFailed(cause);
        }
        else if (pending is not null)
        {
            FailPending(ErrorOf(cause, wasConnected: false));
        }
    }

    private void OnActorLeft(int actor)
    {
        CountOthers();
        if (actor == silentHost)
        {
            silentHost = 0;
        }

        foreach (PhotonConnection gone in draining.FindAll(c => c.Actor == actor))
        {
            DropQueued(gone);
        }

        if (Role == TransportRole.Host)
        {
            if (clientsByActor.TryGetValue(actor, out PhotonConnection? client))
            {
                CloseConnection(client, DisconnectReason.ConnectionLost);
            }

            return;
        }

        OnActorLeftClient(actor);
    }

    private void CountOthers() => othersInRoom = link is { State: LinkState.Room } current ? Math.Max(0, current.Actors.Count - 1) : 0;

    private void OnEvent(int sender, byte code, byte[]? content)
    {
        if (hostConnection is { IsConnected: true } toHost && toHost.Actor == sender)
        {
            hostHeardAt = clock.Now;
        }

        switch (code)
        {
            case Wire.Open:
                OnOpen(sender, content);
                break;
            case Wire.Accept:
                OnAccept(sender, content);
                break;
            case Wire.Close:
                OnClose(sender, content);
                break;
            case Wire.Ping:
                if (ConnectionOf(sender) is { } pinged)
                {
                    SendControl(Wire.Pong, content ?? [], pinged.Actor);
                }

                break;
            case Wire.Pong:
                if (ConnectionOf(sender) is { } ponged && Wire.TryDecodeStamp(content, out TimeSpan stamp))
                {
                    ponged.AddRttSample(clock.Now - stamp);
                }

                break;
            case Wire.Data or Wire.Fragment:
                Deliver(sender, code, content);
                break;
            case Wire.Credit:
                OnCredit(sender, content);
                break;
            default:
                // Events of newer versions are ignored.
                break;
        }
    }

    /// <summary>The open connection to <paramref name="actor"/>, or null.</summary>
    private PhotonConnection? ConnectionOf(int actor)
    {
        if (Role == TransportRole.Host)
        {
            return clientsByActor.TryGetValue(actor, out PhotonConnection? client) && client.IsConnected ? client : null;
        }

        return hostConnection is { IsConnected: true } toHost && toHost.Actor == actor ? toHost : null;
    }

    private void Deliver(int sender, byte code, byte[]? content)
    {
        if (ConnectionOf(sender) is not { } connection)
        {
            return;
        }

        try
        {
            byte[]? message;
            if (code == Wire.Data)
            {
                if (connection.Assembler.IsAssembling || content is null || content.Length > settings.MaxMessageBytes)
                {
                    throw new ProtocolException("A message arrived in the middle of another one, or too large.");
                }

                message = content;
            }
            else
            {
                message = connection.Assembler.Add(content);
            }

            CountReceived(connection, content!.Length);
            if (message is not null)
            {
                Queue(TransportEventKind.Message, connection, DisconnectReason.None, message);
            }
        }
        catch (ProtocolException e)
        {
            PhotonLog.Write($"PHOTON transport: closing the connection to actor {sender}: {e.Message}");
            SendControl(Wire.Close, Wire.EncodeNonce(connection.Nonce), sender);
            CloseConnection(connection, DisconnectReason.ProtocolError);
        }
    }

    private void OnClose(int sender, byte[]? content)
    {
        if (!Wire.TryDecodeNonce(content, out int nonce))
        {
            return;
        }

        if (pending is { } attempt && attempt.OpenedTo == sender && attempt.Connection.Nonce == nonce)
        {
            FailPending(PhotonError.HostNotAnswering);
            return;
        }

        if (ConnectionOf(sender) is { } connection && connection.Nonce == nonce)
        {
            CloseConnection(connection, DisconnectReason.Remote);
        }

        // The other side closed a connection this side was still sending on: nobody reads it any more.
        foreach (PhotonConnection closed in draining.FindAll(c => c.Actor == sender && c.Nonce == nonce))
        {
            DropQueued(closed);
        }
    }

    private static PhotonError ErrorOf(LinkFailure cause, bool wasConnected) => cause switch
    {
        LinkFailure.None or LinkFailure.ByClient => PhotonError.None,
        LinkFailure.Unreachable => wasConnected ? PhotonError.ConnectionLost : PhotonError.NoInternet,
        LinkFailure.Timeout => wasConnected ? PhotonError.ConnectionLost : PhotonError.Timeout,
        LinkFailure.InvalidAppId => PhotonError.InvalidAppId,
        LinkFailure.CcuLimit => PhotonError.ServerFull,
        LinkFailure.InvalidRegion => PhotonError.RegionUnavailable,
        LinkFailure.RoomNotFound => PhotonError.RoomNotFound,
        LinkFailure.RoomFull => PhotonError.RoomFull,
        LinkFailure.RoomClosed => PhotonError.RoomClosed,
        _ => PhotonError.ServerError,
    };

    /// <summary>Forwards a link's callbacks; those of a link that was dropped are ignored.</summary>
    private sealed class LinkCallbacks(PhotonTransport owner, IPhotonLink source) : ILinkListener
    {
        private bool Current => ReferenceEquals(owner.link, source);

        public void OnConnectedToMaster()
        {
            if (Current)
            {
                owner.OnConnectedToMaster();
            }
        }

        public void OnDisconnected(LinkFailure cause)
        {
            if (Current)
            {
                owner.OnLinkLost(cause);
            }
        }

        public void OnJoinedLobby()
        {
            // The transport never joins the lobby.
        }

        public void OnRoomList(IReadOnlyList<LinkRoom> rooms)
        {
            // The transport never joins the lobby.
        }

        public void OnEnterRoomFailed(LinkFailure cause)
        {
            if (Current)
            {
                owner.OnEnterRoomFailed(cause);
            }
        }

        public void OnJoinedRoom()
        {
            if (Current)
            {
                owner.OnJoinedRoom();
            }
        }

        public void OnActorEntered(int actor)
        {
            // Peers announce themselves with an Open event.
            if (Current)
            {
                owner.CountOthers();
            }
        }

        public void OnActorLeft(int actor)
        {
            if (Current)
            {
                owner.OnActorLeft(actor);
            }
        }

        public void OnRoomPropertiesChanged(IReadOnlyCollection<string> keys)
        {
            if (Current && keys.Contains(Wire.HostProperty))
            {
                owner.OnHostPropertyChanged();
            }
        }

        public void OnSetPropertiesFailed()
        {
            if (Current)
            {
                owner.takeOverRefused = true;
            }
        }

        public void OnMasterChanged(int actor)
        {
            if (Current)
            {
                owner.OnMasterChanged(actor);
            }
        }

        public void OnEvent(int sender, byte code, byte[]? content)
        {
            if (Current)
            {
                owner.OnEvent(sender, code, content);
            }
        }
    }
}
