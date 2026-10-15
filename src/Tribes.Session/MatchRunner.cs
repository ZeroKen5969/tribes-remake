using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Tribes.Core.Simulation;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>What both runners share: events, phase, stopping, saves and autosaves.</summary>
public abstract class MatchRunner : IMatchRunner
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Width of save pictures: larger ones are halved down to it.</summary>
    private const int PictureWidth = 160;

    private readonly List<Action> held = [];
    private readonly Dictionary<string, SaveJob> lastQueued = new(StringComparer.Ordinal);
    private SaveWriter? writer;
    private long saveSequence;
    private int ticksSinceAutosave;
    private int holding;
    private bool disposed;

    /// <summary>Creates the runner.</summary>
    private protected MatchRunner(MatchKind match, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Match = match;
        Options = options;
    }

    /// <summary>The runner's settings.</summary>
    protected RunnerOptions Options { get; }

    /// <inheritdoc />
    public MatchKind Match { get; }

    /// <inheritdoc />
    public abstract bool IsNetwork { get; }

    /// <inheritdoc />
    public abstract bool IsHost { get; }

    /// <inheritdoc />
    public MatchPhase Phase { get; private set; } = MatchPhase.Running;

    /// <inheritdoc />
    public abstract GameSimulation Simulation { get; }

    /// <inheritdoc />
    public abstract PlayerTable Players { get; }

    /// <inheritdoc />
    public abstract Guid LocalPlayerId { get; }

    /// <inheritdoc />
    public int LocalTribe => Role == LocalRole.Player ? Players.ById(LocalPlayerId)?.Tribe ?? -1 : -1;

    /// <inheritdoc />
    public int ViewTribe
    {
        get
        {
            if (Players.ById(LocalPlayerId) is { Tribe: >= 0 } own)
            {
                return own.Tribe;
            }
            return Math.Max(0, Players.FirstHumanTribe);
        }
    }

    /// <inheritdoc />
    public LocalRole Role => Players.ById(LocalPlayerId) switch
    {
        { State: PlayerState.Forfeited } => LocalRole.Forfeited,
        { State: PlayerState.Out } => LocalRole.Out,
        null or { HasSlot: false } or { Tribe: < 0 } => LocalRole.Spectator,
        _ => LocalRole.Player,
    };

    /// <inheritdoc />
    public abstract IOrderSink Orders { get; }

    /// <inheritdoc />
    public MatchOutcome? Outcome { get; private set; }

    /// <inheritdoc />
    public MatchStop? StopInfo { get; private set; }

    /// <inheritdoc />
    public abstract bool IsPaused { get; }

    /// <inheritdoc />
    public abstract bool CanPause { get; }

    /// <inheritdoc />
    public abstract bool CanChangeSpeed { get; }

    /// <inheritdoc />
    public abstract int SpeedPercent { get; }

    /// <inheritdoc />
    public abstract double TickSeconds { get; }

    /// <inheritdoc />
    public abstract double TickAlpha { get; }

    /// <inheritdoc />
    public virtual bool RosterOpen { get; set; }

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

    /// <inheritdoc />
    public abstract void Update(double deltaSeconds);

    /// <inheritdoc />
    public virtual void SetHeld(bool held)
    {
    }

    /// <inheritdoc />
    public abstract RequestResult SetPaused(bool paused);

    /// <inheritdoc />
    public abstract RequestResult SetSpeed(int speedPercent);

    /// <inheritdoc />
    public abstract RequestResult Forfeit();

    /// <inheritdoc />
    public SaveSlot? Save(string slot) => Queue(slot, SaveReason.Manual);

    /// <inheritdoc />
    public int PendingSaves => writer?.Pending ?? 0;

    /// <inheritdoc />
    public bool FlushSaves()
    {
        if (writer is null)
        {
            return true;
        }
        bool written = writer.Wait(FlushTimeout);
        return TakeSaveResults() && written;
    }

    /// <inheritdoc />
    public bool AttachPicture(string slot, Pixels picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        picture.Check();
        if (writer is null || !SaveSlot.IsValidSlot(slot))
        {
            return false;
        }
        foreach (SaveJob job in lastQueued.Values)
        {
            if (job.Slot.Slot == slot)
            {
                writer.EnqueuePicture(job, picture, PictureWidth);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Takes the state for a save now (the caller is at a tick boundary) and queues the writing.
    /// Returns where it goes, or null when this device cannot save or taking the state failed.
    /// </summary>
    private SaveSlot? Queue(string slot, SaveReason reason)
    {
        if (!SaveSlot.IsValidSlot(slot))
        {
            throw new ArgumentException($"'{slot}' is not a save slot.", nameof(slot));
        }
        if (Options.Saves is null || !IsHost)
        {
            return null;
        }
        try
        {
            long started = Stopwatch.GetTimestamp();
            (SaveHeader header, byte[] body) = BuildSave();
            var target = new SaveSlot(header, slot);
            writer ??= new SaveWriter(Options.Saves, Options.WriteInBackground);
            var job = new SaveJob(target, body, reason, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ++saveSequence);
            writer.Enqueue(job);
            if (reason != SaveReason.WarmUp)
            {
                lastQueued[target.Stem] = job;
            }
            if (slot == SaveSlot.Auto)
            {
                ticksSinceAutosave = 0;
            }
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log($"SESSION save {slot} failed: {e.Message}");
            RaiseNotice(new MatchNotice(MatchNoticeKind.SaveFailed, Simulation.Tick, null, e.Message));
            return null;
        }
    }

    /// <summary>
    /// Takes the state for a save once and throws it away, while the match opens: the first save
    /// of a run is many times slower (code compiled on first use, most on Android), and this keeps
    /// that cost out of the first autosave.
    /// </summary>
    private protected void WarmUpSaves()
    {
        if (Options.Saves is null || !IsHost || Options.AutosaveTicks <= 0)
        {
            return;
        }
        // The whole path once, as a dry run: nothing is written and nobody is told.
        if (Queue(SaveSlot.Auto, SaveReason.WarmUp) is { } slot && writer is not null)
        {
            var blank = new Pixels(PictureWidth * 2, PictureWidth * 9 / 8, 4, new byte[PictureWidth * 2 * (PictureWidth * 9 / 8) * 4]);
            writer.EnqueuePicture(new SaveJob(slot, [], SaveReason.WarmUp, 0, 0), blank, PictureWidth);
        }
    }

    /// <summary>Tells the players about the saves written since the last call; false when one failed.</summary>
    private protected bool TakeSaveResults()
    {
        bool allWritten = true;
        while (writer is not null && writer.TryTakeMessage(out string? message) && message is not null)
        {
            Log(message);
        }
        while (writer is not null && writer.TryTake(out SaveResult? result) && result is not null)
        {
            SaveJob job = result.Job;
            if (result.Error is { } error)
            {
                allWritten = false;
                Log($"SESSION save {job.Slot.Slot} failed: {error.Message}");
                RaiseNotice(new MatchNotice(MatchNoticeKind.SaveFailed, Simulation.Tick, null, error.Message));
                continue;
            }
            if (job.Reason == SaveReason.WarmUp)
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"SESSION_SAVE_WARMUP capture={job.CaptureMs:0.00} assemble={result.AssembleMs:0.00} (dry run)"));
                continue;
            }
            Log(string.Create(CultureInfo.InvariantCulture,
                $"SESSION_SAVE_TIMING slot={job.Slot.Slot} bytes={result.Bytes} capture={job.CaptureMs:0.00} assemble={result.AssembleMs:0.00} write={result.WriteMs:0.00} (assemble and write in the background)"));
            Log($"SESSION saved {job.Slot.Stem} tick={job.Slot.Header.Tick}");
            MatchNoticeKind? notice = job.Reason switch
            {
                SaveReason.Autosave => MatchNoticeKind.Autosaved,
                SaveReason.Manual => MatchNoticeKind.Saved,
                _ => null,
            };
            if (notice is { } kind)
            {
                RaiseNotice(new MatchNotice(kind, job.Slot.Header.Tick));
            }
        }
        return allWritten;
    }

    /// <inheritdoc />
    public abstract void Quit();

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Quits the match if it still runs (writing the last saves), then releases the session.</summary>
    /// <param name="disposing">False when called from a finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed || !disposing)
        {
            return;
        }
        disposed = true;
        if (Phase != MatchPhase.Stopped)
        {
            Quit();
        }
        FlushSaves();
        DisposeSession();
    }

    /// <summary>The header and body of a save of the match now.</summary>
    private protected abstract (SaveHeader Header, byte[] Body) BuildSave();

    /// <summary>Releases what the runner holds once it stopped.</summary>
    private protected virtual void DisposeSession()
    {
    }

    /// <summary>Writes a log line.</summary>
    private protected void Log(string line) => Options.Log?.Invoke(line);

    /// <summary>Counts a tick of play and autosaves when it is time.</summary>
    private protected void CountAutosaveTick()
    {
        if (Options.AutosaveTicks <= 0 || !IsHost || Phase is MatchPhase.Stopped or MatchPhase.Over)
        {
            return;
        }
        if (++ticksSinceAutosave >= Options.AutosaveTicks)
        {
            ticksSinceAutosave = 0;
            RequestAutosave();
        }
    }

    /// <summary>An autosave is due; saves at once unless the runner must wait for a calmer moment.</summary>
    private protected virtual void RequestAutosave() => Autosave();

    /// <summary>Saves to "auto"; the players hear of it once it is written.</summary>
    private protected void Autosave()
    {
        if (Phase is not (MatchPhase.Stopped or MatchPhase.Over))
        {
            Queue(SaveSlot.Auto, SaveReason.Autosave);
        }
    }

    /// <summary>Saves to "auto" before quitting or stopping, when this device keeps the records, and waits until it is written.</summary>
    private protected void SaveBeforeLeaving()
    {
        if (IsHost && Phase != MatchPhase.Over && Queue(SaveSlot.Auto, SaveReason.Leaving) is not null)
        {
            FlushSaves();
        }
    }

    /// <summary>Moves to <paramref name="phase"/>.</summary>
    private protected void SetPhase(MatchPhase phase)
    {
        if (Phase == phase || Phase == MatchPhase.Stopped)
        {
            return;
        }
        Phase = phase;
        Emit(() => PhaseChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Stops the runner for good (the first stop wins).</summary>
    private protected void StopWith(MatchStop stop)
    {
        if (Phase == MatchPhase.Stopped)
        {
            return;
        }
        StopInfo = stop;
        Log($"SESSION stopped {stop.Reason} tick={stop.Tick}: {stop.Message}");
        SetPhase(MatchPhase.Stopped);
        Emit(() => Stopped?.Invoke(this, new MatchStoppedEventArgs { Stop = stop }));
    }

    /// <summary>Records the result and tells the screen.</summary>
    private protected void Decide(MatchOutcome outcome, int localTribe)
    {
        if (Outcome is not null || Phase == MatchPhase.Stopped)
        {
            return;
        }
        Outcome = outcome;
        Log($"SESSION over tick={Simulation.Tick} winners=[{string.Join(",", outcome.WinningTribes)}]");
        SetPhase(MatchPhase.Over);
        Emit(() => MatchOver?.Invoke(this, new MatchOverEventArgs { Outcome = outcome, LocalVictory = outcome.Won(localTribe) }));
    }

    /// <summary>
    /// From now until <see cref="ReleaseEvents"/>, events wait: handlers never run inside a
    /// network session's own update, so they may call anything (quit, save, dispose) safely.
    /// </summary>
    private protected void HoldEvents() => holding++;

    /// <summary>Raises the events held since <see cref="HoldEvents"/>, in order.</summary>
    private protected void ReleaseEvents()
    {
        if (--holding > 0)
        {
            return;
        }
        while (held.Count > 0)
        {
            Action[] batch = [.. held];
            held.Clear();
            foreach (Action raise in batch)
            {
                raise();
            }
        }
    }

    private void Emit(Action raise)
    {
        if (holding > 0)
        {
            held.Add(raise);
        }
        else
        {
            raise();
        }
    }

    /// <summary>Raises <see cref="TickRan"/>.</summary>
    private protected void RaiseTick(MatchTickEventArgs tick) => Emit(() => TickRan?.Invoke(this, tick));

    /// <summary>Raises <see cref="PlayersChanged"/>.</summary>
    private protected void RaisePlayersChanged() => Emit(() => PlayersChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>Raises <see cref="Notice"/>.</summary>
    private protected void RaiseNotice(MatchNotice notice) => Emit(() => Notice?.Invoke(this, new MatchNoticeEventArgs { Notice = notice }));

    /// <summary>Raises <see cref="SimulationReplaced"/>.</summary>
    private protected void RaiseSimulationReplaced() => Emit(() => SimulationReplaced?.Invoke(this, EventArgs.Empty));

    /// <summary>Notices for player table changes.</summary>
    private protected void RaiseChangeNotices(IReadOnlyList<PlayerChange> changes)
    {
        foreach (PlayerChange change in changes)
        {
            MatchNoticeKind kind = change.Kind switch
            {
                PlayerChangeKind.Joined => MatchNoticeKind.PlayerJoined,
                PlayerChangeKind.Absent => MatchNoticeKind.PlayerAbsent,
                PlayerChangeKind.Returned => MatchNoticeKind.PlayerReturned,
                PlayerChangeKind.Forfeited => MatchNoticeKind.PlayerForfeited,
                _ => MatchNoticeKind.PlayerOut,
            };
            RaiseNotice(new MatchNotice(kind, change.Tick, change.Player));
        }
        if (changes.Count > 0)
        {
            RaisePlayersChanged();
        }
    }
}
