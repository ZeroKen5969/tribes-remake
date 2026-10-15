namespace Tribes.Net.Photon;

/// <summary>Where a <see cref="PhotonTransport"/> or <see cref="PhotonRoomBrowser"/> is.</summary>
public enum PhotonStatus
{
    /// <summary>Not connected and not trying.</summary>
    Offline,

    /// <summary>Connecting to Photon, creating or joining a room, or reaching the room's host.</summary>
    Connecting,

    /// <summary>Connected: in the lobby (browser) or in a room (transport).</summary>
    Connected,

    /// <summary>The last attempt failed or the connection was lost; <c>LastError</c> says why.</summary>
    Failed,
}

/// <summary>Why connecting to Photon or a room failed, or why the connection ended.</summary>
public enum PhotonError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>This build has no Photon AppId (it was built without the secrets file).</summary>
    NotConfigured,

    /// <summary>Photon's servers could not be reached: no internet, or the network blocks them.</summary>
    NoInternet,

    /// <summary>Connecting took too long.</summary>
    Timeout,

    /// <summary>Photon does not accept this build's AppId.</summary>
    InvalidAppId,

    /// <summary>The app's limit of players online at the same time is reached.</summary>
    ServerFull,

    /// <summary>The chosen region is not available to the app.</summary>
    RegionUnavailable,

    /// <summary>No room has this code (in this region).</summary>
    RoomNotFound,

    /// <summary>The room has no free place.</summary>
    RoomFull,

    /// <summary>The room does not let anybody in.</summary>
    RoomClosed,

    /// <summary>The room's host did not answer.</summary>
    HostNotAnswering,

    /// <summary>The connection to Photon was lost.</summary>
    ConnectionLost,

    /// <summary>Another player hosts the room now (this device lost its connection while hosting and the others moved on).</summary>
    HostReplaced,

    /// <summary>Photon refused the connection or the request for another reason.</summary>
    ServerError,
}
