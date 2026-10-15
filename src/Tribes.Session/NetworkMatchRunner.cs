using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Net;
using Tribes.Net.Transport;

namespace Tribes.Session;

/// <summary>A participant a resumed match waits for.</summary>
/// <param name="Player">The participant as the save left them.</param>
/// <param name="IsConnected">Whether they are connected again.</param>
public sealed record ExpectedPlayer(PlayerEntry Player, bool IsConnected);

/// <summary>
/// A network match on this device, hosted (<see cref="HostSession"/>) or joined
/// (<see cref="ClientSession"/>); the session drives a <see cref="LockstepMatch"/>. The host keeps
/// the records: it autosaves, saves on quit and on a desync, and marks wiped-out tribes as
/// defeated. A desync stops the match on every device with both hashes and the tick in the log.
/// </summary>
public sealed partial class NetworkMatchRunner : MatchRunner
{
    private const int MaxPendingOrders = 256;

    private readonly LinkedList<(GameOrder Order, byte[] Payload)> pending = new();
    private readonly NetworkSink sink;
    private HostSession? host;
    private ClientSession? client;
    private LockstepMatch match;
    private double sinceTick;

    /// <summary>Runs the match of a hosted session that is in the match phase.</summary>
    /// <exception cref="InvalidOperationException">The session runs no <see cref="LockstepMatch"/>.</exception>
    public NetworkMatchRunner(HostSession host, RunnerOptions options)
        : this(KindOf(Require(host, nameof(host)).Match), Bind(host!.Simulation), options)
    {
        this.host = host;
        HookHost(host!);
        if (host!.IsPaused)
        {
            SetPhase(MatchPhase.WaitingForPlayers);
        }
        DecideIfOver();
        WarmUpSaves();
    }

    /// <summary>Runs the match of a joined session that is in the match phase.</summary>
    /// <exception cref="InvalidOperationException">The session runs no <see cref="LockstepMatch"/>.</exception>
    public NetworkMatchRunner(ClientSession client, RunnerOptions options)
        : this(KindOf(Require(client, nameof(client)).Match), Bind(client!.Simulation), options)
    {
        this.client = client;
        HookClient(client!);
        DecideIfOver();
    }

    private NetworkMatchRunner(MatchKind kind, LockstepMatch match, RunnerOptions options)
        : base(kind, options)
    {
        this.match = match;
        sink = new NetworkSink(this);
        HookMatch(match);
    }

    /// <summary>The host session, while this device hosts.</summary>
    public HostSession? Host => host;

    /// <summary>The client session, while this device is a client.</summary>
    public ClientSession? Client => client;

    /// <summary>The lockstep match the session drives.</summary>
    public LockstepMatch Lockstep => match;

    /// <summary>The room as last known.</summary>
    public RoomState? Room => host?.Room ?? client?.Room;

    /// <inheritdoc />
    public override bool IsNetwork => true;

    /// <inheritdoc />
    public override bool IsHost => host is not null;

    /// <inheritdoc />
    public override GameSimulation Simulation => match.Simulation;

    /// <inheritdoc />
    public override PlayerTable Players => match.Players;

    /// <inheritdoc />
    public override Guid LocalPlayerId => host?.LocalPlayer.Id ?? client?.LocalPlayerId ?? Guid.Empty;

    /// <inheritdoc />
    public override IOrderSink Orders => sink;

    /// <inheritdoc />
    public override bool IsPaused => host?.IsPaused ?? client?.IsPaused ?? false;

    /// <inheritdoc />
    public override bool CanPause =>
        Phase != MatchPhase.Stopped && (IsHost || (Room?.Settings.AllowClientPause == true && client?.CanSendCommands == true));

    /// <inheritdoc />
    public override bool CanChangeSpeed =>
        Phase != MatchPhase.Stopped && (IsHost || (Room?.Settings.AcceptSpeedRequests == true && client?.CanSendCommands == true));

    /// <inheritdoc />
    public override int SpeedPercent => host?.SpeedPercent ?? client?.SpeedPercent ?? 100;

    /// <inheritdoc />
    public override double TickSeconds => match.Start.TickMilliseconds / 1000.0 * 100 / SpeedPercent;

    /// <inheritdoc />
    public override double TickAlpha => IsPaused ? 1 : Math.Clamp(sinceTick / TickSeconds, 0, 1);

