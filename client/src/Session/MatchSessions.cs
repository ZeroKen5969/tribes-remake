using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Lan;
using Tribes.Session;

namespace Tribes.Client.Session;

/// <summary>
/// Turns what the menus ask for (<see cref="MatchRequest"/>) into a running match
/// (<see cref="IMatchRunner"/>): a game alone, a network match handed over by the lobby, a
/// single-player save, or a hosted save resumed on the local network. The match screen calls
/// <see cref="Open"/> when it opens, then <c>Update</c> every frame and <c>Dispose</c> when it
/// closes.
/// </summary>
public static class MatchSessions
{
    /// <summary>The rules' tribe handling used by every network match this build starts or joins.</summary>
    public static ITribeControl TribeControl { get; set; } = MultiTribeControl.Instance;

    /// <summary>
    /// Makes the lobby's sessions build the real rules: <see cref="MatchSimulations.Factory"/>
    /// builds a <see cref="LockstepMatch"/>. Runs when the client assembly loads.
    /// </summary>
#pragma warning disable CA2255 // The client is an application; this wires its own menus to the rules at load.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install() => MatchSimulations.Factory = CreateSimulation;

    /// <summary>The simulation of a network match (the same factory on every peer).</summary>
    public static ILockstepSimulation CreateSimulation(MatchStart start) =>
        LockstepMatch.Create(GameApp.Instance.Content, start, TribeControl);

    /// <summary>The runner for <paramref name="request"/>.</summary>
    /// <exception cref="SaveFileException">The save to load cannot be used (show <see cref="SessionText.SaveProblem"/>).</exception>
    public static IMatchRunner Open(MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RunnerOptions options = Options();
        if (request.Session is { } session)
        {
            return FromLobby(session, options);
        }
        if (request.SaveToLoad is { } path)
        {
            return OpenSave(path, options, request.ResumeOnInternet);
        }
        MatchPlayer? local = request.LocalPlayer;
        MatchPlayer? computer = null;
        foreach (MatchPlayer player in request.Players)
        {
            computer ??= player.IsComputer ? player : null;
        }
        return Alone(LocalMatchRunner.Start(GameApp.Instance.Content, new LocalMatchSetup
        {
            Match = KindOf(request.Mode),
            Mission = request.Mission,
            Difficulty = request.Difficulty,
            Tutorial = request.Tutorial,
            TutorialScenesSeen = request.Mode == MatchMode.Campaign ? CampaignProgress.Load().TutorialFlags : 0,
            ShowHints = request.ShowHints,
            Seed = NewSeed(request),
            PlayerName = local?.Name ?? LobbyConnection.PlayerName(),
            Colour = local?.Colour ?? -1,
            ComputerColour = computer?.Colour ?? -1,
            ComputerTribes = request.Endless?.ComputerTribes ?? 1,
            NewTribes = request.Endless?.NewTribes ?? MultiTribeControl.SteadyNewTribes,
            StartAreaSize = request.Endless?.StartAreaSize ?? 0,
            Settings = request.Settings,
        }, options));
    }

    /// <summary>A fresh seed for every new game, unless the endless world has one or "--session-seed=N" asks for one.</summary>
    private static long NewSeed(MatchRequest request)
    {
        int dev = DevOptions.GetInt("session-seed", 0);
        if (dev > 0)
        {
            return dev;
        }
        return request.Endless?.Seed ?? Random.Shared.Next(1, int.MaxValue);
    }

    /// <summary>
    /// Whether the local player can invite players into the running match now: the host of an
    /// endless match, or an endless world played alone (it is opened on the local network).
    /// </summary>
    public static bool CanInvite(IMatchRunner runner) => runner switch
    {
        RunnerSwitch { Current: LocalMatchRunner { Match: MatchKind.Endless, Phase: MatchPhase.Running } } => true,
        RunnerSwitch { Current: var inner } => CanInvite(inner),
        NetworkMatchRunner { IsHost: true, Match: MatchKind.Endless } network => network.Phase != MatchPhase.Stopped,
        _ => false,
    };

