using System;
using System.Collections.Generic;

namespace Tribes.Net.Photon.Link;

/// <summary>Where a <see cref="IPhotonLink"/> is in Photon's connection workflow.</summary>
internal enum LinkState
{
    /// <summary>Not connected (also after a failure).</summary>
    Disconnected,

    /// <summary>Connecting to the name server, picking a region and reaching its master server.</summary>
    Connecting,

    /// <summary>On the master server: can join the lobby, create or join a room.</summary>
    Master,

    /// <summary>In the lobby on the master server, receiving the room list.</summary>
    Lobby,

    /// <summary>Creating or joining a room.</summary>
    Joining,

    /// <summary>In a room on its game server.</summary>
    Room,
}

/// <summary>Why a link failed or an operation was refused.</summary>
internal enum LinkFailure
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>This side disconnected on purpose.</summary>
    ByClient,

    /// <summary>The servers could not be reached at all (no network, DNS, blocked ports).</summary>
    Unreachable,

    /// <summary>The connection stopped answering.</summary>
    Timeout,

    /// <summary>The server does not know the AppId.</summary>
    InvalidAppId,

    /// <summary>The app's concurrent user limit is reached.</summary>
    CcuLimit,

    /// <summary>The region does not exist or is not enabled for the app.</summary>
    InvalidRegion,

    /// <summary>The room does not exist.</summary>
    RoomNotFound,

    /// <summary>The room has no free place.</summary>
    RoomFull,

    /// <summary>The room does not let anybody in.</summary>
    RoomClosed,

    /// <summary>A room with that name exists already.</summary>
    RoomExists,

    /// <summary>The server refused or ended the connection for another reason.</summary>
    ServerError,
}

/// <summary>What a link connects with.</summary>
/// <param name="AppId">The Photon Realtime AppId.</param>
/// <param name="AppVersion">Clients only see rooms of the same app version.</param>
/// <param name="Region">The region code, or null for the best region by ping.</param>
/// <param name="UseWebSockets">Connect with secure WebSockets (port 443) instead of UDP.</param>
/// <param name="AllowWebSocketFallback">Switch to secure WebSockets once when UDP cannot connect.</param>
internal sealed record LinkConnectOptions(string AppId, string AppVersion, string? Region, bool UseWebSockets, bool AllowWebSocketFallback)
{
    /// <summary>Leaves the AppId out, so the options can be logged.</summary>
    public override string ToString() =>
        $"LinkConnectOptions {{ AppVersion = {AppVersion}, Region = {Region ?? "best"}, UseWebSockets = {UseWebSockets}, AllowWebSocketFallback = {AllowWebSocketFallback} }}";
}

/// <summary>How a room is created.</summary>
/// <param name="MaxPlayers">Most actors in the room at once.</param>
/// <param name="Properties">Its first custom properties.</param>
/// <param name="LobbyProperties">The keys of <paramref name="Properties"/> the lobby lists.</param>
internal sealed record LinkRoomOptions(int MaxPlayers, IReadOnlyDictionary<string, object> Properties, IReadOnlyList<string> LobbyProperties);

/// <summary>A room as the lobby lists it (Photon sends changes only).</summary>
/// <param name="Name">The room name.</param>
/// <param name="Removed">Whether the room left the list.</param>
/// <param name="PlayerCount">Actors in the room.</param>
/// <param name="MaxPlayers">Most actors the room takes.</param>
/// <param name="Properties">Its lobby-listed custom properties.</param>
internal sealed record LinkRoom(string Name, bool Removed, int PlayerCount, int MaxPlayers, IReadOnlyDictionary<string, object> Properties);

/// <summary>Callbacks of a <see cref="IPhotonLink"/>, all raised from <see cref="IPhotonLink.Service"/>.</summary>
internal interface ILinkListener
{
    /// <summary>The link reached the master server of its region.</summary>
    void OnConnectedToMaster();

    /// <summary>The link is disconnected (on purpose or not).</summary>
    void OnDisconnected(LinkFailure cause);

    /// <summary>The link joined the lobby; the full room list follows.</summary>
    void OnJoinedLobby();

