using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Net;
using Tribes.Core.Commands;
using Tribes.Net;
using Tribes.Net.Photon;
using Tribes.Session;

namespace Tribes.Client.Session.Dev;

/// <summary>
/// Headless internet match through the Photon Cloud for automated checks
/// (res://scenes/session/internet_dev.tscn; exported builds: <c>--ui-screen=../session/internet_dev</c>).
/// Options after "--":
/// <c>--net-host=N</c> hosts and starts once N players are ready (computer tribes: <c>--net-computers=K</c>, default 1);
/// <c>--net-room-file=PATH</c> host: writes the room address there;
/// <c>--net-join=ADDRESS</c> or <c>--net-join-file=PATH</c> (waits for the file) joins;
/// <c>--net-join-list</c> comes back to this player's match through the room list (「回到對局」);
/// <c>--net-resume=PATH</c> opens a hosted save as an internet room (as the load screen does)
/// and goes on once <c>--net-resume-wait=N</c> players are back (default all);
/// <c>--net-ticks=T</c> ticks to play (2000); <c>--player-name=NAME</c> and <c>--player-id=new</c>
/// (as for the lobby: several instances on one PC need their own keys); <c>--net-seed=S</c>;
/// <c>--net-speed=PERCENT</c> (200); <c>--net-mode=endless</c>; <c>--net-region=CODE</c> (nearest);
/// <c>--net-rejoin-at=TICK</c> (client: leave there, come back through the room list's 「回到對局」);
/// <c>--net-quit-at=TICK</c> (host: leave there, the others carry on under a new host);
/// <c>--net-timeout=SECONDS</c> (900).
/// Every instance plays scripted orders and prints <c>SESSION_HASH name=.. tick=.. hash=..</c> every
/// 250 ticks, <c>NET_EVENT ..</c> lines with the wall clock for joins and host changes, then
/// <c>SESSION_DONE</c>, or <c>SESSION_FAIL</c> and exit code 1. The AppId is never printed.
/// </summary>
public partial class InternetDevRunner : Node
{
    private const int HashEvery = 250;
    private const double LingerSeconds = 45;

    private string name = "peer";
    private long targetTick;
    private double timeout;
    private double elapsed;
    private double doneAt = -1;
    private int frame;
    private int rejoinAt = -1;
    private int quitAt = -1;
    private bool readySent;
    private bool finished;
    private string address = "";
    private string? joinFile;
    private HostSession? host;
    private ClientSession? client;
    private NetworkMatchRunner? runner;
    private PhotonRoomBrowser? browser;
    private ScriptedOrders script = new(1);
    private int expected;
    private int resumeWait;
    private bool resumeAnnounced;

