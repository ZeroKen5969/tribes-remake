using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Xunit;
using Xunit.Sdk;

namespace Tribes.Session.Tests.Support;

/// <summary>One device of a <see cref="SessionCluster"/>: its session, runner and what it saw.</summary>
internal sealed class SessionPeer
{
    public required string Name { get; init; }

    public required Guid Key { get; init; }

    public bool IsHostPeer { get; init; }

    public HostSession? Host { get; set; }

    public ClientSession? Client { get; set; }

    public LoopbackTransport? Transport { get; set; }

    public NetworkMatchRunner? Runner { get; set; }

    public required ScriptedOrders Script { get; init; }

    /// <summary>Whether the peer gives scripted orders.</summary>
    public bool Plays { get; set; } = true;

    /// <summary>Match hashes at the check ticks, per run (a resumed or rejoined runner keeps adding).</summary>
    public Dictionary<long, uint> Hashes { get; } = [];

    public List<MatchNotice> Notices { get; } = [];

    public List<MatchStop> Stops { get; } = [];

    public List<MatchOverEventArgs> Results { get; } = [];

    public List<string> Log { get; } = [];

    public int Queued { get; set; }

    /// <summary>Saves of a client (written only after it took over hosting).</summary>
    public MemorySaveStore OwnSaves { get; } = new();

    public int Answered { get; set; }

    /// <summary>Orders sent straight through the session by a slot without a tribe.</summary>
    public int RawSent { get; set; }

    public int GarbageSent { get; set; }

    public long Tick => Runner?.Simulation.Tick ?? -1;
}

/// <summary>
/// A host and clients with session runners on a loopback network driven by a manual clock, all
/// in one process; every frame advances the clock 10 ms and updates every device once. Each
/// playing device gives random valid orders (and now and then bytes that are no order); every
/// device records the match hash every <see cref="CheckEvery"/> ticks.
/// </summary>
internal sealed class SessionCluster : IDisposable
{
    public const string HostAddress = "host";
    public const int CheckEvery = 100;
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(10);

    private readonly Random random;
    private readonly RoomSettings room;
    private readonly LockstepOptions lockstep;
    private int transports;

    public SessionCluster(ITribeControl control, RoomSettings? room = null, bool hostMigration = false, int seed = 1,
        int autosaveTicks = 0, LockstepOptions? lockstep = null)
    {
        random = new Random(seed);
        Control = control;
        AutosaveTicks = autosaveTicks;
        this.room = room ?? new RoomSettings { Name = "Test", Mode = RoomMode.Versus };
        this.lockstep = lockstep ?? new LockstepOptions();
        Network = new LoopbackNetwork(Clock, seed, hostMigration);
        HostPeer = new SessionPeer { Name = "Host", Key = Guid.NewGuid(), IsHostPeer = true, Script = new ScriptedOrders(seed * 100) };
        HostPeer.Host = new HostSession(new LoopbackTransport(Network, HostAddress), HostOptions(HostPeer), Factory, Clock);
    }

    public ManualClock Clock { get; } = new();

    public LoopbackNetwork Network { get; }

    public ITribeControl Control { get; }

    public int AutosaveTicks { get; }

    /// <summary>Whether the runners write saves on a background thread (as the game does).</summary>
    public bool BackgroundSaves { get; set; }

    public MemorySaveStore Store { get; } = new();

    public SessionPeer HostPeer { get; private set; }

    public List<SessionPeer> Clients { get; } = [];

    public IEnumerable<SessionPeer> Peers => Clients.Prepend(HostPeer);

    /// <summary>Chance per frame that a playing device gives an order.</summary>
    public double OrderChance { get; set; } = 0.03;

    /// <summary>Chance per frame that a playing device sends bytes that are no order.</summary>
    public double GarbageChance { get; set; } = 0.002;

    public HostSession Host => HostPeer.Host!;

    public ILockstepSimulation Factory(MatchStart start) => LockstepMatch.Create(TestContent.Content, start, Control);

    public HostOptions HostOptions(SessionPeer peer) => new()
    {
        GameVersion = TestContent.GameVersion,
        ContentVersion = TestContent.ContentVersion,
        PlayerId = peer.Key,
        PlayerName = peer.Name,
        Room = room,
        Lockstep = lockstep,
    };

    public RunnerOptions RunnerOptions(SessionPeer peer) =>
        TestContent.Runner(peer.IsHostPeer ? Store : peer.OwnSaves, AutosaveTicks, peer.Log, BackgroundSaves);

