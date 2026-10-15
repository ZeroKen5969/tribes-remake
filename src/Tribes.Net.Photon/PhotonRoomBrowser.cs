using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>
/// Lists the rooms of one Photon region: it joins Photon's lobby, which sends the room list and
/// every change to it. Each room's advertisement travels in its lobby-listed room property, so the
/// list shows the same things as the LAN list (players, mode, versions, who holds a slot).
/// </summary>
/// <remarks>
/// Events are raised from <see cref="Poll"/>. After a lost connection the browser connects again
/// by itself every few seconds; after a refusal that will not go away by waiting (unknown AppId,
/// region not available) only <see cref="Refresh"/> tries again.
/// </remarks>
public sealed class PhotonRoomBrowser : IRoomBrowser
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly PhotonSettings settings;
    private readonly Func<IPhotonLink> createLink;
    private readonly IClock clock;
    private readonly Dictionary<string, RoomInfo> rooms = new(StringComparer.Ordinal);
    private IReadOnlyList<RoomInfo> listed = [];
    private IPhotonLink? link;
    private IPhotonLink? retired;
    private bool started;
    private bool reachedServer;
    private bool roomsDirty;
    private bool statusDirty;
    private bool disposed;
    private TimeSpan retryAt = TimeSpan.MaxValue;

    /// <summary>Creates a browser for the region in <paramref name="settings"/> (the best one when none is set).</summary>
    /// <exception cref="ArgumentException">The settings are not usable.</exception>
    public PhotonRoomBrowser(PhotonSettings settings)
        : this(settings, () => new RealtimeLink(), SystemClock.Instance)
    {
    }

    internal PhotonRoomBrowser(PhotonSettings settings, Func<IPhotonLink> createLink, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(createLink);
        ArgumentNullException.ThrowIfNull(clock);
        if (settings.FindProblem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(settings));
        }

        this.settings = settings;
        this.createLink = createLink;
        this.clock = clock;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoomInfo> Rooms => listed;

    /// <summary>How far connecting got.</summary>
    public PhotonStatus Status { get; private set; }

    /// <summary>Why the last attempt failed; <see cref="PhotonError.None"/> while all is well.</summary>
    public PhotonError LastError { get; private set; }

    /// <summary>The region listed (the best one when none was chosen), once connected.</summary>
    public string? Region => link?.Region;

    /// <summary>The regions Photon offered when the best region was looked for, with their pings in milliseconds.</summary>
    public IReadOnlyList<(string Code, int PingMs)> RegionPings { get; private set; } = [];

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <summary><see cref="Status"/>, <see cref="LastError"/> or <see cref="Region"/> changed; raised from <see cref="Poll"/>.</summary>
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        started = true;
        Connect();
    }

    /// <inheritdoc />
    /// <remarks>Photon sends room changes by itself; this only connects again when the browser is not connected.</remarks>
    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!started)
        {
            Start();
        }
        else if (link is null)
        {
            Connect();
        }
    }

    /// <inheritdoc />
    public void Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        link?.Service();
        if (retired is { } old)
        {
            retired = null;
            old.Dispose();
        }

        if (started && link is null && clock.Now >= retryAt)
        {
            Connect();
        }

        if (roomsDirty)
        {
            roomsDirty = false;
            listed = [.. rooms.Values.OrderBy(r => r.Room.Name, StringComparer.CurrentCulture).ThenBy(r => r.Address, StringComparer.Ordinal)];
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (statusDirty)
        {
            statusDirty = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        started = false;
        retryAt = TimeSpan.MaxValue;
        DropLink();
        ClearRooms();
        SetStatus(PhotonStatus.Offline, PhotonError.None);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Shutdown();
        retired?.Dispose();
        retired = null;
        disposed = true;
    }

    private void Connect()
    {
        DropLink();
        retryAt = TimeSpan.MaxValue;
        reachedServer = false;
        IPhotonLink created = createLink();
        created.Listener = new Callbacks(this, created);
        link = created;
        SetStatus(PhotonStatus.Connecting, PhotonError.None);
        if (!created.Connect(new LinkConnectOptions(settings.AppId, settings.AppVersion, settings.Region, settings.UseWebSockets, settings.AllowWebSocketFallback)))
        {
            Lost(LinkFailure.ServerError);
        }
    }

    private void DropLink()
    {
        if (link is not { } current)
        {
            return;
        }

        link = null;
        current.Listener = null;
        current.Disconnect();
        current.Dispose();
    }

    private void ClearRooms()
    {
        if (rooms.Count > 0 || listed.Count > 0)
        {
            rooms.Clear();
            roomsDirty = true;
        }
    }

    private void SetStatus(PhotonStatus status, PhotonError error)
    {
        if (Status != status || LastError != error)
        {
            Status = status;
            LastError = error;
            statusDirty = true;
        }
    }

    private void OnConnectedToMaster()
    {
        reachedServer = true;
        if (link is { } current)
        {
            RegionPings = current.Regions;
            if (!current.JoinLobby())
            {
                Lost(LinkFailure.ServerError);
            }
        }
    }

    private void OnRoomList(IReadOnlyList<LinkRoom> changes)
    {
        string? region = link?.Region;
        if (region is null)
        {
            return;
        }

        TimeSpan ping = link!.RoundTripTime;
        foreach (LinkRoom change in changes)
        {
            if (change.Removed || !PhotonAddress.IsCode(change.Name)
                || Wire.DecodeAdvertisement(change.Properties.GetValueOrDefault(Wire.AdvertisementProperty)) is not { } decoded)
            {
                roomsDirty |= rooms.Remove(change.Name);
                continue;
            }

            rooms[change.Name] = new RoomInfo
            {
                Address = PhotonAddress.Room(region, change.Name),
                Room = decoded.Room,
                ProtocolVersion = decoded.ProtocolVersion,
                Ping = ping,
            };
            roomsDirty = true;
        }
    }

    /// <summary>The link is gone: the rooms are forgotten and a retry is planned unless waiting cannot help.</summary>
    private void Lost(LinkFailure cause)
    {
        PhotonError error = cause switch
        {
            LinkFailure.Unreachable => reachedServer ? PhotonError.ConnectionLost : PhotonError.NoInternet,
            LinkFailure.Timeout => reachedServer ? PhotonError.ConnectionLost : PhotonError.Timeout,
            LinkFailure.InvalidAppId => PhotonError.InvalidAppId,
            LinkFailure.CcuLimit => PhotonError.ServerFull,
            LinkFailure.InvalidRegion => PhotonError.RegionUnavailable,
            LinkFailure.ByClient or LinkFailure.None => PhotonError.ConnectionLost,
            _ => PhotonError.ServerError,
        };
        if (link is { } current)
        {
            link = null;
            current.Listener = null;
            current.Disconnect();
            retired?.Dispose();
            retired = current;
        }

        ClearRooms();
        retryAt = started && error is not (PhotonError.InvalidAppId or PhotonError.RegionUnavailable) ? clock.Now + RetryDelay : TimeSpan.MaxValue;
        SetStatus(PhotonStatus.Failed, error);
    }

    private sealed class Callbacks(PhotonRoomBrowser owner, IPhotonLink source) : ILinkListener
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
                owner.Lost(cause);
            }
        }

        public void OnJoinedLobby()
        {
            if (Current)
            {
                owner.SetStatus(PhotonStatus.Connected, PhotonError.None);
            }
        }

        public void OnRoomList(IReadOnlyList<LinkRoom> rooms)
        {
            if (Current)
            {
                owner.OnRoomList(rooms);
            }
        }

        public void OnEnterRoomFailed(LinkFailure cause)
        {
            // The browser never enters rooms.
        }

        public void OnJoinedRoom()
        {
            // The browser never enters rooms.
        }

        public void OnActorEntered(int actor)
        {
            // The browser never enters rooms.
        }

        public void OnActorLeft(int actor)
        {
            // The browser never enters rooms.
        }

        public void OnRoomPropertiesChanged(IReadOnlyCollection<string> keys)
        {
            // The browser never enters rooms.
        }

        public void OnSetPropertiesFailed()
        {
            // The browser never enters rooms.
        }

        public void OnMasterChanged(int actor)
        {
            // The browser never enters rooms.
        }

        public void OnEvent(int sender, byte code, byte[]? content)
        {
            // The browser never enters rooms.
        }
    }
}
