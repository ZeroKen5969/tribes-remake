using System;
using System.Globalization;
using System.Linq;
using Godot;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Net;
using Tribes.Core.Commands;
using Tribes.Net;
using Tribes.Net.Transport.Lan;
using Tribes.Session;

namespace Tribes.Client.Session.Dev;

/// <summary>
/// Headless LAN match for automated checks (scene res://scenes/session/session_dev.tscn).
/// Options after "--": <c>--session-host=N</c> hosts and starts once N clients are ready;
/// <c>--session-join=ADDRESS</c> joins; <c>--session-port=P</c> host port (47801);
/// <c>--session-ticks=T</c> ticks to play (2000); <c>--session-name=NAME</c>;
/// <c>--session-seed=S</c> match seed; <c>--session-speed=PERCENT</c> (400);
/// <c>--session-mode=endless</c> (an endless world: <c>--session-computers=N</c> computer tribes (2),
/// <c>--session-new-tribes=0..3</c> (2), <c>--session-dev-halls</c> people start with an alliance building);
/// <c>--session-rejoin-at=TICK</c> (client: leave there and come back);
/// <c>--session-rejoin-via=list</c> (come back through the LAN room list, not the address);
/// <c>--session-discovery</c> (host: answer LAN room lists); <c>--session-key=GUID</c> (player key);
/// <c>--session-timeout=SECONDS</c> (300). Each instance plays scripted orders and prints
/// <c>SESSION_HASH name=.. tick=.. hash=..</c> every 250 ticks, then <c>SESSION_DONE</c>, or
/// <c>SESSION_FAIL</c> and exit code 1.
/// </summary>
public partial class SessionDevRunner : Node
{
    private const int HashEvery = 250;
    private const double HostLingerSeconds = 30;

    private string name = "peer";
    private long targetTick;
    private double timeout;
    private double elapsed;
    private double doneAt = -1;
    private int frame;
    private int rejoinAt = -1;
    private bool rejoined;
    private bool readySent;
    private bool finished;
    private Guid playerKey = Guid.NewGuid();
    private string address = "";
    private HostSession? host;
    private ClientSession? client;
    private NetworkMatchRunner? runner;
    private ScriptedOrders script = new(1);
    private LanRoomBrowser? browser;

    /// <inheritdoc />
    public override void _Ready()
    {
        name = DevOptions.Get("session-name") is { Length: > 0 } n ? n : "peer";
        targetTick = DevOptions.GetInt("session-ticks", 2000);
        timeout = DevOptions.GetInt("session-timeout", 300);
        rejoinAt = DevOptions.GetInt("session-rejoin-at", -1);
        if (Guid.TryParse(DevOptions.Get("session-key"), out Guid key))
        {
            playerKey = key;
        }
        script = new ScriptedOrders(name.Aggregate(17, (h, c) => (h * 31) + c));
        // The lobby's sessions must build the real rules (set when the client assembly loads).
        GD.Print($"SESSION_DEV {name} lobby factory: {Tribes.Client.App.MatchSimulations.Factory.Method.Name}");
        int expected = DevOptions.GetInt("session-host", -1);
        if (expected >= 0)
        {
            StartHost(expected);
        }
        else if (DevOptions.Get("session-join") is { Length: > 0 } join)
        {
            address = join;
            StartClient();
        }
        else
        {
            Fail("give --session-host=N or --session-join=ADDRESS");
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (finished)
        {
            return;
        }
        elapsed += delta;
        frame++;
        if (elapsed > timeout)
        {
            Fail($"timeout at tick {runner?.Simulation.Tick ?? -1}");
            return;
        }
        if (browser is not null)
        {
            FindRoomInList();
            return;
        }
        if (runner is null)
        {
            WaitForMatch();
            return;
        }
        runner.Update(delta);
        if (runner.Phase == MatchPhase.Stopped)
        {
            OnStopped();
            return;
        }
        PlayScript();
        if (doneAt >= 0)
        {
            FinishWhenOthersLeft();
        }
    }

    private int ExpectedClients { get; set; }

    private void StartHost(int expected)
    {
        ExpectedClients = expected;
        (string game, string content) = LobbyConnection.Versions();
        var transport = new LanTransport(new LanTransportOptions
        {
            Port = DevOptions.GetInt("session-port", NetProtocol.DefaultLanPort),
            EnableDiscovery = DevOptions.Has("session-discovery"),
        });
        host = new HostSession(transport, new HostOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = playerKey,
            PlayerName = name,
            Room = new RoomSettings
            {
                Name = "session-dev",
                Mode = DevOptions.Get("session-mode") == "endless" ? RoomMode.Endless : RoomMode.Versus,
                AllowJoinInProgress = true,
            },
        }, Tribes.Client.App.MatchSimulations.Create);
        host.AddComputerPlayer(aiLevel: 0);
        GD.Print($"SESSION_DEV {name} hosting {host.Address} expecting {expected}");
    }