    public SessionPeer AddClient(string name, Guid? key = null, bool spectate = false, LoopbackLinkOptions? link = null)
    {
        var peer = new SessionPeer { Name = name, Key = key ?? Guid.NewGuid(), Script = new ScriptedOrders(random.Next()) };
        Clients.Add(peer);
        Connect(peer, spectate, link);
        return peer;
    }

    /// <summary>The peer connects again with a new session (same key), as after leaving or a crash.</summary>
    public void Connect(SessionPeer peer, bool spectate = false, LoopbackLinkOptions? link = null)
    {
        peer.Runner?.Dispose();
        peer.Runner = null;
        peer.Client?.Dispose();
        var transport = new LoopbackTransport(Network, $"{peer.Name}#{++transports}") { Link = link ?? new LoopbackLinkOptions() };
        peer.Transport = transport;
        peer.Client = new ClientSession(transport, new ClientOptions
        {
            GameVersion = TestContent.GameVersion,
            ContentVersion = TestContent.ContentVersion,
            PlayerId = peer.Key,
            PlayerName = peer.Name,
            Spectate = spectate,
        }, Factory, Clock);
        peer.Client.Connect(HostPeer.Host?.Address ?? HostAddress);
    }

    /// <summary>Every client says ready, the host starts, and every device gets a runner.</summary>
    public void StartMatch(MatchPlan plan)
    {
        RunUntil(() => Clients.All(c => c.Client!.Phase == ClientPhase.Lobby), "the clients to join");
        foreach (SessionPeer client in Clients.Where(c => c.Client!.LocalPlayer?.HasSlot == true))
        {
            Assert.Equal(RequestResult.Accepted, client.Client!.SetReady(true));
        }
        RunUntil(() => Host.CanStart() == RequestResult.Accepted, "everyone to be ready");
        Assert.Equal(RequestResult.Accepted, Host.Start(plan.ToBytes()));
        Bind(HostPeer, new NetworkMatchRunner(Host, RunnerOptions(HostPeer)));
        RunUntil(() => Clients.All(c => c.Runner is not null), "every client to run the match");
    }

    /// <summary>The host continues a save as a new device on the same address.</summary>
    public void ResumeHost(byte[] save, Guid? key = null)
    {
        HostPeer.Runner?.Dispose();
        var peer = new SessionPeer { Name = HostPeer.Name, Key = key ?? HostPeer.Key, IsHostPeer = true, Script = HostPeer.Script };
        NetworkMatchRunner runner = NetworkMatchRunner.Resume(new LoopbackTransport(Network, HostAddress), HostOptions(peer), save, Factory, RunnerOptions(peer), Clock);
        peer.Host = runner.Host;
        HostPeer = peer;
        Bind(peer, runner);
    }

    /// <summary>Replaces the cluster's host with a hosted runner made elsewhere (its transport must be at <see cref="HostAddress"/>).</summary>
    public void AdoptHost(NetworkMatchRunner runner, Guid key)
    {
        HostPeer.Host?.Dispose();
        var peer = new SessionPeer { Name = HostPeer.Name, Key = key, IsHostPeer = true, Script = HostPeer.Script, Host = runner.Host };
        HostPeer = peer;
        Bind(peer, runner);
    }

    public void Bind(SessionPeer peer, NetworkMatchRunner runner)
    {
        peer.Runner = runner;
        // Hashes are taken inside the step (the runner's own events come after the session's update).
        LockstepMatch? hooked = null;
        void OnStep(object? sender, LockstepTickEventArgs e)
        {
            long tick = hooked!.Tick;
            if (tick % CheckEvery == 0)
            {
                peer.Hashes[tick] = hooked.StateHash();
            }
        }
        void Rehook()
        {
            if (hooked is not null)
            {
                hooked.Stepped -= OnStep;
            }
            hooked = runner.Lockstep;
            hooked.Stepped += OnStep;
        }
        Rehook();
        runner.SimulationReplaced += (_, _) => Rehook();
        runner.TickRan += (_, e) => peer.Answered += e.LocalOrders.Count(o => o is not null);
        runner.Notice += (_, e) => peer.Notices.Add(e.Notice);
        runner.Stopped += (_, e) => peer.Stops.Add(e.Stop);
        runner.MatchOver += (_, e) => peer.Results.Add(e);
    }

