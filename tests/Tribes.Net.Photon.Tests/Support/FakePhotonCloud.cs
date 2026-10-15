using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Link;

namespace Tribes.Net.Photon.Tests.Support;

/// <summary>A room on the <see cref="FakePhotonCloud"/>, as its server keeps it.</summary>
internal sealed class FakeRoom(string region, string name, int maxPlayers, IReadOnlyList<string> lobbyKeys)
{
    public string Region { get; } = region;

    public string Name { get; } = name;

    public int MaxPlayers { get; } = maxPlayers;

    public IReadOnlyList<string> LobbyKeys { get; } = lobbyKeys;

    public Dictionary<string, object> Properties { get; } = new(StringComparer.Ordinal);

    /// <summary>Actor number to member, in joining order.</summary>
    public SortedDictionary<int, FakeLink> Members { get; } = [];

    public int NextActor { get; set; } = 1;

    public int Master { get; set; }

    public bool IsOpen { get; set; } = true;

    public LinkRoom ToLobbyEntry(bool removed = false) => new(
        Name,
        removed,
        Members.Count,
        MaxPlayers,
        LobbyKeys.Where(Properties.ContainsKey).ToDictionary(k => k, k => Properties[k], StringComparer.Ordinal));
}

/// <summary>
/// An in-memory stand-in for the Photon Cloud with Photon's room semantics as the transport uses
/// them: regions, a lobby with room list updates, rooms with actor numbers and a master client,
/// custom room properties with compare-and-swap, targeted reliable ordered events, and actors that
/// leave cleanly, crash (removed after <see cref="ServerTimeout"/>) or lose their network. Every
/// message takes <see cref="Latency"/> each way (per link adjustable) on a manual clock, so runs
/// repeat exactly.
/// </summary>
internal sealed class FakePhotonCloud
{
    public const string AppId = "00000000-test-0000-0000-photoncloud0";

    private readonly PriorityQueue<Action, (TimeSpan At, long Order)> server = new();
    private readonly List<FakeLink> links = [];
    private long order;

    public FakePhotonCloud(ManualClock clock)
    {
        Clock = clock;
    }

    public ManualClock Clock { get; }

    public HashSet<string> Regions { get; } = ["asia", "eu", "jp", "us"];

    /// <summary>The region a client without a region preference ends up in.</summary>
    public string BestRegion { get; set; } = "jp";

    public int CcuLimit { get; set; } = 20;

    /// <summary>One-way delay of every message of new links.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>How long the server waits before it removes a client that stopped answering.</summary>
    public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a client whose network failed takes to notice.</summary>
    public TimeSpan ClientTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Most bytes of events the server holds for one receiver; beyond that it drops the receiver,
    /// as Photon does ("SendBufferFull"). Zero for no limit.
    /// </summary>
    public int ServerSendBufferLimit { get; set; } = 800 * 1024;

    /// <summary>When set, new links cannot reach the cloud at all (no internet).</summary>
    public bool Unreachable { get; set; }

    public Dictionary<(string Region, string Name), FakeRoom> Rooms { get; } = [];

    /// <summary>Clients connected now (each counts as one CCU).</summary>
    public int Connected => links.Count(l => l.ServerOnline);

    /// <summary>Every link made so far.</summary>
    public IReadOnlyList<FakeLink> Links => links;

    /// <summary>Events sent through the cloud so far (each targeted event once).</summary>
    public int EventsSent { get; set; }

    /// <summary>Makes a link (the factory handed to transports and browsers).</summary>
    public IPhotonLink CreateLink()
    {
        var created = new FakeLink(this) { Latency = Latency };
        links.Add(created);
        return created;
    }

    public FakeRoom? FindRoom(string region, string name) => Rooms.GetValueOrDefault((region, name));

    /// <summary>Runs <paramref name="action"/> on the server at <paramref name="at"/>, after everything scheduled earlier.</summary>
    internal void Schedule(TimeSpan at, Action action) => server.Enqueue(action, (at, order++));

    /// <summary>Runs the server work that is due.</summary>
    internal void Advance()
    {
        while (server.TryPeek(out _, out (TimeSpan At, long Order) when) && when.At <= Clock.Now)
        {
            server.Dequeue()();
        }
    }

    internal void Connect(FakeLink link, LinkConnectOptions options)
    {
        if (Unreachable || link.NetworkDown)
        {
            link.Deliver(() => link.ClientDisconnected(LinkFailure.Unreachable), extraDelay: TimeSpan.FromSeconds(1));
            return;
        }

        if (options.AppId != AppId)
        {
            link.Deliver(() => link.ClientDisconnected(LinkFailure.InvalidAppId));
            return;
        }

        string region = options.Region ?? BestRegion;
        if (!Regions.Contains(region))
        {
            link.Deliver(() => link.ClientDisconnected(LinkFailure.InvalidRegion));
            return;
        }

        if (Connected >= CcuLimit)
        {
            link.Deliver(() => link.ClientDisconnected(LinkFailure.CcuLimit));
            return;
        }

        link.ServerOnline = true;
        link.ServerRegion = region;
        link.Deliver(() => link.ClientOnMaster(region));
    }

    internal void JoinLobby(FakeLink link)
    {
        if (!link.ServerOnline || link.ServerRoom is not null)
        {
            return;
        }

        link.ServerInLobby = true;
        List<LinkRoom> all = [.. Rooms.Values.Where(r => r.Region == link.ServerRegion).Select(r => r.ToLobbyEntry())];
        link.Deliver(link.ClientJoinedLobby);
        link.Deliver(() => link.ClientRoomList(all));
    }