    private void StartClient()
    {
        (string game, string content) = LobbyConnection.Versions();
        client = new ClientSession(new LanTransport(new LanTransportOptions { EnableDiscovery = false }), new ClientOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = playerKey,
            PlayerName = name,
        }, Tribes.Client.App.MatchSimulations.Create);
        client.Connect(address);
        readySent = false;
        GD.Print($"SESSION_DEV {name} joining {address}");
    }

    private void WaitForMatch()
    {
        if (host is not null)
        {
            host.Update();
            int ready = host.Room.Players.Count(p => p is { Kind: PlayerKind.Human, IsHost: false, IsConnected: true, IsReady: true });
            if (ready >= ExpectedClients && host.CanStart() == RequestResult.Accepted)
            {
                int seed = DevOptions.GetInt("session-seed", 4242);
                var plan = DevOptions.Get("session-mode") == "endless"
                    ? new MatchPlan
                    {
                        Mission = -1, Difficulty = 0, Seed = seed,
                        Endless = new EndlessPlan(seed, 0, DevOptions.GetInt("session-computers", 2), DevOptions.GetInt("session-new-tribes", 2),
                            DevOptions.Has("session-dev-halls")),
                    }
                    : new MatchPlan { Mission = 8, Difficulty = 0, Seed = seed };
                RequestResult started = host.Start(plan.ToBytes());
                GD.Print($"SESSION_DEV {name} start {started}");
                if (started == RequestResult.Accepted)
                {
                    Bind(new NetworkMatchRunner(host, Options()));
                    host.SetSpeed(DevOptions.GetInt("session-speed", 400));
                }
            }
            return;
        }
        if (client is null)
        {
            return;
        }
        client.Update();
        if (client.Phase == ClientPhase.Lobby && !readySent)
        {
            readySent = client.SetReady(true) == RequestResult.Accepted;
        }
        if (client.Phase == ClientPhase.InGame && client.Simulation is LockstepMatch)
        {
            Bind(new NetworkMatchRunner(client, Options()));
        }
        else if (client.Phase == ClientPhase.Ended)
        {
            Fail($"session ended: {client.EndReason} {client.EndMessage}");
        }
    }

    private static RunnerOptions Options()
    {
        (string game, string content) = LobbyConnection.Versions();
        return new RunnerOptions { GameVersion = game, ContentVersion = content, Log = line => GD.Print(line) };
    }

    private void Bind(NetworkMatchRunner bound)
    {
        runner = bound;
        // Hashes are taken inside the step: the runner's own events come after the session's update.
        LockstepMatch hooked = bound.Lockstep;
        hooked.Stepped += (_, _) => OnTick(hooked);
        bound.SimulationReplaced += (_, _) =>
        {
            if (!ReferenceEquals(hooked, bound.Lockstep))
            {
                hooked = bound.Lockstep;
                hooked.Stepped += (_, _) => OnTick(hooked);
            }
        };
        bound.Notice += (_, e) => GD.Print($"SESSION_DEV {name} notice {e.Notice.Kind} {e.Notice.Player?.Name} tick={e.Notice.Tick}");
        GD.Print($"SESSION_DEV {name} in match at tick {bound.Simulation.Tick} slot={bound.Players.ById(bound.LocalPlayerId)?.Slot} tribe={bound.LocalTribe} role={bound.Role}");
    }

    private void OnTick(LockstepMatch bound)
    {
        long tick = bound.Simulation.Tick;
        if (tick % HashEvery == 0 && tick <= targetTick)
        {
            GD.Print($"SESSION_HASH name={name} tick={tick} hash=0x{bound.StateHash():x8}");
        }
        if (tick == targetTick && doneAt < 0)
        {
            GD.Print($"SESSION_DONE name={name} tick={tick} hash=0x{bound.StateHash():x8} players={bound.Players.Count}");
            doneAt = elapsed;
        }
    }

    private void PlayScript()
    {
        NetworkMatchRunner current = runner!;
        if (frame % 3 == 0 && current.Orders.CanSubmit && current.Simulation.Tick < targetTick)
        {
            GameOrder? order = script.Next(current.Simulation, current.LocalTribe);
            if (order is not null)
            {
                current.Orders.Submit(order);
            }
        }
        if (frame % 97 == 0 && current.Simulation.Tick < targetTick)
        {
            // Bytes that are no order: every peer must ignore them the same way.
            byte[] garbage = script.Garbage();
            if (current.Host is not null)
            {
                current.Host.SendCommand(garbage);
            }
            else
            {
                current.Client?.SendCommand(garbage);
            }
        }
        if (client is not null && !rejoined && rejoinAt > 0 && current.Simulation.Tick >= rejoinAt)
        {
            rejoined = true;
            GD.Print($"SESSION_DEV {name} leaving at tick {current.Simulation.Tick} to rejoin");
            current.Quit();
            current.Dispose();
            runner = null;
            if (DevOptions.Get("session-rejoin-via") == "list")
            {
                browser = new LanRoomBrowser();
                browser.Start();
                GD.Print($"SESSION_DEV {name} looking for its match in the LAN room list");
                return;
            }
            StartClient();
        }
    }

    /// <summary>Comes back the way a player does from the multiplayer screen: the room list's 「回到對局」.</summary>
    private void FindRoomInList()
    {
        browser!.Poll();
        (string game, string content) = LobbyConnection.Versions();
        Tribes.Client.Ui.Logic.RoomListEntry? mine = Tribes.Client.Ui.Logic.RoomListing.Build(browser.Rooms, playerKey, game, content, BuildFingerprint.Current)
            .FirstOrDefault(e => e.Action == Tribes.Client.Ui.Logic.RoomAction.Return);
        if (mine is null)
        {
            return;
        }
        GD.Print($"SESSION_DEV {name} found its match in the list: {mine.Room.Room.Name} at {mine.Room.Address} action={mine.Action} badge={mine.BadgeKey}");
        address = mine.Room.Address;
        browser.Dispose();
        browser = null;
        StartClient();
    }

    private void FinishWhenOthersLeft()
    {
        if (client is not null)
        {
            Finish(0);
            return;
        }
        bool othersGone = host!.Room.Players.All(p => p.IsHost || p.Kind == PlayerKind.Computer || !p.IsConnected);
        if (othersGone || elapsed - doneAt > HostLingerSeconds)
        {
            Finish(0);
        }
    }

    private void OnStopped()
    {
        MatchStop? stop = runner?.StopInfo;
        if (doneAt >= 0 && stop?.Reason is StopReason.Quit or StopReason.HostLeft)
        {
            Finish(0);
            return;
        }
        Fail($"stopped {stop?.Reason} at tick {stop?.Tick}: {stop?.Message}");
    }

    private void Fail(string reason)
    {
        GD.Print($"SESSION_FAIL name={name} {reason}");
        Finish(1);
    }

    private void Finish(int exitCode)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        runner?.Dispose();
        client?.Dispose();
        host?.Dispose();
        GD.Print($"SESSION_DEV {name} exit {exitCode.ToString(CultureInfo.InvariantCulture)}");
        GetTree().Quit(exitCode);
    }
}