    /// <summary>Whether players can join the running match now.</summary>
    public static bool IsInviteOpen(IMatchRunner runner) => runner switch
    {
        RunnerSwitch { Current: var inner } => IsInviteOpen(inner),
        NetworkMatchRunner network => network.IsInviteOpen,
        _ => false,
    };

    /// <summary>
    /// Endless host: lets local-network players join the running match, or stops that. An endless
    /// world played alone becomes a hosted match first (on TCP 47801), with this player in slot 0.
    /// </summary>
    public static RequestResult SetInviteOpen(IMatchRunner runner, bool open)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (runner is RunnerSwitch { Current: NetworkMatchRunner network })
        {
            return network.SetInviteOpen(open);
        }
        if (runner is NetworkMatchRunner direct)
        {
            return direct.SetInviteOpen(open);
        }
        if (!open || runner is not RunnerSwitch { Current: LocalMatchRunner } alone || !CanInvite(runner))
        {
            return open ? RequestResult.NotAllowedNow : RequestResult.Accepted;
        }
        (string game, string content) = LobbyConnection.Versions();
        string name = LobbyConnection.PlayerName();
        var hostOptions = new HostOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = LobbyConnection.PlayerKey(),
            PlayerName = name,
            Room = new RoomSettings { Name = UiText.F("lobby.room_name", name), Mode = RoomMode.Endless, AllowJoinInProgress = true },
        };
        try
        {
            alone.SwitchTo(local =>
            {
                var transport = new LanTransport();
                try
                {
                    NetworkMatchRunner hosted = NetworkMatchRunner.HostLocalGame((LocalMatchRunner)local, transport, hostOptions, CreateSimulation, Options());
                    MulticastLease.Acquire();
                    GD.Print($"SESSION opened the endless world to LAN at {hosted.Host?.Address}, tick {hosted.Simulation.Tick}");
                    return Hosted(hosted);
                }
                catch
                {
                    transport.Dispose();
                    throw;
                }
            });
            return RequestResult.Accepted;
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException or System.Net.Sockets.SocketException or IOException)
        {
            GD.PushWarning($"opening the world to LAN failed: {e.Message}");
            return RequestResult.NotAllowedNow;
        }
    }

    /// <summary>Endless games alone may be opened to the network later, so the screen keeps one runner throughout.</summary>
    private static IMatchRunner Alone(LocalMatchRunner runner) =>
        runner.Match == MatchKind.Endless ? new RunnerSwitch(runner) : runner;

    /// <summary>The header of a save file, or null when it cannot be read.</summary>
    public static SaveHeader? ReadHeader(string path)
    {
        try
        {
            return MatchSaveFile.ReadHeader(ReadFile(path));
        }
        catch (SaveFileException)
        {
            return null;
        }
    }

    /// <summary>The runner settings of this build.</summary>
    public static RunnerOptions Options()
    {
        (string game, string content) = LobbyConnection.Versions();
        return new RunnerOptions
        {
            GameVersion = game,
            ContentVersion = content,
            Saves = new CatalogSaveStore(),
            AutosaveTicks = DevOptions.GetInt("session-autosave-ticks", 1500),
            Log = line => GD.Print(line),
        };
    }

    /// <summary>The runner kind of a menu mode.</summary>
    public static MatchKind KindOf(MatchMode mode) => mode switch
    {
        MatchMode.Campaign => MatchKind.Campaign,
        MatchMode.Custom => MatchKind.Custom,
        MatchMode.Versus => MatchKind.Versus,
        _ => MatchKind.Endless,
    };

    /// <summary>The runner of a session the lobby handed over; on failure the session is closed.</summary>
    private static IMatchRunner FromLobby(NetSessionRef session, RunnerOptions options)
    {
        if (session.Host is { } host)
        {
            try
            {
                NetworkMatchRunner hosted = new(host, options);
                InternetPlay.Watch(hosted, host);
                return Hosted(hosted);
            }
            catch
            {
                host.Dispose();
                MulticastLease.Release();
                throw;
            }
        }
        ClientSession client = session.Client ?? throw new InvalidOperationException("The match request has neither a host nor a client session.");
        try
        {
            var joined = new NetworkMatchRunner(client, options);
            InternetPlay.Watch(joined, client);
            return joined;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A save to continue: a game alone, or a hosted match that opens its room again, on the
    /// local network or (<paramref name="onInternet"/>) as an internet room.
    /// </summary>
    private static IMatchRunner OpenSave(string path, RunnerOptions options, bool onInternet)
    {
        byte[] data = ReadFile(path);
        SaveHeader header = MatchSaveFile.ReadHeader(data);
        if (header.Kind == SaveKind.SinglePlayer)
        {
            return Alone(LocalMatchRunner.Load(GameApp.Instance.Content, data, options));
        }
        (string game, string content) = LobbyConnection.Versions();
        var hostOptions = new HostOptions
        {
            GameVersion = game,
            ContentVersion = content,
            RulesFingerprint = BuildFingerprint.Current,
            PlayerId = LobbyConnection.PlayerKey(),
            PlayerName = LobbyConnection.PlayerName(),
        };
        ITransport transport = onInternet ? InternetTransport() : new LanTransport();
        NetworkMatchRunner runner;
        try
        {
            runner = NetworkMatchRunner.Resume(transport, hostOptions, data, CreateSimulation, options);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
        MulticastLease.Acquire();
        if (transport is Tribes.Net.Photon.PhotonTransport photon)
        {
            InternetPlay.Register(runner.Host!, photon);
            InternetPlay.Watch(runner, runner.Host!);
        }
        GD.Print($"SESSION resumed {header.Match} tick={header.Tick} on {(onInternet ? "the internet" : "the LAN")}; waiting for {runner.ExpectedPlayers.Count} players");
        return Hosted(runner);
    }

    /// <summary>A transport for an internet room; a build without the Photon AppId cannot host one.</summary>
    private static ITransport InternetTransport() =>
        InternetPlay.Settings() is { } settings
            ? new Tribes.Net.Photon.PhotonTransport(settings)
            : throw new SaveFileException(SaveProblem.CannotHost, "This build cannot open internet rooms: it has no Photon AppId.");

    /// <summary>Gives the hosted room's multicast lease back once the runner stops.</summary>
    private static NetworkMatchRunner Hosted(NetworkMatchRunner runner)
    {
        bool released = false;
        runner.Stopped += (_, _) =>
        {
            if (!released)
            {
                released = true;
                MulticastLease.Release();
            }
        };
        return runner;
    }

    private static byte[] ReadFile(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new SaveFileException(SaveProblem.NotASave, $"The save {path} does not exist.");
        }
        return Godot.FileAccess.GetFileAsBytes(path);
    }
}

/// <summary>
/// The host's saves in user://saves, as the load screen lists them: <c>&lt;mode&gt;-&lt;slot&gt;.save</c>,
/// its description <c>.json</c> (<see cref="SaveInfo"/>) and its picture <c>.png</c>. Writes come
/// from the runner's background save thread and use plain files only.
/// </summary>
public sealed class CatalogSaveStore : DirectorySaveStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static int warmed;

    /// <summary>
    /// The store of this device. The first time in a run it exercises its file and JSON code once in
    /// the background: their first use is slow (compiled then, most on Android) and would otherwise
    /// fall on the first autosave.
    /// </summary>
    public CatalogSaveStore()
        : base(ProjectSettings.GlobalizePath(SaveCatalog.Folder), Describe)
    {
        if (Interlocked.Exchange(ref warmed, 1) == 0)
        {
            string folder = Folder;
            _ = Task.Run(() => WarmUp(folder));
        }
    }

    private static byte[] Describe(SaveSlot slot)
    {
        SaveHeader header = slot.Header;
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SaveInfo
        {
            Mode = slot.ModeName,
            Slot = slot.Slot,
            Mission = header.Mission,
            Difficulty = header.Difficulty,
            SavedAt = header.SavedAt,
            PlaySeconds = header.PlaySeconds,
            Title = header.Title,
            Settings = header.Settings,
        }, Json));
    }

    private void WarmUp(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, ".warmup.json");
            Replace(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new SaveInfo { Mode = "warmup", Slot = "0" }, Json)));
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a warm-up: the real save reports its own errors.
        }
    }
}