    internal void CreateRoom(FakeLink link, string name, LinkRoomOptions options)
    {
        if (!link.ServerOnline || link.ServerRoom is not null)
        {
            return;
        }

        if (Rooms.ContainsKey((link.ServerRegion!, name)))
        {
            link.Deliver(() => link.ClientEnterFailed(LinkFailure.RoomExists));
            return;
        }

        var room = new FakeRoom(link.ServerRegion!, name, options.MaxPlayers, options.LobbyProperties);
        foreach (KeyValuePair<string, object> property in options.Properties)
        {
            room.Properties[property.Key] = property.Value;
        }

        Rooms[(room.Region, name)] = room;
        Enter(link, room);
        BroadcastLobby(room, removed: false);
    }

    internal void JoinRoom(FakeLink link, string name)
    {
        if (!link.ServerOnline || link.ServerRoom is not null)
        {
            return;
        }

        if (FindRoom(link.ServerRegion!, name) is not { } room)
        {
            link.Deliver(() => link.ClientEnterFailed(LinkFailure.RoomNotFound));
            return;
        }

        if (!room.IsOpen)
        {
            link.Deliver(() => link.ClientEnterFailed(LinkFailure.RoomClosed));
            return;
        }

        if (room.Members.Count >= room.MaxPlayers)
        {
            link.Deliver(() => link.ClientEnterFailed(LinkFailure.RoomFull));
            return;
        }

        Enter(link, room);
        BroadcastLobby(room, removed: false);
    }

    internal void RaiseEvent(FakeLink sender, byte code, byte[] content, IReadOnlyList<int> targets)
    {
        if (sender.ServerRoom is not { } room)
        {
            return;
        }

        foreach (int target in targets.Distinct())
        {
            if (room.Members.TryGetValue(target, out FakeLink? receiver) && receiver != sender)
            {
                EventsSent++;
                byte[] copy = (byte[])content.Clone();
                int from = sender.ServerActor;
                receiver.DeliverData(copy.Length, () => receiver.ClientEvent(from, code, copy));
            }
        }
    }

    internal void SetProperties(FakeLink sender, IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, object>? expected)
    {
        if (sender.ServerRoom is not { } room)
        {
            return;
        }

        if (expected is not null && expected.Any(e => !room.Properties.TryGetValue(e.Key, out object? now) || !now.Equals(e.Value)))
        {
            sender.Deliver(sender.ClientSetPropertiesFailed);
            return;
        }

        foreach (KeyValuePair<string, object> value in values)
        {
            room.Properties[value.Key] = value.Value;
        }

        var changed = new Dictionary<string, object>(values, StringComparer.Ordinal);
        foreach (FakeLink member in room.Members.Values)
        {
            member.Deliver(() => member.ClientPropertiesChanged(changed));
        }

        if (values.Keys.Any(room.LobbyKeys.Contains))
        {
            BroadcastLobby(room, removed: false);
        }
    }

    internal void SetMaster(FakeLink sender, int actor)
    {
        if (sender.ServerRoom is not { } room || !room.Members.ContainsKey(actor) || room.Master == actor)
        {
            return;
        }

        SwitchMaster(room, actor);
    }

    /// <summary>The server removes <paramref name="link"/> (it disconnected, or stopped answering).</summary>
    internal void Remove(FakeLink link)
    {
        if (!link.ServerOnline)
        {
            return;
        }

        link.ServerOnline = false;
        link.ServerInLobby = false;
        if (link.ServerRoom is not { } room)
        {
            return;
        }

        int actor = link.ServerActor;
        link.ServerRoom = null;
        link.ServerActor = -1;
        room.Members.Remove(actor);
        if (room.Members.Count == 0)
        {
            Rooms.Remove((room.Region, room.Name));
            BroadcastLobby(room, removed: true);
            return;
        }

        foreach (FakeLink member in room.Members.Values)
        {
            member.Deliver(() => member.ClientActorLeft(actor));
        }

        if (room.Master == actor)
        {
            SwitchMaster(room, room.Members.Keys.Min());
        }

        BroadcastLobby(room, removed: false);
    }

    private void Enter(FakeLink link, FakeRoom room)
    {
        int actor = room.NextActor++;
        if (room.Members.Count == 0)
        {
            room.Master = actor;
        }

        foreach (FakeLink member in room.Members.Values)
        {
            member.Deliver(() => member.ClientActorEntered(actor));
        }

        room.Members[actor] = link;
        link.ServerRoom = room;
        link.ServerActor = actor;
        link.ServerInLobby = false;
        var snapshot = new FakeRoomView(room.Name, actor, room.Master, [.. room.Members.Keys], new Dictionary<string, object>(room.Properties, StringComparer.Ordinal));
        link.Deliver(() => link.ClientJoinedRoom(snapshot));
    }

    private static void SwitchMaster(FakeRoom room, int actor)
    {
        room.Master = actor;
        foreach (FakeLink member in room.Members.Values)
        {
            member.Deliver(() => member.ClientMasterChanged(actor));
        }
    }

    private void BroadcastLobby(FakeRoom room, bool removed)
    {
        LinkRoom entry = room.ToLobbyEntry(removed);
        foreach (FakeLink watcher in links.Where(l => l.ServerInLobby && l.ServerRegion == room.Region))
        {
            watcher.Deliver(() => watcher.ClientRoomList([entry]));
        }
    }
}

/// <summary>What a client is told about a room when it enters it.</summary>
internal sealed record FakeRoomView(string Name, int LocalActor, int Master, IReadOnlyList<int> Actors, Dictionary<string, object> Properties);
