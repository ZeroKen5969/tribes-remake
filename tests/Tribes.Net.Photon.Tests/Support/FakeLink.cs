using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net.Photon.Link;

namespace Tribes.Net.Photon.Tests.Support;

/// <summary>
/// One client connection to the <see cref="FakePhotonCloud"/>. Operations reach the server after
/// <see cref="Latency"/>, in order; what the server sends arrives after <see cref="Latency"/>, in
/// order, and is applied to this client's view of the room when <see cref="Service"/> runs.
/// </summary>
internal sealed class FakeLink : IPhotonLink
{
    private readonly FakePhotonCloud cloud;
    private readonly Queue<(TimeSpan At, Action Apply)> inbox = new();
    private readonly HashSet<int> actors = [];
    private readonly Dictionary<string, object> properties = new(StringComparer.Ordinal);
    private readonly Queue<TimeSpan> acks = new();
    private TimeSpan lastArrival;
    private TimeSpan lastServerHeard;
    private TimeSpan transmittedUntil;
    private long queuedBytes;
    private TimeSpan lastSend;
    private bool severed;

    public FakeLink(FakePhotonCloud cloud)
    {
        this.cloud = cloud;
    }

    public ILinkListener? Listener { get; set; }

    public TimeSpan Latency { get; set; }

    /// <summary>How fast the server can send to this client, in bytes per second; zero for no limit.</summary>
    public double DownlinkBytesPerSecond { get; set; }

    /// <summary>The most bytes of events the server held for this client at once.</summary>
    public long PeakQueuedBytes { get; private set; }

    public LinkState State { get; private set; }

    public string? Region { get; private set; }

    public TimeSpan RoundTripTime => State == LinkState.Disconnected ? TimeSpan.Zero : Latency * 2;

    public TimeSpan TimeSinceServerHeard => State == LinkState.Disconnected ? TimeSpan.MaxValue : cloud.Clock.Now - lastServerHeard;

    public string? RoomName { get; private set; }

    public int LocalActor { get; private set; } = -1;

    public int MasterActor { get; private set; } = -1;

    public IReadOnlyCollection<int> Actors => State == LinkState.Room ? [.. actors] : [];

    public IReadOnlyList<(string Code, int PingMs)> Regions { get; private set; } = [];

    /// <summary>What the transport connected with.</summary>
    public LinkConnectOptions? Options { get; private set; }

    /// <summary>Set to make this client's network fail before it connects.</summary>
    public bool NetworkDown { get; set; }

    public bool Disposed { get; private set; }

    public bool Crashed { get; private set; }

    /// <summary>Operations of this link the server got (for checks on what was sent).</summary>
    public List<string> Operations { get; } = [];

    internal bool ServerOnline { get; set; }

    internal string? ServerRegion { get; set; }

    internal bool ServerInLobby { get; set; }

    internal FakeRoom? ServerRoom { get; set; }

    internal int ServerActor { get; set; } = -1;

    public object? RoomProperty(string key) => State == LinkState.Room ? properties.GetValueOrDefault(key) : null;

    public bool Connect(LinkConnectOptions options)
    {
        if (Disposed || State != LinkState.Disconnected)
        {
            return false;
        }

        Options = options;
        State = LinkState.Connecting;
        Send("connect", () => cloud.Connect(this, options));
        return true;
    }

    public void Disconnect()
    {
        if (State == LinkState.Disconnected)
        {
            return;
        }

        LeaveView();
        State = LinkState.Disconnected;
        if (!severed && !Crashed)
        {
            Send("disconnect", () => cloud.Remove(this));
        }
    }

    public bool JoinLobby() => Op(LinkState.Master, "join lobby", () => cloud.JoinLobby(this));

    public bool CreateRoom(string name, LinkRoomOptions options)
    {
        if (!Op(LinkState.Master, "create " + name, () => cloud.CreateRoom(this, name, options), alsoInLobby: true))
        {
            return false;
        }

        State = LinkState.Joining;
        return true;
    }