    /// <inheritdoc />
    public override void _Ready()
    {
        name = LobbyConnection.PlayerName();
        targetTick = DevOptions.GetInt("net-ticks", 2000);
        timeout = DevOptions.GetInt("net-timeout", 900);
        rejoinAt = DevOptions.GetInt("net-rejoin-at", -1);
        quitAt = DevOptions.GetInt("net-quit-at", -1);
        script = new ScriptedOrders(name.Aggregate(17, (h, c) => (h * 31) + c));
        if (!InternetPlay.IsAvailable)
        {
            Fail("this build has no Photon AppId");
            return;
        }

        Event("start", $"region={InternetPlay.Region ?? "best"}");
        expected = DevOptions.GetInt("net-host", -1);
        if (expected >= 0)
        {
            StartHost();
        }
        else if (DevOptions.Get("net-join") is { Length: > 0 } join)
        {
            address = join;
            StartClient();
        }
        else if (DevOptions.Get("net-join-file") is { Length: > 0 } file)
        {
            joinFile = file;
        }
        else if (DevOptions.Has("net-join-list"))
        {
            browser = new PhotonRoomBrowser(InternetPlay.Settings()!);
            browser.Start();
            Event("looking-for-match");
        }
        else if (DevOptions.Get("net-resume") is { Length: > 0 } save)
        {
            Resume(save);
        }
        else
        {
            Fail("give --net-host=N, --net-join=ADDRESS, --net-join-file=PATH, --net-join-list or --net-resume=PATH");
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

        if (joinFile is not null)
        {
            WaitForRoomFile();
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
        ContinueWhenBack();
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

    private static string Clock() => DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private void Event(string what, string detail = "") => GD.Print($"NET_EVENT name={name} at={Clock()} {what} {detail}".TrimEnd());

    private void StartHost()
    {
        bool endless = DevOptions.Get("net-mode") == "endless";
        host = LobbyConnection.HostInternet(endless ? RoomMode.Endless : RoomMode.Versus).HandOver(null).Host!;
        for (int i = 0; i < DevOptions.GetInt("net-computers", 1); i++)
        {
            host.AddComputerPlayer(aiLevel: 0);
        }

        Event("hosting", $"expecting={expected}");
    }

    private void StartClient()
    {
        client = LobbyConnection.JoinInternet(address).HandOver(null).Client!;
        readySent = false;
        Event("joining", address);
    }

    private void WaitForRoomFile()
    {
        if (frame % 30 != 0 || !File.Exists(joinFile))
        {
            return;
        }

        string text = File.ReadAllText(joinFile!).Trim();
        if (text.Length == 0)
        {
            return;
        }

        address = text;
        joinFile = null;
        StartClient();
    }

    private void WaitForMatch()
    {
        if (host is not null)
        {
            host.Update();
            if (host.Address is { } room && DevOptions.Get("net-room-file") is { Length: > 0 } file && !File.Exists(file))
            {
                File.WriteAllText(file, room);
                Event("room", room);
            }

            int ready = host.Room.Players.Count(p => p is { Kind: PlayerKind.Human, IsHost: false, IsConnected: true, IsReady: true });
            if (ready >= expected && host.CanStart() == RequestResult.Accepted)
            {
                var plan = new MatchPlan { Mission = 8, Difficulty = 0, Seed = DevOptions.GetInt("net-seed", 4242) };
                RequestResult started = host.Start(plan.ToBytes());
                Event("start-match", started.ToString());
                if (started == RequestResult.Accepted)
                {
                    Bind(new NetworkMatchRunner(host, Options()), host);
                    host.SetSpeed(DevOptions.GetInt("net-speed", 200));
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
            Bind(new NetworkMatchRunner(client, Options()), client);
        }
        else if (client.Phase == ClientPhase.Ended)
        {
            Fail($"session ended: {client.EndReason} {client.EndMessage} ({InternetPlay.TransportOf(client)?.LastError})");
        }
    }

    /// <summary>The game's own runner settings: saves go to user://saves as in the game.</summary>
    private static RunnerOptions Options() => MatchSessions.Options();

    /// <summary>Opens a hosted save as an internet room through the game's own path.</summary>
    private void Resume(string path)
    {
        try
        {
            var request = new Tribes.Client.App.MatchRequest
            {
                Mode = Tribes.Client.App.MatchMode.Versus,
                SaveToLoad = ProjectSettings.GlobalizePath(path),
                ResumeOnInternet = true,
            };
            var resumed = (NetworkMatchRunner)MatchSessions.Open(request);
            Bind(resumed, resumed.Host!);
            resumeWait = DevOptions.GetInt("net-resume-wait", resumed.ExpectedPlayers.Count);
            Event("resumed", $"tick={resumed.Simulation.Tick} expecting={resumed.ExpectedPlayers.Count}");
        }
        catch (Exception e) when (e is SaveFileException or InvalidCastException)
        {
            Fail("cannot resume: " + e.Message);
        }
    }

    /// <summary>A resumed match goes on once enough players are back.</summary>
    private void ContinueWhenBack()
    {
        NetworkMatchRunner current = runner!;
        if (current.Phase != MatchPhase.WaitingForPlayers)
        {
            return;
        }

        if (current.Host?.Address is { } room && !resumeAnnounced)
        {
            resumeAnnounced = true;
            Event("room", room);
        }

        if (current.ExpectedPlayers.Count(p => p.IsConnected) >= resumeWait)
        {
            Event("continue", $"tick={current.Simulation.Tick} result={current.ContinueResumed()}");
        }
    }

    private void Bind(NetworkMatchRunner bound, object session)
    {
        runner = bound;
        InternetPlay.Watch(bound, session);
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
        bound.Notice += (_, e) =>
        {
            string who = e.Notice.Player?.Name ?? "";
            Event("notice", $"{e.Notice.Kind} {who} tick={e.Notice.Tick} host={bound.IsHost}");
        };
        Event("in-match", $"tick={bound.Simulation.Tick} slot={bound.Players.ById(bound.LocalPlayerId)?.Slot} tribe={bound.LocalTribe} role={bound.Role} host={bound.IsHost}");
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
            GD.Print($"SESSION_DONE name={name} tick={tick} hash=0x{bound.StateHash():x8} players={bound.Players.Count} host={runner?.IsHost} role={runner?.Role} tribe={runner?.LocalTribe}");
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

        long tick = current.Simulation.Tick;
        if (current.IsHost && quitAt > 0 && tick >= quitAt && doneAt < 0)
        {
            Event("quit", $"tick={tick}");
            current.Quit();
            Finish(0);
            return;
        }

        if (!current.IsHost && rejoinAt > 0 && tick >= rejoinAt)
        {
            rejoinAt = -1;
            Event("leave-to-rejoin", $"tick={tick}");
            current.Quit();
            current.Dispose();
            runner = null;
            client = null;
            browser = new PhotonRoomBrowser(InternetPlay.Settings()!);
            browser.Start();
        }
    }

    /// <summary>Comes back the way a player does from the 「網際網路」 tab: the room list's 「回到對局」.</summary>
    private void FindRoomInList()
    {
        browser!.Poll();
        (string game, string content) = LobbyConnection.Versions();
        RoomListEntry? mine = RoomListing.Build(browser.Rooms, LobbyConnection.PlayerKey(), game, content, BuildFingerprint.Current)
            .FirstOrDefault(e => e.Action == RoomAction.Return);
        if (mine is null)
        {
            return;
        }

        Event("found-in-list", $"{mine.Room.Address} action={mine.Action}");
        address = mine.Room.Address;
        browser.Dispose();
        browser = null;
        StartClient();
    }

    private void FinishWhenOthersLeft()
    {
        if (!runner!.IsHost)
        {
            Finish(0);
            return;
        }

        RoomState? room = runner.Room;
        bool othersGone = room is null || room.Players.All(p => p.IsHost || p.Kind == PlayerKind.Computer || !p.IsConnected);
        if (othersGone || elapsed - doneAt > LingerSeconds)
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
        browser?.Dispose();
        Event("exit", exitCode.ToString(CultureInfo.InvariantCulture));
        GetTree().Quit(exitCode);
    }
}
