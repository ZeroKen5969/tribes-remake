using System;
using System.Collections.Generic;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net;

public sealed partial class HostSession
{
    /// <summary>
    /// Continues a saved match: the room, the roster and the simulation come from the save. The
    /// match starts paused; other players are absent (their tribes get no orders) until they
    /// connect again with their persistent ids. Call <see cref="Resume"/> to continue.
    /// </summary>
    /// <param name="transport">A transport that is neither hosting nor connected.</param>
    /// <param name="options">The same versions and host player id as when the match was saved.</param>
    /// <param name="save">Bytes from <see cref="SaveMatch"/>.</param>
    /// <param name="simulationFactory">Builds the simulation the snapshot is loaded into.</param>
    /// <param name="clock">Time source; null for the real clock.</param>
    /// <exception cref="ProtocolException">The save is damaged or not a match save.</exception>
    /// <exception cref="ArgumentException">The save belongs to other versions or another host.</exception>
    /// <exception cref="InvalidOperationException">The simulation did not load the snapshot at the saved tick.</exception>
    public static HostSession ResumeFromSave(
        ITransport transport,
        HostOptions options,
        ReadOnlySpan<byte> save,
        Func<MatchStart, ILockstepSimulation> simulationFactory,
        IClock? clock = null)
    {
        HostOptions checkedOptions = Validated(options);
        SavedMatch saved = MatchSaveCodec.Read(save);
        if (!string.Equals(saved.GameVersion, options.GameVersion, StringComparison.Ordinal)
            || !string.Equals(saved.ContentVersion, options.ContentVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The save was made with game version '{saved.GameVersion}' and content '{saved.ContentVersion}'; this build has '{options.GameVersion}' and '{options.ContentVersion}'.",
                nameof(save));
        }

        Guid hostId = SessionShared.PublicId(options.PlayerId, saved.Room.RoomId);
        if (saved.Room.FindPlayer(hostId) is not { IsHost: true })
        {
            throw new ArgumentException("Only the player who hosted the match can resume it.", nameof(options));
        }

        ArgumentNullException.ThrowIfNull(simulationFactory);
        HostRoom room = HostRoom.Restore(saved.Room, saved.Banned, hostId, out List<int> absentSlots);
        HostMatch restored = RestoreMatch(saved, absentSlots, simulationFactory, checkedOptions.Lockstep);
        var session = new HostSession(transport, checkedOptions, simulationFactory, clock, room) { match = restored };
        session.StartHosting();
        return session;
    }

    /// <summary>Reads the summary of a save without resuming it.</summary>
    /// <param name="save">Bytes from <see cref="SaveMatch"/>.</param>
    /// <exception cref="ProtocolException">The save is damaged or not a match save.</exception>
    public static SavedMatchInfo ReadSaveInfo(ReadOnlySpan<byte> save)
    {
        SavedMatch saved = MatchSaveCodec.Read(save);
        return new SavedMatchInfo
        {
            MatchId = saved.Start.MatchId,
            Room = saved.Room,
            Tick = saved.Tick,
            GameVersion = saved.GameVersion,
            ContentVersion = saved.ContentVersion,
        };
    }

    /// <summary>
    /// Starts the match: builds the simulation with <paramref name="gameSetup"/> and sends the
    /// start to every peer. Requires every connected person to be ready and, in versus, at least
    /// two teams.
    /// </summary>
    /// <param name="gameSetup">The game's own start data (map, seed, options), passed to every peer's simulation.</param>
    public RequestResult Start(ReadOnlyMemory<byte> gameSetup = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (gameSetup.Length > ProtocolLimits.MaxSetupBytes)
        {
            throw new ArgumentException($"The game setup may be at most {ProtocolLimits.MaxSetupBytes} bytes.", nameof(gameSetup));
        }

        RequestResult ready = CanStart();
        if (ready != RequestResult.Accepted)
        {
            return ready;
        }

        var start = new MatchStart
        {
            MatchId = Guid.NewGuid(),
            Mode = room.Settings.Mode,
            TickMilliseconds = options.Lockstep.TickMilliseconds,
            HashIntervalTicks = options.Lockstep.HashIntervalTicks,
            Players = room.State.Players,
            GameSetup = gameSetup.ToArray(),
        };

        // The simulation is built first: if the game's factory throws, the room stays in the lobby.
        var started = new HostMatch(start, CreateSimulation(simulationFactory, start), options.Lockstep, new MatchLedger(start.Players));
        room.BeginMatch();
        match = started;
        started.Pacer.Reset(clock.Now);
        byte[] begin = MessageCodec.Encode(new MatchBeginMessage(start, started.SpeedPercent, started.TimelineId));
        foreach (RemotePeer peer in peers.Values)
        {
            if (peer.HasJoined)
            {
                peer.Connection.Send(begin);
                peer.InMatch = true;
            }
        }

        MatchStarted?.Invoke(this, new MatchStartedEventArgs { Start = start, Simulation = started.Simulation });
        PublishRoom();
        return RequestResult.Accepted;
    }

