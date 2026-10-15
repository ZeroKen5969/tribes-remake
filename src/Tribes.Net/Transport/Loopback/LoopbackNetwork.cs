using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Tribes.Net.Transport.Loopback;

/// <summary>How a loopback connection behaves; set on the client transport before it connects.</summary>
public sealed record LoopbackLinkOptions
{
    /// <summary>One-way delay of every message.</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// Extra one-way delay, uniformly random between zero and this. Messages still arrive in
    /// order: a message never overtakes an earlier one.
    /// </summary>
    public TimeSpan Jitter { get; init; }

    /// <summary>Chance, per message sent, that the connection drops (0 to 1).</summary>
    public double ConnectionLossChance { get; init; }

    /// <summary>How long after a drop each side notices it.</summary>
    public TimeSpan LossDetectionDelay { get; init; }

    /// <summary>Interval of the pings that measure the round-trip time.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// An in-process network for tests: transports created on it reach each other by address, with
/// simulated latency, jitter and connection loss. Time comes from the given clock, so a
/// <see cref="ManualClock"/> makes every run repeat exactly. Single-threaded: poll all transports
/// from one thread.
/// </summary>
/// <remarks>
/// With host migration on (the default) it behaves like a relay service such as Photon: a host
/// and the clients connected to it form a room that outlives the host. When the host leaves or
/// crashes the clients see <see cref="DisconnectReason.HostLeft"/>, stay in the room, and one of
/// them can take over with <see cref="ITransport.TakeOverHost"/>. With it off, the network
/// behaves like the LAN: a host that leaves takes the room with it.
/// </remarks>
public sealed class LoopbackNetwork
{
    private readonly Dictionary<string, LoopbackTransport> hosts = new(StringComparer.Ordinal);
    private readonly List<LoopbackTransport> transports = [];
    private readonly Random random;
    private int nextConnectionId;

    /// <summary>Creates a network.</summary>
    /// <param name="clock">Time source for delays and pings.</param>
    /// <param name="seed">Seed of the jitter and loss draws.</param>
    /// <param name="supportsHostMigration">Whether rooms outlive their host (see the remarks).</param>
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Simulated jitter for tests; it must repeat for a given seed.")]
    public LoopbackNetwork(IClock clock, int seed = 1, bool supportsHostMigration = true)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
        random = new Random(seed);
        SupportsHostMigration = supportsHostMigration;
    }

    /// <summary>The time source.</summary>
    public IClock Clock { get; }

    /// <summary>Whether rooms outlive their host, so the host role can move to another peer.</summary>
    public bool SupportsHostMigration { get; }

    internal void Add(LoopbackTransport transport) => transports.Add(transport);

    /// <summary>Whether a live transport with this address is a member of <paramref name="room"/>.</summary>
    internal bool IsPresent(string address, LoopbackRoom? room) =>
        room is not null
        && transports.Exists(t => !t.IsCrashed && ReferenceEquals(t.Room, room) && room.Members.Contains(t)
            && string.Equals(t.Address, address, StringComparison.Ordinal));

    /// <summary>The rooms hosted on this network right now.</summary>
    public IReadOnlyList<RoomInfo> ListRooms()
    {
        var rooms = new List<RoomInfo>();
        foreach ((string address, LoopbackTransport host) in hosts)
        {
            if (host.Advertisement is { } advertisement)
            {
                rooms.Add(new RoomInfo { Address = address, Room = advertisement });
            }
        }

        rooms.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
        return rooms;
    }

    internal int NextConnectionId() => ++nextConnectionId;

    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Simulated jitter for tests; it must repeat for a given seed.")]
    internal TimeSpan NextJitter(TimeSpan maximum) =>
        maximum <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks((long)(random.NextDouble() * maximum.Ticks));

    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "Simulated connection loss for tests; it must repeat for a given seed.")]
    internal bool RollConnectionLoss(LoopbackLinkOptions link) =>
        link.ConnectionLossChance > 0 && random.NextDouble() < link.ConnectionLossChance;

    internal void Register(string address, LoopbackTransport host)
    {
        if (!hosts.TryAdd(address, host))
        {
            throw new InvalidOperationException($"Another transport already hosts at '{address}'.");
        }
    }

    internal void Unregister(string address, LoopbackTransport host)
    {
        if (hosts.TryGetValue(address, out LoopbackTransport? current) && ReferenceEquals(current, host))
        {
            hosts.Remove(address);
        }
    }

    internal LoopbackTransport? FindHost(string address) =>
        hosts.TryGetValue(address, out LoopbackTransport? host) ? host : null;

    /// <summary>
    /// Drops a connection: both sides notice after the link's detection delay. With
    /// <paramref name="hostGone"/> the far side learns that the host left the room.
    /// </summary>
    internal void Sever(LoopbackEndpoint endpoint, bool hostGone = false)
    {
        TimeSpan at = Clock.Now + endpoint.Link.LossDetectionDelay;
        endpoint.MarkLost(at, DisconnectReason.ConnectionLost);
        endpoint.Peer?.MarkLost(at, hostGone ? DisconnectReason.HostLeft : DisconnectReason.ConnectionLost);
    }
}

/// <summary>A host and the peers that joined it; with host migration it outlives the host.</summary>
internal sealed class LoopbackRoom
{
    public LoopbackRoom(LoopbackTransport host)
    {
        Host = host;
        Members.Add(host);
    }

    public LoopbackTransport? Host { get; set; }

    public HashSet<LoopbackTransport> Members { get; } = [];

    public void Leave(LoopbackTransport member)
    {
        Members.Remove(member);
        if (ReferenceEquals(Host, member))
        {
            Host = null;
        }
    }
}

/// <summary>Lists the rooms of a <see cref="LoopbackNetwork"/>.</summary>
public sealed class LoopbackRoomBrowser : IRoomBrowser
{
    private readonly LoopbackNetwork network;
    private IReadOnlyList<RoomInfo> rooms = [];
    private bool running;

    /// <summary>Creates a browser.</summary>
    /// <param name="network">The network to look at.</param>
    public LoopbackRoomBrowser(LoopbackNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);
        this.network = network;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoomInfo> Rooms => rooms;

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <inheritdoc />
    public void Start() => running = true;

    /// <inheritdoc />
    public void Refresh()
    {
        // The network is read directly on every poll; there is nothing to ask for.
    }

    /// <inheritdoc />
    public void Poll()
    {
        if (!running)
        {
            return;
        }

        IReadOnlyList<RoomInfo> current = network.ListRooms();
        if (!SameRooms(current, rooms))
        {
            rooms = current;
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        running = false;
        if (rooms.Count > 0)
        {
            rooms = [];
            RoomsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Shutdown();

    private static bool SameRooms(IReadOnlyList<RoomInfo> a, IReadOnlyList<RoomInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
