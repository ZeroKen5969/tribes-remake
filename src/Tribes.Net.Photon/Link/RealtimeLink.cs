using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Client;
using Photon.Realtime;

namespace Tribes.Net.Photon.Link;

/// <summary>
/// <see cref="IPhotonLink"/> on Photon's <see cref="RealtimeClient"/> (Photon Realtime 5). The
/// client's callbacks arrive during <see cref="Service"/> and are passed on to the listener.
/// </summary>
internal sealed class RealtimeLink : IPhotonLink, IConnectionCallbacks, IMatchmakingCallbacks, IInRoomCallbacks, ILobbyCallbacks, IOnEventCallback
{
    /// <summary>The region ping summary of the last best-region connect, so the next connect pings fewer regions.</summary>
    private static string? bestRegionSummary;

    private readonly RealtimeClient client;
    private bool disposed;

    public RealtimeLink()
    {
        PhotonLog.InstallSdkLog();
        client = new RealtimeClient(ConnectionProtocol.Udp);
        client.AddCallbackTarget(this);
        client.OpResponseReceived += OnOperationResponse;
    }

    /// <summary>
    /// When set, the UDP port of the master server (normally 27001): a port nobody answers on
    /// makes UDP fail the way a blocking network does, to check the switch to WebSockets.
    /// </summary>
    internal static ushort? UdpMasterPortOverride { get; set; }

    public ILinkListener? Listener { get; set; }

    public LinkState State { get; private set; }

    public string? Region => string.IsNullOrEmpty(client.CurrentRegion) ? null : client.CurrentRegion;

    public TimeSpan RoundTripTime => TimeSpan.FromMilliseconds(client.RealtimePeer.Stats.RoundtripTime);

    public TimeSpan TimeSinceServerHeard =>
        State == LinkState.Disconnected ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(client.RealtimePeer.LastReceiveDeltaTime);

    public string? RoomName => State == LinkState.Room ? client.CurrentRoom?.Name : null;

    public int LocalActor => State == LinkState.Room ? client.LocalPlayer.ActorNumber : -1;

    public int MasterActor => State == LinkState.Room && client.CurrentRoom is { } room ? room.MasterClientId : -1;

    public IReadOnlyCollection<int> Actors =>
        State == LinkState.Room && client.CurrentRoom is { } room
            ? [.. room.Players.Values.Where(p => !p.IsInactive).Select(p => p.ActorNumber)]
            : [];

    /// <summary>The regions the name server offered in the last best-region connect, with their pings (ms).</summary>
    public IReadOnlyList<(string Code, int PingMs)> Regions { get; private set; } = [];

    public object? RoomProperty(string key) =>
        State == LinkState.Room && client.CurrentRoom?.CustomProperties is { } properties && properties.TryGetValue(key, out object? value) ? value : null;

    public bool Connect(LinkConnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = new AppSettings
        {
            AppIdRealtime = options.AppId,
            AppVersion = options.AppVersion,
            FixedRegion = options.Region,
            Protocol = options.UseWebSockets ? ConnectionProtocol.WebSocketSecure : ConnectionProtocol.Udp,
            EnableProtocolFallback = options.AllowWebSocketFallback,
            BestRegionSummaryFromStorage = options.Region is null ? bestRegionSummary : null,
            ClientLogging = LogLevel.Warning,
            NetworkLogging = LogLevel.Error,
        };
        if (UdpMasterPortOverride is { } port)
        {
            client.ProtocolPorts.Udp.MasterServer = port;
        }

        State = LinkState.Connecting;
        bool started = Guard(() => client.ConnectUsingSettings(settings));
        if (!started)
        {
            State = LinkState.Disconnected;
        }

        return started;
    }

    public void Disconnect()
    {
        if (client.IsConnected)
        {
            Guard(() =>
            {
                client.Disconnect();
                return true;
            });
        }
    }

    public bool JoinLobby() => Guard(() => client.OpJoinLobby());

    public bool CreateRoom(string name, LinkRoomOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var roomOptions = new RoomOptions
        {
            MaxPlayers = options.MaxPlayers,
            IsVisible = true,
            IsOpen = true,
            PlayerTtl = 0,
            EmptyRoomTtl = 0,
            CleanupCacheOnLeave = true,
            BroadcastPropsChangeToAll = true,
            PublishUserId = false,
            CustomRoomProperties = ToHashtable(options.Properties),
            CustomRoomPropertiesForLobby = [.. options.LobbyProperties.Cast<object>()],
        };
        bool sent = Guard(() => client.OpCreateRoom(new EnterRoomArgs { RoomName = name, RoomOptions = roomOptions }));
        if (sent)
        {
            State = LinkState.Joining;
        }

        return sent;
    }

