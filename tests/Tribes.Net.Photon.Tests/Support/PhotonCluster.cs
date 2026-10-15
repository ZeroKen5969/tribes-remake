using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Link;
using Tribes.Net.Tests.Support;
using Xunit;
using Xunit.Sdk;

namespace Tribes.Net.Photon.Tests.Support;

/// <summary>One device of a <see cref="PhotonCluster"/>: its transport, the links it made, and what its session observed.</summary>
internal sealed class PhotonNode
{
    public required string Name { get; init; }

    /// <summary>The player's secret key.</summary>
    public required Guid Id { get; init; }

    public required PhotonTransport Transport { get; init; }

    public required List<FakeLink> Links { get; init; }

    /// <summary>The link the transport uses now (its latest).</summary>
    public FakeLink Link => Links[^1];

    /// <summary>Set when the device crashed: its sessions are not updated any more.</summary>
    public bool Frozen { get; set; }

    public ClientSession Session { get; set; } = null!;

    public Guid PublicId => Session.LocalPlayerId;

    public List<HashChainSimulation> Simulations { get; } = [];

    public HashChainSimulation Simulation => Simulations[^1];

    public List<SessionEndedEventArgs> Ends { get; } = [];

    public List<PlayerPresenceChangedEventArgs> Presence { get; } = [];

    public List<DesyncDetectedEventArgs> Desyncs { get; } = [];

    public List<MatchStartedEventArgs> MatchStarts { get; } = [];

    public List<ClientPhase> Phases { get; } = [];

    public List<Guid> HostChanges { get; } = [];

    public HostSession? Hosting { get; set; }

    public int CommandsSent { get; set; }

    /// <summary>The device crashes: no goodbye, the room drops it after the server timeout.</summary>
    public void Crash()
    {
        Frozen = true;
        Link.Crash();
    }
}

/// <summary>
/// A host and clients on a <see cref="FakePhotonCloud"/>, each with its own <see cref="PhotonTransport"/>,
/// driven by a manual clock: every run repeats exactly. Each frame advances the clock by 10 ms and
/// updates every session once.
/// </summary>
internal sealed class PhotonCluster : IDisposable
{
    public const string GameVersion = "1.2.0";
    public const string ContentVersion = "content-7";
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(10);

    private readonly Random random;
    private readonly int snapshotPadding;
    private readonly List<HostSession> hostsSeen = [];

    public PhotonCluster(RoomMode mode = RoomMode.Versus, int seed = 1, int hostLatencyMs = 20, int snapshotPadding = 0, Func<PhotonSettings, PhotonSettings>? settings = null)
    {
        random = new Random(seed);
        this.snapshotPadding = snapshotPadding;
        Cloud = new FakePhotonCloud(Clock);
        Settings = settings?.Invoke(BaseSettings) ?? BaseSettings;
        HostNode = CreateNode("host", Guid.NewGuid(), hostLatencyMs);
        HostOptions = new HostOptions
        {
            GameVersion = GameVersion,
            ContentVersion = ContentVersion,
            PlayerId = HostNode.Id,
            PlayerName = "Host",
            Room = new RoomSettings { Name = "Test room", Mode = mode },
        };
        Host = new HostSession(HostNode.Transport, HostOptions, CreateHostSimulation, Clock);
        hostsSeen.Add(Host);
        Hook(Host);
        RunUntil(() => Host.Address is not null, "the room to be created");
        RoomAddress = Host.Address!;
    }

    public static PhotonSettings BaseSettings { get; } = PhotonSettings.ForAppId(FakePhotonCloud.AppId);

    public ManualClock Clock { get; } = new();

    public FakePhotonCloud Cloud { get; }

    public PhotonSettings Settings { get; }

    public PhotonNode HostNode { get; private set; }

    public HostOptions HostOptions { get; }

    public HostSession Host { get; private set; }

    public List<HashChainSimulation> HostSimulations { get; } = [];

    public HashChainSimulation HostSimulation => (HashChainSimulation)Host.Simulation!;

    public List<PhotonNode> Clients { get; } = [];

    /// <summary>Where clients join: the room's address (the same after a host migration).</summary>
    public string RoomAddress { get; private set; } = string.Empty;