    /// <summary>Queues a game command of the host's own tribe; it runs a few ticks from now on every peer.</summary>
    /// <param name="payload">The game's encoding of the command.</param>
    public RequestResult SendCommand(ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (payload.Length > options.Lockstep.MaxCommandBytes)
        {
            throw new ArgumentException($"A command may be at most {options.Lockstep.MaxCommandBytes} bytes.", nameof(payload));
        }

        if (match is null || closed)
        {
            return RequestResult.NotAllowedNow;
        }

        PlayerInfo host = room.Host;
        if (!host.CanSendCommands)
        {
            return RequestResult.NotAPlayer;
        }

        long sequence = match.LastSequenceOf(host.Slot) + 1;
        return match.Schedule(host.Slot, match.NextTick + options.Lockstep.MinInputDelayTicks, payload.ToArray(), sequence);
    }

    /// <summary>Pauses the match on every peer; they all stop at the same tick.</summary>
    public RequestResult Pause() => SetPaused(true);

    /// <summary>Continues a paused match.</summary>
    public RequestResult Resume() => SetPaused(false);

    /// <summary>Changes the game speed for every peer.</summary>
    /// <param name="speedPercent">Speed in percent of normal, 25 to 400.</param>
    public RequestResult SetSpeed(int speedPercent)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!RoomLimits.IsValidSpeed(speedPercent))
        {
            return RequestResult.InvalidValue;
        }

        if (match is null || closed)
        {
            return RequestResult.NotAllowedNow;
        }

        if (match.SpeedPercent != speedPercent)
        {
            match.SetSpeed(speedPercent);
            PublishControl(match);
        }

        return RequestResult.Accepted;
    }

    /// <summary>
    /// The host gives up. Final for the match: the host keeps hosting and watches; the match goes
    /// on for the others.
    /// </summary>
    public RequestResult Forfeit() => Publish(ForfeitSlotHolder(room.Host.Id));

    /// <summary>
    /// Marks a slot as defeated (its tribe was wiped out), with the same effect as a forfeit.
    /// Call it from the host's rules when they find a tribe eliminated.
    /// </summary>
    /// <param name="slot">The defeated slot.</param>
    public RequestResult Defeat(int slot) =>
        room.InSlot(slot) is { } player ? Publish(ForfeitSlotHolder(player.Id)) : RequestResult.NotFound;

    /// <summary>
    /// The whole match as bytes: room, roster, the simulation snapshot and the commands waiting
    /// for their tick. Store it on the host; <see cref="ResumeFromSave"/> continues from it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No match is running.</exception>
    public byte[] SaveMatch()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        HostMatch current = match ?? throw new InvalidOperationException("There is no running match to save.");
        var save = new SavedMatch
        {
            GameVersion = options.GameVersion,
            ContentVersion = options.ContentVersion,
            Room = room.State with { Phase = RoomPhase.InGame },
            Banned = [.. room.Banned],
            Start = current.Start,
            Tick = current.NextTick,
            SpeedPercent = current.SpeedPercent,
            Snapshot = current.Simulation.SaveSnapshot(),
            SystemQueue = [.. current.PendingSystemCommands],
            Scheduled = [.. current.ScheduledCommands],
            Ledger = current.Ledger.State,
        };
        return MatchSaveCodec.Write(save);
    }

    private RequestResult SetPaused(bool paused)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (match is null || closed)
        {
            return RequestResult.NotAllowedNow;
        }

        if (match.Paused != paused)
        {
            match.Paused = paused;
            match.Pacer.Reset(clock.Now);
            PublishControl(match);
        }

        return RequestResult.Accepted;
    }

    private void PublishControl(HostMatch current)
    {
        transport.SendToMany(MatchConnections(), MessageCodec.Encode(new ControlStateMessage(current.Paused, current.SpeedPercent)));
        ControlStateChanged?.Invoke(this, new ControlStateChangedEventArgs { Paused = current.Paused, SpeedPercent = current.SpeedPercent });
    }

    private RequestResult ForfeitSlotHolder(Guid playerId)
    {
        if (match is null || closed)
        {
            return RequestResult.NotAllowedNow;
        }

        PlayerInfo? player = room.Find(playerId);
        if (player is not { HasSlot: true })
        {
            return RequestResult.NotAPlayer;
        }

        if (player.Presence == PlayerPresence.Forfeited)
        {
            return RequestResult.NotAllowedNow;
        }

        room.SetPresence(playerId, PlayerPresence.Forfeited);
        match.DropScheduled(player.Slot);
        match.QueueSystem(PlayerCommand.System(player.Slot, CommandKind.PlayerForfeited));
        return RequestResult.Accepted;
    }

    private static ILockstepSimulation CreateSimulation(Func<MatchStart, ILockstepSimulation> factory, MatchStart start) =>
        factory(start) ?? throw new InvalidOperationException("The simulation factory returned null.");

    private static HostMatch RestoreMatch(
        SavedMatch saved,
        List<int> absentSlots,
        Func<MatchStart, ILockstepSimulation> factory,
        LockstepOptions lockstep)
    {
        ILockstepSimulation simulation = CreateSimulation(factory, saved.Start);
        simulation.LoadSnapshot(saved.Snapshot);
        if (simulation.Tick != saved.Tick)
        {
            throw new InvalidOperationException($"After loading the snapshot of tick {saved.Tick} the simulation reports tick {simulation.Tick}.");
        }

        var restored = new HostMatch(saved.Start, simulation, lockstep, MatchLedger.From(saved.Ledger)) { Paused = true };
        restored.SetSpeed(saved.SpeedPercent);
        restored.Restore(saved.SystemQueue, saved.Scheduled);
        foreach (int slot in absentSlots)
        {
            restored.DropScheduled(slot);
            restored.QueueSystem(PlayerCommand.System(slot, CommandKind.PlayerAbsent));
        }

        return restored;
    }

    private void JoinRunningMatch(RemotePeer peer, HelloMessage hello, AdmitResult admitted, HostMatch current)
    {
        PlayerInfo player = admitted.Player!;
        if (admitted.Outcome == AdmitOutcome.Returned)
        {
            current.QueueSystem(PlayerCommand.System(player.Slot, CommandKind.PlayerReturned));
        }
        else if (admitted.Outcome == AdmitOutcome.JoinedInProgress)
        {
            current.QueueSystem(PlayerCommand.Joined(player));
        }

        List<byte[]>? missed = current.MissedBatches(hello.TimelineId, hello.ResumeTick);
        long lastSequence = player.HasSlot ? current.LastSequenceOf(player.Slot) : 0;
        Send(peer, new WelcomeMessage(room.State, Extras(), missed is null ? -1 : hello.ResumeTick, current.TimelineId, lastSequence));
        if (missed is null)
        {
            SendSnapshot(peer, current);
        }
        else
        {
            foreach (byte[] batch in missed)
            {
                peer.Connection.Send(batch);
            }

            Send(peer, new ControlStateMessage(current.Paused, current.SpeedPercent));
            peer.IgnoreHashesBefore = hello.ResumeTick;
        }

        peer.InMatch = true;
        PublishRoom();
    }

    /// <summary>Sends the state before the next tick; the batches from that tick on follow as they are made.</summary>
    private static void SendSnapshot(RemotePeer peer, HostMatch current)
    {
        (long tick, PackedSnapshot packed) = current.TakeSnapshot();
        Send(peer, new SnapshotBeginMessage(
            current.Start, tick, packed.RawLength, packed.Checksum, packed.Compressed, packed.Chunks.Count, current.SpeedPercent, current.Paused, current.TimelineId, current.Ledger.State));
        for (int i = 0; i < packed.Chunks.Count; i++)
        {
            Send(peer, new SnapshotChunkMessage(i, packed.Chunks[i]));
        }

        peer.IgnoreHashesBefore = tick;
        peer.DesyncReported = false;
    }

    private void RunMatch(HostMatch current, TimeSpan now)
    {
        if (current.Paused)
        {
            current.Pacer.Reset(now);
            return;
        }

        int steps = current.Pacer.Advance(now, options.Lockstep.MaxHostStepsPerUpdate);
        for (int i = 0; i < steps && !closed; i++)
        {
            ProduceTick(current);
        }
    }

    private void ProduceTick(HostMatch current)
    {
        TickBatch batch = current.TakeBatch(current.NextTick);
        byte[] encoded = MessageCodec.Encode(new BatchMessage(batch));
        current.History.Add(batch.Tick, encoded);
        transport.SendToMany(MatchConnections(), encoded);
        current.Engine.Enqueue(batch);
        current.Engine.TryStep(out _);
        current.Ledger.Apply(batch);
        if (LockstepEngine.IsHashTick(batch.Tick, current.Start.HashIntervalTicks))
        {
            current.Hashes.Record(batch.Tick, current.Simulation.StateHash());
        }

        SessionShared.RaisePresenceChanges(this, batch, room.State, PlayerPresenceChanged);
        TickStepped?.Invoke(this, new TickSteppedEventArgs { Tick = batch.Tick, Commands = batch.Commands });
    }

    private void HandleCommand(RemotePeer peer, CommandMessage command)
    {
        PlayerInfo? player = room.Find(peer.PlayerId);
        RequestResult result;
        if (match is null)
        {
            result = RequestResult.NotAllowedNow;
        }
        else if (player is null || !player.CanSendCommands)
        {
            result = RequestResult.NotAPlayer;
        }
        else if (command.Payload.Length > options.Lockstep.MaxCommandBytes)
        {
            result = RequestResult.InvalidValue;
        }
        else
        {
            result = match.Schedule(player.Slot, command.TargetTick, command.Payload, command.Sequence);
        }

        if (result != RequestResult.Accepted)
        {
            Deny(peer, RequestKind.Command, result);
            CommandRejected?.Invoke(this, new CommandRejectedEventArgs { PlayerId = peer.PlayerId, Result = result });
        }
    }

    private void HandleStateHash(RemotePeer peer, StateHashMessage report)
    {
        if (match is null || report.Tick < peer.IgnoreHashesBefore)
        {
            return;
        }

        HashCheck check = match.Hashes.Check(report.Tick, report.Hash, out uint expected);
        if (check == HashCheck.Match)
        {
            peer.LastMatchingHashTick = Math.Max(peer.LastMatchingHashTick, report.Tick);
            return;
        }

        if (check == HashCheck.Unknown || peer.DesyncReported)
        {
            return;
        }

        peer.DesyncReported = true;
        Send(peer, new DesyncNoticeMessage(report.Tick, peer.LastMatchingHashTick, peer.PlayerId, expected, report.Hash));
        DesyncDetected?.Invoke(this, new DesyncDetectedEventArgs
        {
            Tick = report.Tick,
            LastMatchingTick = peer.LastMatchingHashTick,
            PlayerId = peer.PlayerId,
            ExpectedHash = expected,
            ReportedHash = report.Hash,
        });
        if (options.Lockstep.ResyncOnDesync && match is not null && peers.ContainsKey(peer.Connection.Id))
        {
            SendSnapshot(peer, match);
        }
    }

    private void HandleControlRequest(RemotePeer peer, ControlRequestMessage request)
    {
        RequestKind kind = request.Kind == ControlKind.Speed ? RequestKind.Speed : RequestKind.Pause;
        if (match is null || room.Find(peer.PlayerId) is not { CanSendCommands: true })
        {
            Deny(peer, kind, RequestResult.NotAllowedNow);
            return;
        }

        if (request.Kind != ControlKind.Speed)
        {
            if (room.Settings.AllowClientPause)
            {
                SetPaused(request.Kind == ControlKind.Pause);
            }
            else
            {
                Deny(peer, kind, RequestResult.NotAllowedNow);
            }

            return;
        }

        if (!RoomLimits.IsValidSpeed(request.Value))
        {
            Deny(peer, kind, RequestResult.InvalidValue);
            return;
        }

        SpeedRequested?.Invoke(this, new SpeedRequestedEventArgs { PlayerId = peer.PlayerId, SpeedPercent = request.Value });
        if (room.Settings.AcceptSpeedRequests)
        {
            SetSpeed(request.Value);
        }
    }
}
