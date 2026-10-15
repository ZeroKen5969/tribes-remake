using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Xunit;
using Xunit.Sdk;

namespace Tribes.Net.Tests.Support;

/// <summary>A client of a <see cref="LoopbackCluster"/> and what it observed.</summary>
internal sealed class TestPeer
{
    /// <summary>The player's secret key (<see cref="ClientOptions.PlayerId"/>).</summary>
    public required Guid Id { get; init; }

    /// <summary>The id the room shows for this player.</summary>
    public Guid PublicId => Session.LocalPlayerId;

    public required string Name { get; init; }

    public required LoopbackTransport Transport { get; init; }

    public ClientSession Session { get; set; } = null!;

    public List<HashChainSimulation> Simulations { get; } = [];

    public HashChainSimulation Simulation => Simulations[^1];

    public List<SessionEndedEventArgs> Ends { get; } = [];

    public List<RequestDeniedEventArgs> Denials { get; } = [];

    public List<PlayerPresenceChangedEventArgs> Presence { get; } = [];

    public List<DesyncDetectedEventArgs> Desyncs { get; } = [];

    public List<MatchStartedEventArgs> MatchStarts { get; } = [];

    public List<ChatReceivedEventArgs> Chats { get; } = [];

    public List<ClientPhase> Phases { get; } = [];

    public List<Guid> HostChanges { get; } = [];

    /// <summary>The host session this peer turned into when it took over a match, or null.</summary>
    public HostSession? Hosting { get; set; }

    /// <summary>Commands this peer's client session sent (its command numbers run 1 to this).</summary>
    public int CommandsSent { get; set; }
}

/// <summary>
/// A host and clients on a loopback network driven by a manual clock: every run repeats exactly.
/// Each frame advances the clock by 10 ms and updates every session once.
/// </summary>
internal sealed class LoopbackCluster : IDisposable
{
    public const string GameVersion = "1.2.0";
    public const string ContentVersion = "content-7";
    public const string HostAddress = "host";
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(10);

    private readonly Random random;
    private readonly int snapshotPadding;

    public LoopbackCluster(RoomMode mode = RoomMode.Versus, Func<HostOptions, HostOptions>? configure = null, int snapshotPadding = 0, int seed = 1, bool hostMigration = true)
    {
        random = new Random(seed);
        this.snapshotPadding = snapshotPadding;
        Network = new LoopbackNetwork(Clock, seed, hostMigration);
        HostTransport = new LoopbackTransport(Network, HostAddress);
        var options = new HostOptions
        {
            GameVersion = GameVersion,
            ContentVersion = ContentVersion,
            PlayerId = Guid.NewGuid(),
            PlayerName = "Host",
            Room = new RoomSettings { Name = "Test room", Mode = mode },
        };
        HostOptions = configure?.Invoke(options) ?? options;
        Host = new HostSession(HostTransport, HostOptions, CreateHostSimulation, Clock);
        Hook(Host);
    }

    public ManualClock Clock { get; } = new();

    public LoopbackNetwork Network { get; }

    public LoopbackTransport HostTransport { get; }

    public HostOptions HostOptions { get; }

    public HostSession Host { get; private set; }

    public List<HashChainSimulation> HostSimulations { get; } = [];

    public HashChainSimulation HostSimulation => (HashChainSimulation)Host.Simulation!;

    public List<PlayerPresenceChangedEventArgs> HostPresence { get; } = [];

    public List<DesyncDetectedEventArgs> HostDesyncs { get; } = [];

    public List<ChatReceivedEventArgs> HostChats { get; } = [];

    public List<SpeedRequestedEventArgs> SpeedRequests { get; } = [];

    public List<CommandRejectedEventArgs> RejectedCommands { get; } = [];

    public List<TestPeer> Clients { get; } = [];

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