    public List<PlayerPresenceChangedEventArgs> HostPresence { get; } = [];

    public List<DesyncDetectedEventArgs> HostDesyncs { get; } = [];

    /// <summary>The device that hosts the match now.</summary>
    public PhotonNode CurrentHostNode => Clients.FirstOrDefault(c => c.Hosting == Host) ?? HostNode;

    public static void AssertInSync(int minimumOverlap, params HashChainSimulation[] simulations)
    {
        for (int i = 0; i < simulations.Length; i++)
        {
            for (int j = i + 1; j < simulations.Length; j++)
            {
                int overlap = 0;
                foreach ((long tick, uint hash) in simulations[i].HashByTick)
                {
                    if (simulations[j].HashByTick.TryGetValue(tick, out uint other))
                    {
                        Assert.True(hash == other, $"Simulations {i} and {j} differ at tick {tick}.");
                        overlap++;
                    }
                }

                Assert.True(overlap >= minimumOverlap, $"Simulations {i} and {j} share only {overlap} ticks.");
            }
        }
    }

    /// <summary>A transport on this cloud whose links get <paramref name="latencyMs"/>.</summary>
    public PhotonNode CreateNode(string name, Guid id, int latencyMs = 20, PhotonSettings? settings = null)
    {
        var links = new List<FakeLink>();
        IPhotonLink Factory()
        {
            var created = (FakeLink)Cloud.CreateLink();
            created.Latency = TimeSpan.FromMilliseconds(latencyMs);
            links.Add(created);
            return created;
        }

        var transport = new PhotonTransport(settings ?? Settings, Factory, Clock, d => Clock.Advance(d), seed: name.Length + id.GetHashCode());
        return new PhotonNode { Name = name, Id = id, Transport = transport, Links = links };
    }

    public PhotonNode AddClient(string name, int latencyMs = 20, Func<ClientOptions, ClientOptions>? configure = null, Guid? id = null, string? address = null)
    {
        PhotonNode node = CreateNode(name, id ?? Guid.NewGuid(), latencyMs);
        node.Session = CreateSession(node, configure);
        Clients.Add(node);
        node.Session.Connect(address ?? RoomAddress);
        return node;
    }

    /// <summary>
    /// The host opens <paramref name="save"/> again, on a new device of the same player: a new
    /// Photon room (new code) that waits paused for the players.
    /// </summary>
    public HostSession ResumeHost(byte[] save)
    {
        HostNode = CreateNode("host", HostNode.Id, 20);
        Host = HostSession.ResumeFromSave(HostNode.Transport, HostOptions, save, CreateHostSimulation, Clock);
        hostsSeen.Add(Host);
        Hook(Host);
        RunUntil(() => Host.Address is not null, "the resumed room to be created");
        RoomAddress = Host.Address!;
        return Host;
    }

    /// <summary>The same device and player join again with a new session.</summary>
    public void Rejoin(PhotonNode node, Func<ClientOptions, ClientOptions>? configure = null)
    {
        node.Session.Dispose();
        node.Session = CreateSession(node, configure);
        node.Session.Connect(RoomAddress);
    }

    /// <summary>A device that is not a client yet (for example the old host) joins the room as a player.</summary>
    public PhotonNode JoinAs(PhotonNode device, string address)
    {
        if (!Clients.Contains(device))
        {
            Clients.Add(device);
        }

        device.Frozen = false;
        device.Session = CreateSession(device, null);
        device.Session.Connect(address);
        return device;
    }

    public ILockstepSimulation CreateHostSimulation(MatchStart start)
    {
        var simulation = new HashChainSimulation(start, snapshotPadding);
        HostSimulations.Add(simulation);
        return simulation;
    }

    public void Step()
    {
        Clock.Advance(Frame);
        if (!CurrentHostNode.Frozen)
        {
            Host.Update();
        }

        foreach (PhotonNode client in Clients.ToArray())
        {
            if (!client.Frozen && client.Session.Phase != ClientPhase.Ended)
            {
                client.Session.Update();
            }
        }
    }

    public void Run(TimeSpan duration)
    {
        for (long frames = duration.Ticks / Frame.Ticks; frames > 0; frames--)
        {
            Step();
        }
    }