    public bool JoinRoom(string name)
    {
        bool sent = Guard(() => client.OpJoinRoom(new EnterRoomArgs { RoomName = name }));
        if (sent)
        {
            State = LinkState.Joining;
        }

        return sent;
    }

    public bool RaiseEvent(byte code, byte[] content, IReadOnlyList<int> targets)
    {
        var args = new RaiseEventArgs { TargetActors = [.. targets], CachingOption = EventCaching.DoNotCache };
        return Guard(() => client.OpRaiseEvent(code, content, args, SendOptions.SendReliable));
    }

    public bool SetRoomProperties(IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, object>? expected) =>
        Guard(() => client.OpSetCustomPropertiesOfRoom(ToHashtable(values), expected is null ? null : ToHashtable(expected)));

    public bool SetMasterClient(int actor) =>
        client.CurrentRoom?.GetPlayer(actor) is { } player && Guard(() => client.CurrentRoom.SetMasterClient(player));

    public void Service()
    {
        if (!disposed)
        {
            Guard(() =>
            {
                client.Service();
                return true;
            });
        }
    }

    public bool SendOutgoing() => !disposed && Guard(client.SendOutgoingCommands);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Listener = null;
        client.RemoveCallbackTarget(this);
        client.OpResponseReceived -= OnOperationResponse;
        Guard(() =>
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }

            // Lets the disconnect go out and stops the client's keep-alive thread.
            client.Service();
            client.Handler?.StopFallbackSendAckThread();
            return true;
        });
    }

    void IConnectionCallbacks.OnConnected()
    {
        // The workflow goes on to the master server.
    }

    void IConnectionCallbacks.OnConnectedToMaster()
    {
        State = LinkState.Master;
        if (client.SummaryToCache is { Length: > 0 } summary)
        {
            bestRegionSummary = summary;
        }

        Listener?.OnConnectedToMaster();
    }

    void IConnectionCallbacks.OnDisconnected(DisconnectCause cause)
    {
        if (cause is not (DisconnectCause.DisconnectByClientLogic or DisconnectCause.ApplicationQuit))
        {
            PhotonLog.Write($"PHOTON link disconnected: {cause} (region {Region ?? "none"}, state {State})");
        }

        State = LinkState.Disconnected;
        Listener?.OnDisconnected(FailureOf(cause));
    }

    void IConnectionCallbacks.OnRegionListReceived(RegionHandler regionHandler)
    {
        Regions = [.. regionHandler.EnabledRegions.Select(r => (r.Code, r.Ping))];
    }

    void IConnectionCallbacks.OnCustomAuthenticationResponse(Dictionary<string, object> data)
    {
        // No custom authentication.
    }

    void IConnectionCallbacks.OnCustomAuthenticationFailed(string debugMessage)
    {
        // No custom authentication; the disconnect that follows reports it.
    }

    void ILobbyCallbacks.OnJoinedLobby()
    {
        State = LinkState.Lobby;
        Listener?.OnJoinedLobby();
    }

    void ILobbyCallbacks.OnLeftLobby()
    {
        State = LinkState.Master;
    }

    void ILobbyCallbacks.OnRoomListUpdate(List<RoomInfo> roomList)
    {
        var rooms = new List<LinkRoom>(roomList.Count);
        foreach (RoomInfo info in roomList)
        {
            var properties = new Dictionary<string, object>();
            if (info.CustomProperties is { } custom)
            {
                foreach (object key in custom.Keys)
                {
                    if (key is string name && custom[key] is { } value)
                    {
                        properties[name] = value;
                    }
                }
            }

            rooms.Add(new LinkRoom(info.Name, info.RemovedFromList, info.PlayerCount, info.MaxPlayers, properties));
        }

        Listener?.OnRoomList(rooms);
    }

    void ILobbyCallbacks.OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics)
    {
        // Not used.
    }

    void IMatchmakingCallbacks.OnFriendListUpdate(List<FriendInfo> friendList)
    {
        // Not used.
    }

    void IMatchmakingCallbacks.OnCreatedRoom()
    {
        // OnJoinedRoom follows.
    }

    void IMatchmakingCallbacks.OnCreateRoomFailed(short returnCode, string message) => EnterFailed(returnCode);

    private void EnterFailed(short returnCode)
    {
        State = client.InLobby ? LinkState.Lobby : client.IsConnectedAndReady ? LinkState.Master : State;
        Listener?.OnEnterRoomFailed(RoomFailureOf(returnCode));
    }

    void IMatchmakingCallbacks.OnJoinedRoom()
    {
        State = LinkState.Room;
        Listener?.OnJoinedRoom();
    }

    void IMatchmakingCallbacks.OnJoinRoomFailed(short returnCode, string message) => EnterFailed(returnCode);

    void IMatchmakingCallbacks.OnJoinRandomFailed(short returnCode, string message) => EnterFailed(returnCode);

    void IMatchmakingCallbacks.OnLeftRoom()
    {
        // Leaving happens only by disconnecting; OnDisconnected follows.
    }

    void IInRoomCallbacks.OnPlayerEnteredRoom(Player newPlayer) => Listener?.OnActorEntered(newPlayer.ActorNumber);

    void IInRoomCallbacks.OnPlayerLeftRoom(Player otherPlayer) => Listener?.OnActorLeft(otherPlayer.ActorNumber);

    void IInRoomCallbacks.OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        string[] keys = [.. propertiesThatChanged.Keys.OfType<string>()];
        if (keys.Length > 0)
        {
            Listener?.OnRoomPropertiesChanged(keys);
        }
    }

    void IInRoomCallbacks.OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        // Not used.
    }

    void IInRoomCallbacks.OnMasterClientSwitched(Player newMasterClient) => Listener?.OnMasterChanged(newMasterClient.ActorNumber);

    void IOnEventCallback.OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code >= 200)
        {
            // Photon's own events are handled by the client.
            return;
        }

        byte[]? content = photonEvent.CustomData is byte[] bytes ? (byte[])bytes.Clone() : null;
        Listener?.OnEvent(photonEvent.Sender, photonEvent.Code, content);
    }

    private void OnOperationResponse(OperationResponse response)
    {
        if (response.OperationCode == OperationCode.SetProperties && response.ReturnCode != ErrorCode.Ok)
        {
            Listener?.OnSetPropertiesFailed();
        }
    }

    /// <summary>Why creating or joining a room failed, from Photon's return code.</summary>
    internal static LinkFailure RoomFailureOf(short returnCode) => returnCode switch
    {
        ErrorCode.GameDoesNotExist => LinkFailure.RoomNotFound,
        ErrorCode.GameFull => LinkFailure.RoomFull,
        ErrorCode.GameClosed => LinkFailure.RoomClosed,
        ErrorCode.GameIdAlreadyExists => LinkFailure.RoomExists,
        _ => LinkFailure.ServerError,
    };

    /// <summary>Why the client disconnected, from Photon's cause.</summary>
    internal static LinkFailure FailureOf(DisconnectCause cause) => cause switch
    {
        DisconnectCause.None or DisconnectCause.DisconnectByClientLogic or DisconnectCause.ApplicationQuit => LinkFailure.ByClient,
        DisconnectCause.ExceptionOnConnect or DisconnectCause.DnsExceptionOnConnect or DisconnectCause.ServerAddressInvalid or DisconnectCause.Exception => LinkFailure.Unreachable,
        DisconnectCause.ServerTimeout or DisconnectCause.ClientTimeout or DisconnectCause.ClientServiceInactivity => LinkFailure.Timeout,
        DisconnectCause.InvalidAuthentication or DisconnectCause.CustomAuthenticationFailed or DisconnectCause.AuthenticationTicketExpired => LinkFailure.InvalidAppId,
        DisconnectCause.MaxCcuReached => LinkFailure.CcuLimit,
        DisconnectCause.InvalidRegion => LinkFailure.InvalidRegion,
        _ => LinkFailure.ServerError,
    };

    private static PhotonHashtable ToHashtable(IReadOnlyDictionary<string, object> values)
    {
        var table = new PhotonHashtable();
        foreach (KeyValuePair<string, object> entry in values)
        {
            table[entry.Key] = entry.Value;
        }

        return table;
    }

    /// <summary>
    /// Runs a call into the Photon SDK. The SDK reports problems through its callbacks; should it
    /// throw instead, the exception is logged and the call counts as failed, so it never reaches the game.
    /// </summary>
    private static bool Guard(Func<bool> call)
    {
        try
        {
            return call();
        }
#pragma warning disable CA1031 // A failure inside the Photon SDK must not end the game; it is logged and reported as a failed call.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            PhotonLog.Write("PHOTON sdk call failed: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }
}
