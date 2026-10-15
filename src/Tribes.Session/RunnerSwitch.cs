using System;
using Tribes.Core.Simulation;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>
/// One runner the match screen keeps while the match moves to another runner underneath (a game
/// played alone opened to the network). It forwards everything to <see cref="Current"/>; after a
/// switch it raises <see cref="SimulationReplaced"/>, <see cref="PlayersChanged"/> and
/// <see cref="PhaseChanged"/>, and the old runner's stop is not passed on.
/// </summary>
public sealed class RunnerSwitch : IMatchRunner
{
    private IMatchRunner current;

    /// <summary>Starts with <paramref name="first"/>.</summary>
    public RunnerSwitch(IMatchRunner first)
    {
        ArgumentNullException.ThrowIfNull(first);
        current = first;
        Hook(first);
    }

    /// <summary>The runner the match runs in now.</summary>
    public IMatchRunner Current => current;

    /// <inheritdoc />
    public MatchKind Match => current.Match;

    /// <inheritdoc />
    public bool IsNetwork => current.IsNetwork;

    /// <inheritdoc />
    public bool IsHost => current.IsHost;

    /// <inheritdoc />
    public MatchPhase Phase => current.Phase;

    /// <inheritdoc />
    public GameSimulation Simulation => current.Simulation;

    /// <inheritdoc />
    public PlayerTable Players => current.Players;

    /// <inheritdoc />
    public Guid LocalPlayerId => current.LocalPlayerId;

    /// <inheritdoc />
    public int LocalTribe => current.LocalTribe;

    /// <inheritdoc />
    public int ViewTribe => current.ViewTribe;

    /// <inheritdoc />
    public LocalRole Role => current.Role;

    /// <inheritdoc />
    public IOrderSink Orders => current.Orders;

    /// <inheritdoc />
    public MatchOutcome? Outcome => current.Outcome;

    /// <inheritdoc />
    public MatchStop? StopInfo => current.StopInfo;

    /// <inheritdoc />
    public bool IsPaused => current.IsPaused;

    /// <inheritdoc />
    public bool CanPause => current.CanPause;

    /// <inheritdoc />
    public bool CanChangeSpeed => current.CanChangeSpeed;

    /// <inheritdoc />
    public int SpeedPercent => current.SpeedPercent;

    /// <inheritdoc />
    public double TickSeconds => current.TickSeconds;

    /// <inheritdoc />
    public double TickAlpha => current.TickAlpha;

    /// <inheritdoc />
    public bool RosterOpen
    {
        get => current.RosterOpen;
        set => current.RosterOpen = value;
    }

    /// <inheritdoc />
    public event EventHandler<MatchTickEventArgs>? TickRan;

    /// <inheritdoc />
    public event EventHandler? PlayersChanged;

    /// <inheritdoc />
    public event EventHandler<MatchNoticeEventArgs>? Notice;

    /// <inheritdoc />
    public event EventHandler<MatchOverEventArgs>? MatchOver;

    /// <inheritdoc />
    public event EventHandler<MatchStoppedEventArgs>? Stopped;

    /// <inheritdoc />
    public event EventHandler? PhaseChanged;

    /// <inheritdoc />
    public event EventHandler? SimulationReplaced;

    /// <summary>
    /// Moves the match to the runner <paramref name="open"/> makes from the current one (which it
    /// stops). When <paramref name="open"/> throws, the current runner stays.
    /// </summary>
    public IMatchRunner SwitchTo(Func<IMatchRunner, IMatchRunner> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        IMatchRunner old = current;
        Unhook(old);
        IMatchRunner next;
        try
        {
            next = open(old);
        }
        catch
        {
            Hook(old);
            throw;
        }
        current = next;
        Hook(next);
        if (!ReferenceEquals(next, old))
        {
            old.Dispose();
        }
        SimulationReplaced?.Invoke(this, EventArgs.Empty);
        PlayersChanged?.Invoke(this, EventArgs.Empty);
        PhaseChanged?.Invoke(this, EventArgs.Empty);
        return next;
    }

    /// <inheritdoc />
    public void Update(double deltaSeconds) => current.Update(deltaSeconds);

    /// <inheritdoc />
    public void SetHeld(bool held) => current.SetHeld(held);

    /// <inheritdoc />
    public RequestResult SetPaused(bool paused) => current.SetPaused(paused);

    /// <inheritdoc />
    public RequestResult SetSpeed(int speedPercent) => current.SetSpeed(speedPercent);

    /// <inheritdoc />
    public RequestResult Forfeit() => current.Forfeit();

    /// <inheritdoc />
    public SaveSlot? Save(string slot) => current.Save(slot);

    /// <inheritdoc />
    public bool AttachPicture(string slot, Pixels picture) => current.AttachPicture(slot, picture);

    /// <inheritdoc />
    public int PendingSaves => current.PendingSaves;

    /// <inheritdoc />
    public bool FlushSaves() => current.FlushSaves();

    /// <inheritdoc />
    public void Quit() => current.Quit();

    /// <inheritdoc />
    public void Dispose() => current.Dispose();

    private void Hook(IMatchRunner runner)
    {
        runner.TickRan += OnTick;
        runner.PlayersChanged += OnPlayers;
        runner.Notice += OnNotice;
        runner.MatchOver += OnOver;
        runner.Stopped += OnStopped;
        runner.PhaseChanged += OnPhase;
        runner.SimulationReplaced += OnReplaced;
    }

    private void Unhook(IMatchRunner runner)
    {
        runner.TickRan -= OnTick;
        runner.PlayersChanged -= OnPlayers;
        runner.Notice -= OnNotice;
        runner.MatchOver -= OnOver;
        runner.Stopped -= OnStopped;
        runner.PhaseChanged -= OnPhase;
        runner.SimulationReplaced -= OnReplaced;
    }

    private void OnTick(object? sender, MatchTickEventArgs e) => TickRan?.Invoke(this, e);

    private void OnPlayers(object? sender, EventArgs e) => PlayersChanged?.Invoke(this, e);

    private void OnNotice(object? sender, MatchNoticeEventArgs e) => Notice?.Invoke(this, e);

    private void OnOver(object? sender, MatchOverEventArgs e) => MatchOver?.Invoke(this, e);

    private void OnStopped(object? sender, MatchStoppedEventArgs e) => Stopped?.Invoke(this, e);

    private void OnPhase(object? sender, EventArgs e) => PhaseChanged?.Invoke(this, e);

    private void OnReplaced(object? sender, EventArgs e) => SimulationReplaced?.Invoke(this, e);
}