    public TestPeer AddClient(string name, LoopbackLinkOptions? link = null, Func<ClientOptions, ClientOptions>? configure = null, Guid? id = null)
    {
        var peer = new TestPeer
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Transport = new LoopbackTransport(Network, name) { Link = link ?? new LoopbackLinkOptions() },
        };
        peer.Session = CreateSession(peer, configure);
        Clients.Add(peer);
        peer.Session.Connect(CurrentHostAddress);
        return peer;
    }

    /// <summary>Where clients connect: the current host (which changes after a host migration).</summary>
    public string CurrentHostAddress => Host.Address ?? HostAddress;

    /// <summary>A player joins on a transport it already has (for example the old host after leaving).</summary>
    public TestPeer JoinWith(string name, LoopbackTransport transport, Guid id, string address)
    {
        var peer = new TestPeer { Id = id, Name = name, Transport = transport };
        peer.Session = CreateSession(peer, null);
        Clients.Add(peer);
        peer.Session.Connect(address);
        return peer;
    }

    /// <summary>The same player (same id and transport) joins again with a new session.</summary>
    public void Rejoin(TestPeer peer, Func<ClientOptions, ClientOptions>? configure = null)
    {
        peer.Session.Dispose();
        peer.Session = CreateSession(peer, configure);
        peer.Session.Connect(CurrentHostAddress);
    }

    public void ReplaceHost(Func<HostSession> create)
    {
        Host = create();
        Hook(Host);
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
        Host.Update();
        foreach (TestPeer client in Clients.ToArray())
        {
            if (client.Session.Phase != ClientPhase.Ended)
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
            if (Host.LocalPlayer.CanSendCommands && random.NextDouble() < chancePerFrame)
            {
                Assert.Equal(RequestResult.Accepted, Host.SendCommand(RandomPayload()));
            }

            foreach (TestPeer client in Clients)
            {
                if (client.Session.CanSendCommands && random.NextDouble() < chancePerFrame)
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
        TimeSpan end = Clock.Now + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (Clock.Now >= end)
            {
                throw new XunitException($"Timed out waiting for {waitingFor}.");
            }

            Step();
        }
    }

    public void RunUntilJoined(params TestPeer[] peers) =>
        RunUntil(() => peers.All(p => p.Session.Phase is ClientPhase.Lobby or ClientPhase.InGame), "the clients to join");

    /// <summary>Everyone with a slot says ready, then the host starts and every client runs the match.</summary>
    public void StartMatch(byte[]? setup = null)
    {
        TestPeer[] members = [.. Clients.Where(c => c.Session.Phase != ClientPhase.Ended)];
        RunUntilJoined(members);
        foreach (TestPeer client in members.Where(c => c.Session.LocalPlayer?.HasSlot == true))
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
        foreach (TestPeer client in Clients)
        {
            client.Session.Dispose();
        }

        Host.Dispose();
    }

    private ClientSession CreateSession(TestPeer peer, Func<ClientOptions, ClientOptions>? configure)
    {
        var options = new ClientOptions
        {
            GameVersion = GameVersion,
            ContentVersion = ContentVersion,
            PlayerId = peer.Id,
            PlayerName = peer.Name,
        };
        var session = new ClientSession(
            peer.Transport,
            configure?.Invoke(options) ?? options,
            start =>
            {
                var simulation = new HashChainSimulation(start, snapshotPadding);
                peer.Simulations.Add(simulation);
                return simulation;
            },
            Clock);
        session.Ended += (_, e) => peer.Ends.Add(e);
        session.RequestDenied += (_, e) => peer.Denials.Add(e);
        session.PlayerPresenceChanged += (_, e) => peer.Presence.Add(e);
        session.DesyncDetected += (_, e) => peer.Desyncs.Add(e);
        session.MatchStarted += (_, e) => peer.MatchStarts.Add(e);
        session.ChatReceived += (_, e) => peer.Chats.Add(e);
        session.PhaseChanged += (_, e) => peer.Phases.Add(e.Phase);
        session.HostChanged += (_, e) => peer.HostChanges.Add(e.HostId);
        session.BecameHost += (_, e) =>
        {
            peer.Hosting = e.Host;
            ReplaceHost(() => e.Host);
        };
        return session;
    }

    private void Hook(HostSession host)
    {
        host.PlayerPresenceChanged += (_, e) => HostPresence.Add(e);
        host.DesyncDetected += (_, e) => HostDesyncs.Add(e);
        host.ChatReceived += (_, e) => HostChats.Add(e);
        host.SpeedRequested += (_, e) => SpeedRequests.Add(e);
        host.CommandRejected += (_, e) => RejectedCommands.Add(e);
    }
}