    public void Step()
    {
        Clock.Advance(Frame);
        foreach (SessionPeer peer in Peers.ToArray())
        {
            if (peer.Runner is { } runner)
            {
                if (runner.Phase != MatchPhase.Stopped)
                {
                    Play(peer, runner);
                    runner.Update(Frame.TotalSeconds);
                }
                continue;
            }
            if (peer.Client is { Phase: not ClientPhase.Ended } client)
            {
                client.Update();
                if (client.Phase == ClientPhase.InGame && client.Simulation is LockstepMatch)
                {
                    Bind(peer, new NetworkMatchRunner(client, RunnerOptions(peer)));
                }
            }
            peer.Host?.Update();
        }
    }

    public void RunTicks(long ticks)
    {
        long end = HostPeer.Tick + ticks;
        RunUntil(() => HostPeer.Tick >= end, $"tick {end}", TimeSpan.FromSeconds(ticks * 0.2 + 30));
    }

    public void RunUntil(Func<bool> condition, string waitingFor, TimeSpan? timeout = null)
    {
        TimeSpan end = Clock.Now + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            if (Clock.Now >= end)
            {
                throw new XunitException($"Timed out waiting for {waitingFor}.");
            }
            Step();
        }
    }

    /// <summary>Lets every device catch up with the host (no device has unrun batches).</summary>
    public void Settle() => RunUntil(
        () => Clients.All(c => c.Runner is null || c.Runner.Phase == MatchPhase.Stopped || c.Client is null || c.Client.BufferedTicks == 0),
        "the clients to catch up");

    /// <summary>Every pair of devices agrees on every check tick both recorded; returns the number of shared checks.</summary>
    public int AssertInSync(int minimumShared, IEnumerable<SessionPeer>? peers = null)
    {
        SessionPeer[] list = [.. peers ?? Peers];
        int fewest = int.MaxValue;
        for (int i = 0; i < list.Length; i++)
        {
            for (int j = i + 1; j < list.Length; j++)
            {
                int shared = 0;
                foreach ((long tick, uint hash) in list[i].Hashes)
                {
                    if (list[j].Hashes.TryGetValue(tick, out uint other))
                    {
                        Assert.True(hash == other, $"{list[i].Name} and {list[j].Name} differ at tick {tick}: 0x{hash:x8} / 0x{other:x8}");
                        shared++;
                    }
                }
                Assert.True(shared >= minimumShared, $"{list[i].Name} and {list[j].Name} share only {shared} checks.");
                fewest = Math.Min(fewest, shared);
            }
        }
        return fewest;
    }

    public void AssertNoStops()
    {
        foreach (SessionPeer peer in Peers)
        {
            Assert.True(peer.Stops.Count == 0, $"{peer.Name} stopped: {string.Join("; ", peer.Stops)}");
        }
    }

    private void Play(SessionPeer peer, NetworkMatchRunner runner)
    {
        bool slotHolder = runner.Host?.LocalPlayer.CanSendCommands ?? runner.Client?.CanSendCommands ?? false;
        if (!peer.Plays || !slotHolder)
        {
            return;
        }
        if (random.NextDouble() < OrderChance)
        {
            if (runner.Orders.CanSubmit)
            {
                if (peer.Script.Next(runner.Simulation, runner.LocalTribe) is { } order && runner.Orders.Submit(order) == OrderSubmitResult.Queued)
                {
                    peer.Queued++;
                }
            }
            else if (peer.Script.Next(runner.Simulation, runner.ViewTribe) is { } other)
            {
                // A slot without a tribe in this world: its orders must be dropped the same way everywhere.
                byte[] payload = GameOrderCodec.Encode(other);
                bool sent = runner.Host is not null ? runner.Host.SendCommand(payload) == RequestResult.Accepted : runner.Client!.SendCommand(payload);
                peer.RawSent += sent ? 1 : 0;
            }
        }
        if (random.NextDouble() < GarbageChance)
        {
            byte[] garbage = peer.Script.Garbage();
            bool sent = runner.Host is not null ? runner.Host.SendCommand(garbage) == RequestResult.Accepted : runner.Client!.SendCommand(garbage);
            peer.GarbageSent += sent ? 1 : 0;
        }
    }

    public void Dispose()
    {
        foreach (SessionPeer peer in Peers)
        {
            peer.Runner?.Dispose();
            peer.Client?.Dispose();
            peer.Host?.Dispose();
        }
    }
}