    public bool JoinRoom(string name)
    {
        if (!Op(LinkState.Master, "join " + name, () => cloud.JoinRoom(this, name), alsoInLobby: true))
        {
            return false;
        }

        State = LinkState.Joining;
        return true;
    }

    public bool RaiseEvent(byte code, byte[] content, IReadOnlyList<int> targets)
    {
        if (State != LinkState.Room)
        {
            return false;
        }

        byte[] copy = (byte[])content.Clone();
        int[] to = [.. targets];
        Send("event " + code, () => cloud.RaiseEvent(this, code, copy, to));
        return true;
    }

    public bool SetRoomProperties(IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, object>? expected)
    {
        if (State != LinkState.Room)
        {
            return false;
        }

        var set = new Dictionary<string, object>(values, StringComparer.Ordinal);
        Dictionary<string, object>? check = expected is null ? null : new(expected, StringComparer.Ordinal);
        Send("set " + string.Join(",", set.Keys) + (check is null ? string.Empty : " if " + string.Join(",", check.Select(e => $"{e.Key}={e.Value}"))),
            () => cloud.SetProperties(this, set, check));
        return true;
    }

    public bool SetMasterClient(int actor)
    {
        if (State != LinkState.Room)
        {
            return false;
        }

        Send("master " + actor, () => cloud.SetMaster(this, actor));
        return true;
    }

    public void Service()
    {
        if (Disposed || Crashed)
        {
            return;
        }

        cloud.Advance();
        while (acks.Count > 0 && acks.Peek() <= cloud.Clock.Now)
        {
            lastServerHeard = acks.Dequeue();
        }

        while (inbox.Count > 0 && inbox.Peek().At <= cloud.Clock.Now)
        {
            (TimeSpan at, Action apply) = inbox.Dequeue();
            lastServerHeard = at > lastServerHeard ? at : lastServerHeard;
            apply();
        }
    }

    public bool SendOutgoing() => false;

    public void Dispose()
    {
        Disposed = true;
        Listener = null;
    }

    /// <summary>The device stops at once: nothing is sent or received any more; the server removes it after its timeout.</summary>
    public void Crash()
    {
        if (Crashed)
        {
            return;
        }

        Crashed = true;
        inbox.Clear();
        if (ServerOnline)
        {
            cloud.Schedule(cloud.Clock.Now + cloud.ServerTimeout, () => cloud.Remove(this));
        }
    }

    /// <summary>
    /// The network of this device fails: nothing more gets through; the client notices after the
    /// cloud's client timeout and the server removes it after its server timeout.
    /// </summary>
    public void LoseNetwork()
    {
        if (severed || State == LinkState.Disconnected)
        {
            return;
        }

        severed = true;
        inbox.Clear();
        acks.Clear();
        if (ServerOnline)
        {
            cloud.Schedule(cloud.Clock.Now + cloud.ServerTimeout, () => cloud.Remove(this));
        }

        inbox.Enqueue((cloud.Clock.Now + cloud.ClientTimeout, () =>
        {
            LeaveView();
            State = LinkState.Disconnected;
            Listener?.OnDisconnected(LinkFailure.Timeout);
        }));
    }

    /// <summary>
    /// Queues an event of <paramref name="bytes"/> for this client: it waits in the server's queue
    /// for the downlink; a queue over the cloud's limit makes the server drop this client.
    /// </summary>
    internal void DeliverData(int bytes, Action apply)
    {
        if (severed || Crashed)
        {
            return;
        }

        int limit = cloud.ServerSendBufferLimit;
        if (limit > 0 && queuedBytes + bytes > limit)
        {
            DropForFullBuffer();
            return;
        }

        queuedBytes += bytes;
        PeakQueuedBytes = Math.Max(PeakQueuedBytes, queuedBytes);
        TimeSpan now = cloud.Clock.Now;
        TimeSpan start = transmittedUntil > now ? transmittedUntil : now;
        transmittedUntil = start + (DownlinkBytesPerSecond > 0 ? TimeSpan.FromSeconds(bytes / DownlinkBytesPerSecond) : TimeSpan.Zero);
        TimeSpan at = transmittedUntil + Latency;
        if (at < lastArrival)
        {
            at = lastArrival;
        }

        lastArrival = at;
        inbox.Enqueue((at, () =>
        {
            queuedBytes -= bytes;
            apply();
        }));
    }