    /// <summary>Runs while every active participant sends a random command now and then.</summary>
    public void RunWithCommands(TimeSpan duration, double chancePerFrame = 0.15)
    {
        for (long frames = duration.Ticks / Frame.Ticks; frames > 0; frames--)
        {
            if (!CurrentHostNode.Frozen && Host.LocalPlayer.CanSendCommands && random.NextDouble() < chancePerFrame)
            {
                Assert.Equal(RequestResult.Accepted, Host.SendCommand(RandomPayload()));
            }

            foreach (PhotonNode client in Clients)
            {
                if (!client.Frozen && client.Session.CanSendCommands && random.NextDouble() < chancePerFrame)
                {
                    Assert.True(client.Session.SendCommand(RandomPayload()));
                    client.CommandsSent++;
                }
            }

            Step();
        }
    }

    public void RunUntil(Func<bool> condition, string waitingFor, TimeSpan? timeout = null)
    {
        TimeSpan end = Clock.Now + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (Clock.Now >= end)
            {
                string states = string.Join("; ", Clients.Select(c => $"{c.Name}: {string.Join(">", c.Phases)} {c.Session.EndReason} {c.Transport.Status}/{c.Transport.LastError}"));
                throw new XunitException($"Timed out waiting for {waitingFor}. Host: {Host.LocalPlayer.Name} {CurrentHostNode.Transport.Status}/{CurrentHostNode.Transport.LastError}. {states}");
            }

            Step();
        }
    }

    public void RunUntilJoined(params PhotonNode[] nodes) =>
        RunUntil(() => nodes.All(p => p.Session.Phase is ClientPhase.Lobby or ClientPhase.InGame), "the clients to join");

    /// <summary>Everyone with a slot says ready, then the host starts and every client runs the match.</summary>
    public void StartMatch(byte[]? setup = null)
    {
        PhotonNode[] members = [.. Clients.Where(c => c.Session.Phase != ClientPhase.Ended)];
        RunUntilJoined(members);
        foreach (PhotonNode client in members.Where(c => c.Session.LocalPlayer?.HasSlot == true))
        {
            Assert.Equal(RequestResult.Accepted, client.Session.SetReady(true));
        }

        RunUntil(() => Host.CanStart() == RequestResult.Accepted, "everyone to be ready");
        Assert.Equal(RequestResult.Accepted, Host.Start(setup));
        RunUntil(() => members.All(c => c.Session.Phase == ClientPhase.InGame), "every client to run the match");
    }

    public byte[] RandomPayload()
    {
        var payload = new byte[1 + random.Next(8)];
        random.NextBytes(payload);
        return payload;
    }

    public void Dispose()
    {
        foreach (PhotonNode client in Clients)
        {
            client.Session.Dispose();
        }

        foreach (HostSession host in hostsSeen)
        {
            host.Dispose();
        }
    }

    private ClientSession CreateSession(PhotonNode node, Func<ClientOptions, ClientOptions>? configure)
    {
        var options = new ClientOptions
        {
            GameVersion = GameVersion,
            ContentVersion = ContentVersion,
            PlayerId = node.Id,
            PlayerName = node.Name,
        };
        var session = new ClientSession(
            node.Transport,
            configure?.Invoke(options) ?? options,
            start =>
            {
                var simulation = new HashChainSimulation(start, snapshotPadding);
                node.Simulations.Add(simulation);
                return simulation;
            },
            Clock);
        session.Ended += (_, e) => node.Ends.Add(e);
        session.PlayerPresenceChanged += (_, e) => node.Presence.Add(e);
        session.DesyncDetected += (_, e) => node.Desyncs.Add(e);
        session.MatchStarted += (_, e) => node.MatchStarts.Add(e);
        session.PhaseChanged += (_, e) => node.Phases.Add(e.Phase);
        session.HostChanged += (_, e) => node.HostChanges.Add(e.HostId);
        session.BecameHost += (_, e) =>
        {
            node.Hosting = e.Host;
            Host = e.Host;
            hostsSeen.Add(e.Host);
            Hook(e.Host);
        };
        return session;
    }

    private void Hook(HostSession host)
    {
        host.PlayerPresenceChanged += (_, e) => HostPresence.Add(e);
        host.DesyncDetected += (_, e) => HostDesyncs.Add(e);
    }
}