    /// <summary>Whether new players may join the running match (endless mode, host's choice).</summary>
    public bool IsInviteOpen => Room is { Settings: { Mode: RoomMode.Endless, AllowJoinInProgress: true } };

    /// <summary>The human participants a resumed match waits for, with whether they are back.</summary>
    public IReadOnlyList<ExpectedPlayer> ExpectedPlayers
    {
        get
        {
            var list = new List<ExpectedPlayer>();
            foreach (PlayerEntry entry in Players)
            {
                if (!entry.IsComputer && entry.HasSlot && entry.Id != LocalPlayerId)
                {
                    list.Add(new ExpectedPlayer(entry, Room?.FindPlayer(entry.Id)?.IsConnected == true));
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Continues a hosted match from a save: the room opens again on <paramref name="transport"/>,
    /// paused, in <see cref="MatchPhase.WaitingForPlayers"/>; players come back with their
    /// persistent ids (those who gave up come back as spectators). Call
    /// <see cref="ContinueResumed"/> to go on.
    /// </summary>
    /// <exception cref="SaveFileException">The save cannot be resumed here.</exception>
    public static NetworkMatchRunner Resume(ITransport transport, HostOptions hostOptions, ReadOnlySpan<byte> file,
        Func<MatchStart, ILockstepSimulation> simulationFactory, RunnerOptions options, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        (SaveHeader header, byte[] body) = MatchSaveFile.Read(file);
        if (header.Kind != SaveKind.HostedMatch)
        {
            throw new SaveFileException(SaveProblem.NotASave, "This save is a single-player game, not a hosted match.");
        }
        // A host may resume its match on a newer build: the save is read as long as its formats are
        // (older or the same; newer ones were refused above). The room then runs this build, so the
        // players join with it too; the header keeps the build that wrote the save.
        if (header.GameVersion != hostOptions.GameVersion || header.ContentVersion != hostOptions.ContentVersion)
        {
            options.Log?.Invoke($"SESSION resuming a match saved by {header.GameVersion} ({header.ContentVersion}) on {hostOptions.GameVersion} ({hostOptions.ContentVersion})");
        }
        body = RestampVersions(body, hostOptions.GameVersion, hostOptions.ContentVersion);
        HostSession session;
        try
        {
            session = HostSession.ResumeFromSave(transport, hostOptions, body, simulationFactory, clock);
        }
        catch (ProtocolException e)
        {
            throw new SaveFileException(SaveProblem.Damaged, "The saved match is damaged: " + e.Message, e);
        }
        catch (ArgumentException e)
        {
            // The versions were made this build's above, so the save belongs to another host.
            throw new SaveFileException(SaveProblem.NotTheHost, e.Message, e);
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            throw new SaveFileException(SaveProblem.CannotHost, "The room cannot be opened: " + e.Message, e);
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            throw new SaveFileException(SaveProblem.Damaged, "The saved world does not load: " + e.Message, e);
        }
        return new NetworkMatchRunner(session, options);
    }

/// <summary>
    /// The network layer's match save with its game and content versions replaced by
    /// <paramref name="gameVersion"/> and <paramref name="contentVersion"/> (the documented layout:
    /// <c>TRBS</c>, <c>u16</c> format, two strings of a LEB128 length and UTF-8 bytes); the rest stays
    /// byte for byte.
    /// </summary>
    /// <exception cref="SaveFileException">The bytes do not start like a match save.</exception>
    internal static byte[] RestampVersions(byte[] save, string gameVersion, string contentVersion)
    {
        ArgumentNullException.ThrowIfNull(save);
        const int Prefix = 6;
        if (save.Length < Prefix || !save.AsSpan(0, 4).SequenceEqual("TRBS"u8))
        {
            throw new SaveFileException(SaveProblem.Damaged, "The saved match is damaged: it does not start like a match save.");
        }
        int at = Prefix;
        for (int field = 0; field < 2; field++)
        {
            at = SkipString(save, at);
        }
        using var stream = new MemoryStream(save.Length + 32);
        stream.Write(save, 0, Prefix);
        WriteString(stream, gameVersion);
        WriteString(stream, contentVersion);
        stream.Write(save, at, save.Length - at);
        return stream.ToArray();
    }

    private static int SkipString(byte[] data, int at)
    {
        ulong length = 0;
        for (int shift = 0; ; shift += 7)
        {
            if (at >= data.Length || shift > 28)
            {
                throw new SaveFileException(SaveProblem.Damaged, "The saved match is damaged: a version is cut short.");
            }
            byte b = data[at++];
            length |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }
        }
        if (length > (ulong)(data.Length - at))
        {
            throw new SaveFileException(SaveProblem.Damaged, "The saved match is damaged: a version is cut short.");
        }
        return at + (int)length;
    }

    private static void WriteString(Stream stream, string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        for (uint length = (uint)bytes.Length; ; length >>= 7)
        {
            if (length < 0x80)
            {
                stream.WriteByte((byte)length);
                break;
            }
            stream.WriteByte((byte)(length | 0x80));
        }
        stream.Write(bytes);
    }

    /// <summary>
    /// Opens an endless world played alone to other players: a hosted endless match starts from
    /// <paramref name="local"/>'s world at its tick, with joining in progress allowed (each player
    /// who joins gets a free slot), and <paramref name="local"/> stops.
    /// </summary>
    /// <exception cref="InvalidOperationException">The game is not a running endless game, or its world is too large to send.</exception>
    public static NetworkMatchRunner HostLocalGame(LocalMatchRunner local, ITransport transport, HostOptions hostOptions,
        Func<MatchStart, ILockstepSimulation> simulationFactory, RunnerOptions options, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(hostOptions);
        if (local.Match != MatchKind.Endless || local.Phase is not MatchPhase.Running)
        {
            throw new InvalidOperationException("Only a running endless game can be opened to other players.");
        }
        byte[] setup = MatchPlan.Continuing(local.Simulation).ToBytes();
        if (setup.Length > NetProtocol.MaxGameSetupBytes)
        {
            throw new InvalidOperationException($"The world needs {setup.Length} bytes, more than a match start can carry.");
        }
        PlayerEntry? me = local.Players.ById(local.LocalPlayerId);
        var session = new HostSession(transport, hostOptions with
        {
            PreferredColour = hostOptions.PreferredColour >= 0 ? hostOptions.PreferredColour : me?.Colour ?? -1,
            Room = hostOptions.Room with { Mode = RoomMode.Endless, AllowJoinInProgress = true },
        }, simulationFactory, clock);
        RequestResult started;
        try
        {
            started = session.Start(setup);
        }
        catch
        {
            session.Dispose();
            throw;
        }
        if (started != RequestResult.Accepted)
        {
            session.Dispose();
            throw new InvalidOperationException($"The network match did not start: {started}.");
        }
        NetworkMatchRunner runner;
        try
        {
            runner = new NetworkMatchRunner(session, options);
        }
        catch
        {
            session.Dispose();
            throw;
        }
        // Only once the network match runs does the game alone stop.
        local.HandOver();
        return runner;
    }

    /// <summary>
    /// Tells a hosting runner that its transport lost the room for good while the other players
    /// carry on with a new host (an internet room after this device lost its connection). After
    /// the current update the runner saves, stops with <see cref="StopReason.ConnectionLost"/> and
    /// closes the hosted session; the player can come back to the match as a client. Ignored when
    /// this device does not host. Safe to call from inside the session's update.
    /// </summary>
    public void ReportHostingLost() => hostingLost = host is not null;

    /// <summary>The host goes on with a resumed match (whoever is back by now).</summary>
    public RequestResult ContinueResumed()
    {
        if (host is null || Phase != MatchPhase.WaitingForPlayers)
        {
            return RequestResult.NotAllowedNow;
        }
        RequestResult result = host.Resume();
        if (result == RequestResult.Accepted)
        {
            SetPhase(match.Outcome is null ? MatchPhase.Running : MatchPhase.Over);
        }
        return result;
    }

    /// <summary>Endless mode, host: lets players join the running match (each gets a free slot and a new tribe) or stops that.</summary>
    public RequestResult SetInviteOpen(bool open)
    {
        if (host is null || Room?.Settings.Mode != RoomMode.Endless || Phase == MatchPhase.Stopped)
        {
            return RequestResult.NotAllowedNow;
        }
        host.SetJoinInProgress(open);
        RaisePlayersChanged();
        return RequestResult.Accepted;
    }

    /// <inheritdoc />
    public override void Update(double deltaSeconds)
    {
        if (Phase == MatchPhase.Stopped)
        {
            return;
        }
        sinceTick += Math.Max(0, deltaSeconds);
        HoldEvents();
        try
        {
            if (host is not null)
            {
                host.Update();
            }
            else
            {
                client?.Update();
            }
        }
#pragma warning disable CA1031 // A failure inside the rules or the session must end the match with a message, not the game.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            Log($"SESSION failed at tick {Simulation.Tick}: {e}");
            StopWith(new MatchStop(StopReason.Error, Simulation.Tick, e.GetType().Name + ": " + e.Message));
            closeSession = true;
        }
        finally
        {
            ReleaseEvents();
        }
        RunDeferred();
        TakeSaveResults();
    }

    /// <inheritdoc />
    public override RequestResult SetPaused(bool paused)
    {
        if (Phase == MatchPhase.Stopped)
        {
            return RequestResult.NotAllowedNow;
        }
        if (host is not null)
        {
            if (!paused && Phase == MatchPhase.WaitingForPlayers)
            {
                return ContinueResumed();
            }
            return paused ? host.Pause() : host.Resume();
        }
        if (!CanPause)
        {
            return RequestResult.NotAllowedNow;
        }
        return paused ? client!.RequestPause() : client!.RequestResume();
    }

    /// <inheritdoc />
    public override RequestResult SetSpeed(int speedPercent)
    {
        if (!RoomLimits.IsValidSpeed(speedPercent))
        {
            return RequestResult.InvalidValue;
        }
        if (!CanChangeSpeed)
        {
            return RequestResult.NotAllowedNow;
        }
        return host?.SetSpeed(speedPercent) ?? client!.RequestSpeed(speedPercent);
    }

    /// <inheritdoc />
    public override RequestResult Forfeit()
    {
        if (Phase == MatchPhase.Stopped || Players.ById(LocalPlayerId) is not { HasSlot: true, IsComputer: false, IsFinished: false })
        {
            return RequestResult.NotAllowedNow;
        }
        return host?.Forfeit() ?? client!.Forfeit();
    }

    /// <inheritdoc />
    public override void Quit()
    {
        if (Phase == MatchPhase.Stopped)
        {
            return;
        }
        long tick = Simulation.Tick;
        if (host is not null)
        {
            SaveBeforeLeaving();
            StopWith(new MatchStop(StopReason.Quit, tick, "The host quit; the match was saved."));
            // Hands the match to the next player where the transport can (Photon); on LAN this closes it.
            host.Leave();
        }
        else
        {
            StopWith(new MatchStop(StopReason.Quit, tick, "The player left the match."));
            client?.Leave();
        }
    }

    /// <inheritdoc />
    private protected override (SaveHeader Header, byte[] Body) BuildSave()
    {
        HostSession session = host ?? throw new InvalidOperationException("Only the host saves a network match.");
        byte[] body = session.SaveMatch();
        var header = new SaveHeader
        {
            Kind = SaveKind.HostedMatch,
            Match = Match,
            GameVersion = Options.GameVersion,
            ContentVersion = Options.ContentVersion,
            MatchId = match.Start.MatchId,
            Mission = match.Plan.Mission,
            Difficulty = match.Plan.Difficulty,
            Tick = match.Tick,
            TickMilliseconds = match.Start.TickMilliseconds,
            SavedAt = Options.UtcNow(),
            Title = Options.Title,
            Players = match.Players,
            Settings = match.Simulation.IsMatch ? match.Simulation.World.Settings : null,
        };
        return (header, body);
    }

    /// <inheritdoc />
    private protected override void DisposeSession()
    {
        sessionDisposed = true;
        UnhookMatch(match);
        host?.Dispose();
        client?.Dispose();
    }

    private static T Require<T>(T? value, string name)
        where T : class => value ?? throw new ArgumentNullException(name);

    private static MatchKind KindOf(MatchStart? start) =>
        start?.Mode == RoomMode.Endless ? MatchKind.Endless : MatchKind.Versus;

    private static LockstepMatch Bind(ILockstepSimulation? simulation) =>
        simulation as LockstepMatch ?? throw new InvalidOperationException(
            "The session runs no LockstepMatch: create sessions with LockstepMatch.Factory.");
}