    /// <summary>Queues something the server sent this client.</summary>
    internal void Deliver(Action apply, TimeSpan extraDelay = default)
    {
        if (severed || Crashed)
        {
            return;
        }

        TimeSpan at = cloud.Clock.Now + Latency + extraDelay;
        if (at < lastArrival)
        {
            at = lastArrival;
        }

        lastArrival = at;
        inbox.Enqueue((at, apply));
    }

    internal void ClientOnMaster(string region)
    {
        State = LinkState.Master;
        Region = region;
        Regions = [.. cloud.Regions.Order(StringComparer.Ordinal).Select(r => (r, r == cloud.BestRegion ? 30 : 120))];
        Listener?.OnConnectedToMaster();
    }

    internal void ClientDisconnected(LinkFailure cause)
    {
        LeaveView();
        State = LinkState.Disconnected;
        Listener?.OnDisconnected(cause);
    }

    internal void ClientJoinedLobby()
    {
        State = LinkState.Lobby;
        Listener?.OnJoinedLobby();
    }

    internal void ClientRoomList(IReadOnlyList<LinkRoom> rooms) => Listener?.OnRoomList(rooms);

    internal void ClientEnterFailed(LinkFailure cause)
    {
        State = LinkState.Master;
        Listener?.OnEnterRoomFailed(cause);
    }

    internal void ClientJoinedRoom(FakeRoomView view)
    {
        State = LinkState.Room;
        RoomName = view.Name;
        LocalActor = view.LocalActor;
        MasterActor = view.Master;
        actors.Clear();
        actors.UnionWith(view.Actors);
        properties.Clear();
        foreach (KeyValuePair<string, object> property in view.Properties)
        {
            properties[property.Key] = property.Value;
        }

        Listener?.OnJoinedRoom();
    }

    internal void ClientActorEntered(int actor)
    {
        actors.Add(actor);
        Listener?.OnActorEntered(actor);
    }

    internal void ClientActorLeft(int actor)
    {
        actors.Remove(actor);
        Listener?.OnActorLeft(actor);
    }

    internal void ClientPropertiesChanged(IReadOnlyDictionary<string, object> changed)
    {
        foreach (KeyValuePair<string, object> property in changed)
        {
            properties[property.Key] = property.Value;
        }

        Listener?.OnRoomPropertiesChanged([.. changed.Keys]);
    }

    internal void ClientSetPropertiesFailed() => Listener?.OnSetPropertiesFailed();

    internal void ClientMasterChanged(int actor)
    {
        MasterActor = actor;
        Listener?.OnMasterChanged(actor);
    }

    internal void ClientEvent(int sender, byte code, byte[] content) => Listener?.OnEvent(sender, code, content);

    private void DropForFullBuffer()
    {
        severed = true;
        inbox.Clear();
        queuedBytes = 0;
        cloud.Remove(this);
        inbox.Enqueue((cloud.Clock.Now + Latency, () =>
        {
            LeaveView();
            State = LinkState.Disconnected;
            Listener?.OnDisconnected(LinkFailure.ServerError);
        }));
    }

    private bool Op(LinkState required, string what, Action action, bool alsoInLobby = false)
    {
        if (State != required && !(alsoInLobby && State == LinkState.Lobby))
        {
            return false;
        }

        Send(what, action);
        return true;
    }

    private void Send(string what, Action action)
    {
        if (severed || Crashed || Disposed)
        {
            return;
        }

        TimeSpan at = cloud.Clock.Now + Latency;
        if (at < lastSend)
        {
            at = lastSend;
        }

        lastSend = at;
        Operations.Add(what);
        cloud.Schedule(at, action);

        // Photon's server acknowledges every reliable command: the client hears from it a round trip later.
        acks.Enqueue(at + Latency);
    }

    private void LeaveView()
    {
        RoomName = null;
        LocalActor = -1;
        MasterActor = -1;
        actors.Clear();
        properties.Clear();
    }
}
