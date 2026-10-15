using System;
using System.Collections.Generic;
using System.IO;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>A game played alone, as the menus set it up.</summary>
public sealed record LocalMatchSetup
{
    /// <summary>Campaign, custom or endless.</summary>
    public required MatchKind Match { get; init; }

    /// <summary>Mission (map) number; -1 for an endless world.</summary>
    public int Mission { get; init; } = -1;

    /// <summary>Difficulty 0 easy, 1 hard.</summary>
    public int Difficulty { get; init; }

    /// <summary>Whether the tutorial explains things (first missions).</summary>
    public bool Tutorial { get; init; }

    /// <summary>The one-off tutorial scenes already seen in earlier missions (tutorial flag bits 1..3; bit 0 is ignored).</summary>
    public int TutorialScenesSeen { get; init; }

    /// <summary>Whether hints are shown.</summary>
    public bool ShowHints { get; init; } = true;

    /// <summary>Seed of the rules' random generator.</summary>
    public long Seed { get; init; } = 1;

    /// <summary>The player's name.</summary>
    public string PlayerName { get; init; } = "";

    /// <summary>Tribe colour 0..7, or -1 for the mission's own look.</summary>
    public int Colour { get; init; } = -1;

    /// <summary>The computer tribe's colour 0..7, or -1 for the mission's own look.</summary>
    public int ComputerColour { get; init; } = -1;

    /// <summary>Endless worlds: computer tribes at the start (any number).</summary>
    public int ComputerTribes { get; init; } = 1;

    /// <summary>Endless worlds: how often new computer tribes appear in new land (0 none .. 3 many).</summary>
    public int NewTribes { get; init; } = MultiTribeControl.SteadyNewTribes;

    /// <summary>Endless worlds: side in cells of the land made around the start (0: the rules' default).</summary>
    public int StartAreaSize { get; init; }

    /// <summary>Endless worlds: the lobby's settings (opening grace, start stock, wild animals); null for the endless rules' defaults.</summary>
    public WorldSettings? Settings { get; init; }
}

/// <summary>
/// A game played alone: the rules driven directly at the fixed tick rate. Orders run on the next
/// tick; while held (a menu, hint or help open) or paused, ticks still run the orders but the
/// world stands still, as in the original.
/// </summary>
public sealed class LocalMatchRunner : MatchRunner
{
    /// <summary>The rules' tick length at normal speed.</summary>
    public const int TickMilliseconds = 120;

    /// <summary>The local player's id in the player table.</summary>
    public static readonly Guid LocalId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>The computer's id in the player table.</summary>
    public static readonly Guid ComputerId = new("00000000-0000-0000-0000-000000000002");

    private const int MaxStepsPerUpdate = 8;
    private const double TickLength = TickMilliseconds / 1000.0;

    private readonly List<GameOrder> queued = [];
    private readonly LocalSink sink;
    private PlayerTable players;
    private double accumulated;
    private bool held;
    private bool paused;
    private int speed = 100;

    private LocalMatchRunner(MatchKind match, GameSimulation simulation, PlayerTable players, RunnerOptions options)
        : base(match, options)
    {
        Simulation = simulation;
        this.players = players;
        sink = new LocalSink(this);
        Follow();
        WarmUpSaves();
    }

    /// <inheritdoc />
    public override bool IsNetwork => false;

    /// <inheritdoc />
    public override bool IsHost => true;

    /// <inheritdoc />
    public override GameSimulation Simulation { get; }

    /// <inheritdoc />
    public override PlayerTable Players => players;

    /// <inheritdoc />
    public override Guid LocalPlayerId => LocalId;

    /// <inheritdoc />
    public override IOrderSink Orders => sink;

    /// <inheritdoc />
    public override bool IsPaused => held || paused;

    /// <inheritdoc />
    public override bool CanPause => true;

    /// <inheritdoc />
    public override bool CanChangeSpeed => true;

    /// <inheritdoc />
    public override int SpeedPercent => speed;

    /// <inheritdoc />
    public override double TickSeconds => TickLength * 100 / speed;

    /// <inheritdoc />
    public override double TickAlpha => Math.Clamp(accumulated / TickLength, 0, 1);

