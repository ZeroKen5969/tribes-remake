using System;
using System.Collections.Generic;
using Tribes.Net.Protocol;

namespace Tribes.Net;

public sealed partial class ClientSession
{
    private void Handle(NetMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome:
                OnWelcome(welcome);
                break;
            case RejectMessage reject:
                Fail(SessionEndReason.Rejected, reject.Message, reject.Reason);
                break;
            case RoomUpdateMessage update:
                extras = update.Extras;
                SetRoom(update.Room);
                break;
            case HostLeavingMessage leaving:
                designatedSuccessor = leaving.SuccessorId;
                break;
            case ChatRelayMessage chat:
                ChatReceived?.Invoke(this, new ChatReceivedEventArgs { SenderId = chat.SenderId, SenderName = chat.SenderName, Text = chat.Text });
                break;
            case RequestDeniedMessage denied:
                RequestDenied?.Invoke(this, new RequestDeniedEventArgs { Request = denied.Request, Result = denied.Result, Message = denied.Message });
                break;
            case MatchBeginMessage begin:
                OnMatchBegin(begin);
                break;
            case SnapshotBeginMessage header:
                OnSnapshotBegin(header);
                break;
            case SnapshotChunkMessage chunk:
                OnSnapshotChunk(chunk);
                break;
            case BatchMessage batch:
                OnBatch(batch.Batch);
                break;
            case ControlStateMessage control:
                ApplyControl(control.Paused, control.SpeedPercent);
                break;
            case DesyncNoticeMessage notice:
                desynced = true;
                DesyncDetected?.Invoke(this, new DesyncDetectedEventArgs
                {
                    Tick = notice.Tick,
                    LastMatchingTick = notice.LastMatchingTick,
                    PlayerId = notice.PlayerId,
                    ExpectedHash = notice.ExpectedHash,
                    ReportedHash = notice.ReportedHash,
                });
                break;
            case SessionEndMessage end:
                Fail(end.Reason, end.Message);
                break;
            default:
                throw new ProtocolException($"A host may not send {message.Type} messages.");
        }
    }

    private void OnWelcome(WelcomeMessage welcome)
    {
        if (Phase is not (ClientPhase.Connecting or ClientPhase.Reconnecting or ClientPhase.Migrating))
        {
            throw new ProtocolException("The host welcomed a client that had already joined.");
        }

        bool migrated = Phase == ClientPhase.Migrating;
        reconnectsLeft = options.ReconnectAttempts;
        extras = welcome.Extras;
        nextSequence = Math.Max(nextSequence, welcome.LastSequence + 1);
        SetRoom(welcome.Room);
        if (welcome.Room.Phase != RoomPhase.InGame)
        {
            DiscardMatch();
            sentCommands.Clear();
            SetPhase(ClientPhase.Lobby);
            return;
        }

        if (welcome.ResumeTick < 0)
        {
            DiscardMatch();
            SetPhase(ClientPhase.Loading);
        }
        else
        {
            ResumeFromHistory(welcome);
        }

        FinishMigration(migrated);
    }

    /// <summary>The host sends the batches this client missed; it keeps its simulation.</summary>
    private void ResumeFromHistory(WelcomeMessage welcome)
    {
        if (engine is null || engine.Simulation.Tick != welcome.ResumeTick)
        {
            throw new ProtocolException($"The host resumes from tick {welcome.ResumeTick}, which this client is not at.");
        }

        engine.ClearBuffered();
        receivedBatches.DropFrom(engine.Simulation.Tick);
        timelineId = welcome.TimelineId;
        pacer?.Reset(clock.Now);
        ResetBacklogWindow(clock.Now);
        SetPhase(ClientPhase.InGame);
        ResendPendingCommands();
    }

    private void OnMatchBegin(MatchBeginMessage begin)
    {
        if (Phase != ClientPhase.Lobby)
        {
            throw new ProtocolException("A match began while this client was not in the lobby.");
        }

        ILockstepSimulation simulation = BuildSimulation(begin.Start, snapshot: null, tick: 0);
        sentCommands.Clear();
        receivedBatches.Clear();
        ledger = new MatchLedger(begin.Start.Players);
        BeginMatch(begin.Start, simulation, begin.SpeedPercent, paused: false, fromSnapshot: false, begin.TimelineId);
    }

    private void OnSnapshotBegin(SnapshotBeginMessage header)
    {
        if (Phase is not (ClientPhase.Loading or ClientPhase.InGame))
        {
            throw new ProtocolException("A snapshot arrived outside a match.");
        }

        assembler = new SnapshotAssembler(header, options.Lockstep.MaxSnapshotBytes);
        engine = null;
        earlyBatches.Clear();
        receivedBatches.Clear();
        SetPhase(ClientPhase.Loading);
    }

    private void OnSnapshotChunk(SnapshotChunkMessage chunk)
    {
        if (assembler is null)
        {
            throw new ProtocolException("A snapshot chunk arrived without its header.");
        }

        if (!assembler.Add(chunk))
        {
            return;
        }

        SnapshotBeginMessage header = assembler.Header;
        byte[] raw = assembler.Complete();
        assembler = null;
        ILockstepSimulation simulation = BuildSimulation(header.Start, raw, header.Tick);
        ledger = MatchLedger.From(header.Ledger);
        BeginMatch(header.Start, simulation, header.SpeedPercent, header.Paused, fromSnapshot: true, header.TimelineId);
        foreach (TickBatch batch in earlyBatches)
        {
            engine!.Enqueue(batch);
        }

        earlyBatches.Clear();

        // The snapshot's ledger says which of this player's commands have run; the rest go again.
        ResendPendingCommands();
    }

    private void OnBatch(TickBatch batch)
    {
        if (Phase == ClientPhase.Loading)
        {
            if (earlyBatches.Count >= MaxEarlyBatches)
            {
                throw new ProtocolException("The host sent too many batches before the snapshot was complete.");
            }

            earlyBatches.Add(batch);
            return;
        }

        if (engine is null)
        {
            throw new ProtocolException("A tick batch arrived outside a match.");
        }

        engine.Enqueue(batch);
    }

    private void ApplyControl(bool paused, int speedPercent)
    {
        IsPaused = paused;
        SpeedPercent = speedPercent;
        pacer?.SetSpeed(speedPercent);
        ControlStateChanged?.Invoke(this, new ControlStateChangedEventArgs { Paused = paused, SpeedPercent = speedPercent });
    }

    /// <summary>
    /// Builds the simulation from the host's start data and, for a running match, its snapshot.
    /// Both come from the network, so whatever the game throws on them ends the session as a
    /// protocol error instead of escaping <see cref="Update"/>.
    /// </summary>
    private ILockstepSimulation BuildSimulation(MatchStart start, byte[]? snapshot, long tick)
    {
        ILockstepSimulation? simulation;
        try
        {
            simulation = simulationFactory(start);
            if (simulation is not null && snapshot is not null)
            {
                simulation.LoadSnapshot(snapshot);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ProtocolException($"The game could not build the match from the host's data: {ex.Message}", ex);
        }

        if (simulation is null)
        {
            throw new InvalidOperationException("The simulation factory returned null.");
        }

        if (snapshot is not null && simulation.Tick != tick)
        {
            throw new ProtocolException($"After loading the snapshot of tick {tick} the simulation reports tick {simulation.Tick}.");
        }

        return simulation;
    }

    private void BeginMatch(MatchStart start, ILockstepSimulation simulation, int speedPercent, bool paused, bool fromSnapshot, Guid timeline)
    {
        Match = start;
        timelineId = timeline;
        engine = new LockstepEngine(simulation);
        pacer = new TickPacer(start.TickMilliseconds);
        pacer.SetSpeed(speedPercent);
        pacer.Reset(clock.Now);
        ResetBacklogWindow(clock.Now);
        SpeedPercent = speedPercent;
        IsPaused = paused;
        desynced = false;
        SetPhase(ClientPhase.InGame);
        MatchStarted?.Invoke(this, new MatchStartedEventArgs { Start = start, Simulation = simulation, FromSnapshot = fromSnapshot });
    }

    private void DiscardMatch()
    {
        Match = null;
        engine = null;
        pacer = null;
        assembler = null;
        ledger = null;
        earlyBatches.Clear();
        receivedBatches.Clear();
        ownHashes = new HashLedger(HashMemory);
        desynced = false;
    }

    /// <summary>
    /// Runs the ticks due at the normal rate, plus extra ones while more batches wait than the
    /// catch-up threshold; never more than the per-update limit.
    /// </summary>
    private void RunSimulation(TimeSpan now)
    {
        if (engine is null || pacer is null)
        {
            return;
        }

        if (connection is not null)
        {
            inputDelay.Update(now, connection.RoundTripTime, connection.RoundTripVariance, pacer.Interval);
        }

        LockstepOptions lockstep = options.Lockstep;
        int due = pacer.Advance(now, lockstep.MaxCatchUpStepsPerUpdate);
        int backlog = Math.Max(0, engine.Buffered - lockstep.CatchUpThresholdTicks);
        int budget = Math.Min(due + backlog, lockstep.MaxCatchUpStepsPerUpdate);
        LockstepEngine current = engine;
        int stepped = 0;
        while (stepped < budget && ReferenceEquals(engine, current) && Phase == ClientPhase.InGame && current.TryStep(out TickBatch? batch))
        {
            stepped++;
            AfterStep(current, batch);
        }

        if (stepped < due)
        {
            pacer.CreditOne();
        }

        DrainStandingBacklog(now, current);
    }

    /// <summary>
    /// A batch that waited through every update for a whole second is delay the network no longer
    /// needs (a burst delivered it early); one extra tick removes it. Jitter that still needs the
    /// buffer empties it now and then, which stops the draining.
    /// </summary>
    private void DrainStandingBacklog(TimeSpan now, LockstepEngine current)
    {
        standingBacklog = Math.Min(standingBacklog, current.Buffered);
        if (now - backlogWindowStart < BacklogWindow)
        {
            return;
        }

        bool drain = standingBacklog > 0;
        ResetBacklogWindow(now);
        if (drain && ReferenceEquals(engine, current) && Phase == ClientPhase.InGame && current.TryStep(out TickBatch? batch))
        {
            AfterStep(current, batch);
        }
    }

    private void ResetBacklogWindow(TimeSpan now)
    {
        standingBacklog = int.MaxValue;
        backlogWindowStart = now;
    }

    private void AfterStep(LockstepEngine current, TickBatch batch)
    {
        ledger?.Apply(batch);
        SessionShared.RaisePresenceChanges(this, batch, Room, PlayerPresenceChanged);
        TickStepped?.Invoke(this, new TickSteppedEventArgs { Tick = batch.Tick, Commands = batch.Commands });
        if (Match is { } start && LockstepEngine.IsHashTick(batch.Tick, start.HashIntervalTicks))
        {
            uint hash = current.Simulation.StateHash();
            ownHashes.Record(batch.Tick, hash);
            Send(new StateHashMessage(batch.Tick, hash));
        }
    }

    /// <summary>The tick a command sent now aims at: the input delay past the newest batch.</summary>
    private long CommandTarget() => (engine?.LatestTick ?? 0) + 1 + inputDelay.Current;

    /// <summary>
    /// This player's recent commands that have not run as far as the ledger knows. A command that
    /// ran in batches a new host never had counts as not run once a snapshot rewinds the ledger,
    /// which is why sent commands are kept (the most recent <see cref="MaxPendingCommands"/>)
    /// rather than dropped when they first run.
    /// </summary>
    private List<(long Sequence, byte[] Payload)> UnrunCommands()
    {
        long done = ledger is not null && LocalPlayer is { HasSlot: true } self ? ledger.SequenceOf(self.Slot) : 0;
        return sentCommands.FindAll(c => c.Sequence > done);
    }

    /// <summary>
    /// Sends again the commands that have not run, after a reconnect or a host change. The host
    /// drops numbers it already has, so none runs twice.
    /// </summary>
    private void ResendPendingCommands()
    {
        if (!CanSendCommands)
        {
            return;
        }

        foreach ((long sequence, byte[] payload) in UnrunCommands())
        {
            Send(new CommandMessage(CommandTarget(), sequence, payload));
        }
    }
}