    /// <summary>Rooms in the lobby were added, changed or removed.</summary>
    void OnRoomList(IReadOnlyList<LinkRoom> rooms);

    /// <summary>Creating or joining a room failed; the link is back on the master server.</summary>
    void OnEnterRoomFailed(LinkFailure cause);

    /// <summary>The link is in a room now; its actors and properties are known.</summary>
    void OnJoinedRoom();

    /// <summary>Another actor entered the room.</summary>
    void OnActorEntered(int actor);

    /// <summary>Another actor left the room (or dropped out).</summary>
    void OnActorLeft(int actor);

    /// <summary>Room properties changed (by anyone, this link included).</summary>
    void OnRoomPropertiesChanged(IReadOnlyCollection<string> keys);

    /// <summary>A property change of this link was refused (its expected values did not match).</summary>
    void OnSetPropertiesFailed();

    /// <summary>Another actor became the room's master client.</summary>
    void OnMasterChanged(int actor);

    /// <summary>An event from another actor arrived.</summary>
    void OnEvent(int sender, byte code, byte[]? content);
}

/// <summary>
/// The few Photon Realtime calls the transport and the room browser need: one client connection
/// to Photon. The game uses <see cref="RealtimeLink"/>; tests use an in-memory cloud. Everything
/// happens on the caller's thread; callbacks are raised from <see cref="Service"/>.
/// </summary>
internal interface IPhotonLink : IDisposable
{
    /// <summary>Receives the callbacks.</summary>
    ILinkListener? Listener { get; set; }

    /// <summary>Where the link is.</summary>
    LinkState State { get; }

    /// <summary>The region it connected to, once known.</summary>
    string? Region { get; }

    /// <summary>The round trip to the server, zero until measured.</summary>
    TimeSpan RoundTripTime { get; }

    /// <summary>
    /// How long ago anything arrived from the server (data or acknowledgements); very long while
    /// disconnected. A client whose host went quiet uses it to tell "the host is gone" from "my own
    /// connection is gone".
    /// </summary>
    TimeSpan TimeSinceServerHeard { get; }

    /// <summary>The room it is in, or null.</summary>
    string? RoomName { get; }

    /// <summary>This link's actor number in its room, or -1.</summary>
    int LocalActor { get; }

    /// <summary>The room's master client actor, or -1.</summary>
    int MasterActor { get; }

    /// <summary>The actors in the room now (this link included), as far as this link knows.</summary>
    IReadOnlyCollection<int> Actors { get; }

    /// <summary>The regions the name server offered in a best-region connect, with their pings in milliseconds; empty otherwise.</summary>
    IReadOnlyList<(string Code, int PingMs)> Regions { get; }

    /// <summary>A custom property of the room, or null.</summary>
    object? RoomProperty(string key);

    /// <summary>Starts connecting; false when the link cannot start now.</summary>
    bool Connect(LinkConnectOptions options);

    /// <summary>Disconnects (leaving any room): queued commands are sent first as far as possible.</summary>
    void Disconnect();

    /// <summary>Joins the lobby to receive the room list.</summary>
    bool JoinLobby();

    /// <summary>Creates a room and joins it.</summary>
    bool CreateRoom(string name, LinkRoomOptions options);

    /// <summary>Joins a room.</summary>
    bool JoinRoom(string name);

    /// <summary>Sends <paramref name="content"/> reliably, in order, to the given actors of the room.</summary>
    bool RaiseEvent(byte code, byte[] content, IReadOnlyList<int> targets);

    /// <summary>
    /// Changes room properties. With <paramref name="expected"/>, the server changes them only if
    /// its current values match (compare and swap); otherwise <see cref="ILinkListener.OnSetPropertiesFailed"/>.
    /// </summary>
    bool SetRoomProperties(IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, object>? expected);

    /// <summary>Asks the server to make <paramref name="actor"/> the master client.</summary>
    bool SetMasterClient(int actor);

    /// <summary>Receives and sends: raises the callbacks of what arrived. Call it every frame.</summary>
    void Service();

    /// <summary>Sends what is queued now; true while more is waiting.</summary>
    bool SendOutgoing();
}