    /// <summary>A new game.</summary>
    /// <exception cref="ArgumentException">The mission does not exist or the kind is versus.</exception>
    public static LocalMatchRunner Start(GameContent content, LocalMatchSetup setup, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Match == MatchKind.Versus)
        {
            throw new ArgumentException("A versus match needs other players.", nameof(setup));
        }
        int mission = setup.Mission >= 0 ? setup.Mission : MissionStart.FirstCustomMission;
        if (!content.Missions.ContainsKey(mission) || setup.Difficulty is < 0 or > 1)
        {
            throw new ArgumentException($"Mission {mission} at difficulty {setup.Difficulty} does not exist.", nameof(setup));
        }
        if (setup.Match == MatchKind.Endless)
        {
            // An endless world: the player's tribe and the computer tribes, each with a camp; it never ends.
            GameSimulation endless = MultiTribeControl.NewEndlessWorld(content, setup.Seed, setup.Difficulty,
                Math.Max(0, setup.ComputerTribes), setup.ShowHints, setup.NewTribes, setup.StartAreaSize, setup.Settings);
            PlayerTable players = PlayerTable.Of(
                [new PlayerEntry { Id = LocalId, Name = setup.PlayerName, Slot = 0, Tribe = TribeRoles.Player(endless.World), Colour = setup.Colour }]);
            return new LocalMatchRunner(setup.Match, endless, LockstepMatch.WithWorldTribes(players, endless), options);
        }
        GameSimulation simulation = GameSimulation.StartMission(content, mission, setup.Difficulty, new MissionOptions
        {
            Seed = setup.Seed,
            TutorialFlags = (setup.Tutorial ? 1 : 0) | (setup.TutorialScenesSeen & ~1),
            HintsEnabled = setup.ShowHints,
            WithoutLimits = setup.Match == MatchKind.Custom,
        }, new SimulationOptions { HintsPauseGame = true });
        GameWorld world = simulation.World;
        PlayerTable table = PlayerTable.Of(
        [
            new PlayerEntry { Id = LocalId, Name = setup.PlayerName, Slot = 0, Tribe = TribeRoles.Player(world), Colour = setup.Colour },
            new PlayerEntry
            {
                Id = ComputerId, Name = "", Slot = 1, Tribe = TribeRoles.Computer(world), IsComputer = true, AiLevel = setup.Difficulty,
                Colour = setup.ComputerColour,
            },
        ]);
        return new LocalMatchRunner(setup.Match, simulation, table, options);
    }

    /// <summary>A game saved by <see cref="MatchRunner.Save"/>.</summary>
    /// <exception cref="SaveFileException">The file is not a usable single-player save.</exception>
    public static LocalMatchRunner Load(GameContent content, ReadOnlySpan<byte> file, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        (SaveHeader header, byte[] body) = MatchSaveFile.Read(file);
        if (header.Kind != SaveKind.SinglePlayer)
        {
            throw new SaveFileException(SaveProblem.NotASave, "This save is a hosted network match; resume it as the host.");
        }
        try
        {
            GameSimulation simulation = GameSimulation.Load(content, body);
            if (header.Match == MatchKind.Custom)
            {
                // A custom game saved before custom games had no limits goes on without them.
                simulation.LiftLimits();
            }
            return new LocalMatchRunner(header.Match, simulation, header.Players, options);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException or KeyNotFoundException)
        {
            bool sameBuild = header.GameVersion == options.GameVersion && header.ContentVersion == options.ContentVersion;
            throw new SaveFileException(sameBuild ? SaveProblem.Damaged : SaveProblem.OtherVersion,
                $"The saved world cannot be loaded ({header.GameVersion}, {header.ContentVersion}): {e.Message}", e);
        }
    }

    /// <inheritdoc />
    public override void Update(double deltaSeconds)
    {
        TakeSaveResults();
        if (Phase == MatchPhase.Stopped || deltaSeconds <= 0)
        {
            return;
        }
        accumulated += deltaSeconds * speed / 100;
        int steps = 0;
        while (accumulated >= TickLength && steps < MaxStepsPerUpdate)
        {
            accumulated -= TickLength;
            StepOnce();
            steps++;
        }
        if (accumulated >= TickLength)
        {
            // A long stall: drop the lost time instead of racing to catch up.
            accumulated = 0;
        }
    }

    /// <inheritdoc />
    public override void SetHeld(bool held) => this.held = held;

    /// <inheritdoc />
    public override RequestResult SetPaused(bool paused)
    {
        this.paused = paused;
        return RequestResult.Accepted;
    }

    /// <inheritdoc />
    public override RequestResult SetSpeed(int speedPercent)
    {
        if (!RoomLimits.IsValidSpeed(speedPercent))
        {
            return RequestResult.InvalidValue;
        }
        speed = speedPercent;
        return RequestResult.Accepted;
    }

    /// <inheritdoc />
    public override RequestResult Forfeit() => RequestResult.NotAllowedNow;

    /// <inheritdoc />
    public override void Quit()
    {
        if (Phase == MatchPhase.Stopped)
        {
            return;
        }
        SaveBeforeLeaving();
        StopWith(new MatchStop(StopReason.Quit, Simulation.Tick, "The player quit."));
    }

    /// <summary>Stops without saving: the game goes on in a network match built from this world.</summary>
    internal void HandOver() =>
        StopWith(new MatchStop(StopReason.HandedOver, Simulation.Tick, "The game goes on as a network match."));

    /// <summary>Runs one tick now with the queued orders.</summary>
    internal void StepOnce()
    {
        if (Phase == MatchPhase.Stopped)
        {
            return;
        }
        GameOrder[] orders = [.. queued];
        queued.Clear();
        bool hold = IsPaused;
        TickResult result;
        try
        {
            result = Simulation.Step(new TickInput
            {
                Orders = Array.ConvertAll(orders, PlayerOrder.From),
                Paused = hold,
                RosterOpen = RosterOpen,
            });
        }
#pragma warning disable CA1031 // A failure inside the rules must end the game with a message, not crash the client.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            Log($"SESSION failed at tick {Simulation.Tick}: {e}");
            StopWith(new MatchStop(StopReason.Error, Simulation.Tick, e.GetType().Name + ": " + e.Message));
            return;
        }
        IReadOnlyList<PlayerChange> changes = Follow();
        RaiseTick(new MatchTickEventArgs { Result = result, LocalOrders = orders, Changes = changes });
        if (!hold)
        {
            CountAutosaveTick();
        }
    }

    /// <inheritdoc />
    private protected override (SaveHeader Header, byte[] Body) BuildSave()
    {
        var header = new SaveHeader
        {
            Kind = SaveKind.SinglePlayer,
            Match = Match,
            GameVersion = Options.GameVersion,
            ContentVersion = Options.ContentVersion,
            Mission = Simulation.World.Mission,
            Difficulty = Simulation.World.Difficulty,
            Tick = Simulation.Tick,
            TickMilliseconds = TickMilliseconds,
            SavedAt = Options.UtcNow(),
            Title = Options.Title,
            Players = players,
            Settings = Simulation.IsMatch ? Simulation.World.Settings : null,
        };
        return (header, Simulation.Save());
    }

    /// <summary>Who went out and whether the game was decided, after a tick (or on load).</summary>
    private PlayerChange[] Follow()
    {
        if (!Simulation.IsMatch)
        {
            return Outcome is null && Simulation.IsOver ? DecideFromRules() : [];
        }
        var changes = new List<PlayerChange>();
        players = LockstepMatch.WithWorldTribes(players, Simulation);
        var defeated = new List<PlayerEntry>();
        foreach (PlayerEntry entry in players)
        {
            if (entry is { Tribe: >= 0, State: PlayerState.Playing or PlayerState.Absent } && Simulation.IsDefeated(entry.Tribe))
            {
                PlayerEntry changed = entry with { State = PlayerState.Out };
                defeated.Add(changed);
                changes.Add(new PlayerChange(Math.Max(0, Simulation.Tick - 1), PlayerChangeKind.Out, changed));
            }
        }
        players = players.WithAll(defeated);
        if (Outcome is null && Simulation.Match is { IsDecided: true } result)
        {
            Decide(new MatchOutcome([.. result.Winners]), players.ById(LocalId)?.Tribe ?? -1);
        }
        return [.. changes];
    }

    private PlayerChange[] DecideFromRules()
    {
        GameWorld world = Simulation.World;
        bool won = Simulation.Victory == true;
        int loser = won ? TribeRoles.Computer(world) : TribeRoles.Player(world);
        var changes = new List<PlayerChange>();
        if (players.ByTribe(loser) is { State: not PlayerState.Out } entry)
        {
            PlayerEntry changed = entry with { State = PlayerState.Out };
            players = players.With(changed);
            changes.Add(new PlayerChange(Simulation.Tick - 1, PlayerChangeKind.Out, changed));
        }
        int winner = won ? TribeRoles.Player(world) : TribeRoles.Computer(world);
        Decide(new MatchOutcome([winner]), TribeRoles.Player(world));
        return [.. changes];
    }

    private sealed class LocalSink(LocalMatchRunner runner) : IOrderSink
    {
        public bool CanSubmit => runner.Phase != MatchPhase.Stopped && runner.LocalTribe >= 0;

        public int Tribe => runner.LocalTribe;

        public OrderSubmitResult Submit(GameOrder order)
        {
            ArgumentNullException.ThrowIfNull(order);
            if (!CanSubmit)
            {
                return OrderSubmitResult.NotAPlayer;
            }
            if (order.Tribe != Tribe)
            {
                return OrderSubmitResult.WrongTribe;
            }
            runner.queued.Add(order);
            return OrderSubmitResult.Queued;
        }
    }
}
