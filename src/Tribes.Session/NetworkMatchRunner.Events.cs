using System;
using System.Collections.Generic;
using Tribes.Core.Commands;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>Session and match events of <see cref="NetworkMatchRunner"/>.</summary>
public sealed partial class NetworkMatchRunner
{
    private readonly List<int> slotsToDefeat = [];
    private bool closeSession;
    private bool hostingLost;
    private bool autosaveDue;
    private bool sessionDisposed;

    private void HookHost(HostSession session)
    {
        session.MatchStarted += OnMatchStarted;
        session.DesyncDetected += OnDesync;
        session.ControlStateChanged += OnControlState;
        session.RoomChanged += OnRoomChanged;
        session.WorldTooLarge += OnWorldTooLarge;
    }

    private void HookClient(ClientSession session)
    {
        session.MatchStarted += OnMatchStarted;
        session.DesyncDetected += OnDesync;
        session.ControlStateChanged += OnControlState;
        session.RoomChanged += OnRoomChanged;
        session.RequestDenied += OnRequestDenied;
        session.Ended += OnClientEnded;
        session.BecameHost += OnBecameHost;
        session.HostChanged += OnHostChanged;
        session.PhaseChanged += OnClientPhase;
    }

    private void HookMatch(LockstepMatch lockstep)
    {
        lockstep.Stepped += OnStepped;
        lockstep.Reloaded += OnReloaded;
    }

    private void UnhookMatch(LockstepMatch lockstep)
    {
        lockstep.Stepped -= OnStepped;
        lockstep.Reloaded -= OnReloaded;
    }

    private bool IsCurrent(object? sender) => sender is not null && (ReferenceEquals(sender, host) || ReferenceEquals(sender, client));

    private void DecideIfOver()
    {
        if (match.Outcome is { } outcome)
        {
            Decide(outcome, LocalTribe);
        }
    }

    /// <summary>Session calls that must not run inside the session's own update.</summary>
    private void RunDeferred()
    {
        if (sessionDisposed)
        {
            slotsToDefeat.Clear();
            return;
        }
        if (host is not null && Phase != MatchPhase.Stopped)
        {
            foreach (int slot in slotsToDefeat)
            {
                if (host.Room.PlayerInSlot(slot) is { Presence: not PlayerPresence.Forfeited })
                {
                    host.Defeat(slot);
                }
            }
        }
        slotsToDefeat.Clear();
        if (autosaveDue)
        {
            // Between ticks the host's match records (ledger, queues) agree with the world.
            autosaveDue = false;
            Autosave();
        }
        if (hostingLost && host is not null && Phase != MatchPhase.Stopped)
        {
            // The others carry on under a new host; this device's copy of the match must not go on alone.
            Log($"SESSION this device lost its connection while hosting; the others play on (tick {Simulation.Tick})");
            SaveBeforeLeaving();
            StopWith(new MatchStop(StopReason.ConnectionLost, Simulation.Tick, "The connection was lost while hosting; the other players carry on with a new host."));
            closeSession = true;
        }

        hostingLost = false;
        if (closeSession)
        {
            closeSession = false;
            host?.Close();
            client?.Leave();
        }
    }

    /// <inheritdoc />
    private protected override void RequestAutosave() => autosaveDue = true;

    private void OnStepped(object? sender, LockstepTickEventArgs e)
    {
        sinceTick = 0;
        RaiseTick(new MatchTickEventArgs { Result = e.Result, LocalOrders = MatchLocalOrders(e.Sources), Changes = e.Changes });
        RaiseChangeNotices(e.Changes);
        foreach (PlayerChange change in e.Changes)
        {
            if (change is { Kind: PlayerChangeKind.Out, Player: { IsComputer: false, HasSlot: true } })
            {
                slotsToDefeat.Add(change.Player.Slot);
            }
        }
        if (e.Decided is { } outcome)
        {
            Decide(outcome, Players.ById(LocalPlayerId)?.Tribe ?? -1);
        }
        CountAutosaveTick();
    }

    /// <summary>For each order outcome, the local order it answers: local commands run in the order they were sent.</summary>
    private GameOrder?[] MatchLocalOrders(IReadOnlyList<PlayerCommand> sources)
    {
        var orders = new GameOrder?[sources.Count];
        int slot = Players.ById(LocalPlayerId)?.Slot ?? -1;
        for (int i = 0; i < sources.Count && slot >= 0 && pending.Count > 0; i++)
        {
            if (sources[i].Slot != slot)
            {
                continue;
            }
            for (LinkedListNode<(GameOrder Order, byte[] Payload)>? node = pending.First; node is not null; node = node.Next)
            {
                if (!sources[i].Payload.Span.SequenceEqual(node.Value.Payload))
                {
                    continue;
                }
                orders[i] = node.Value.Order;
                // Orders queued before this one never ran (dropped on the way).
                while (pending.First != node)
                {
                    pending.RemoveFirst();
                }
                pending.RemoveFirst();
                break;
            }
        }
        return orders;
    }

    private void OnReloaded(object? sender, EventArgs e)
    {
        RaiseSimulationReplaced();
        RaisePlayersChanged();
        DecideIfOver();
    }

    private void OnMatchStarted(object? sender, MatchStartedEventArgs e)
    {
        if (!IsCurrent(sender) || e.Simulation is not LockstepMatch started || ReferenceEquals(started, match))
        {
            return;
        }
        UnhookMatch(match);
        match = started;
        HookMatch(started);
        Log($"SESSION rebound to a new match state at tick {started.Tick}");
        RaiseSimulationReplaced();
        RaisePlayersChanged();
        DecideIfOver();
    }

    private void OnWorldTooLarge(object? sender, WorldTooLargeEventArgs e)
    {
        if (IsCurrent(sender))
        {
            Log($"SESSION world of {e.WorldBytes} bytes too large to send to {e.PlayerName} (limit {e.LimitBytes})");
            RaiseNotice(new MatchNotice(MatchNoticeKind.WorldTooLarge, Simulation.Tick, Players.ById(e.PlayerId), e.PlayerName));
        }
    }

    private void OnDesync(object? sender, DesyncDetectedEventArgs e)
    {
        if (!IsCurrent(sender))
        {
            return;
        }
        var info = new DesyncInfo(e.Tick, e.LastMatchingTick, e.PlayerId, e.ExpectedHash, e.ReportedHash);
        Log("SESSION DESYNC " + info);
        if (host is not null)
        {
            SaveBeforeLeaving();
        }
        StopWith(new MatchStop(StopReason.Desync, Simulation.Tick, "The peers' states differ: " + info, info));
        closeSession = true;
    }

    private void OnControlState(object? sender, ControlStateChangedEventArgs e)
    {
        if (IsCurrent(sender) && !e.Paused && host is not null && Phase == MatchPhase.WaitingForPlayers)
        {
            SetPhase(match.Outcome is null ? MatchPhase.Running : MatchPhase.Over);
        }
    }

    private void OnRoomChanged(object? sender, RoomChangedEventArgs e)
    {
        if (IsCurrent(sender))
        {
            RaisePlayersChanged();
        }
    }

    private void OnRequestDenied(object? sender, RequestDeniedEventArgs e)
    {
        if (!IsCurrent(sender))
        {
            return;
        }
        MatchNoticeKind kind = e.Request == RequestKind.Command ? MatchNoticeKind.OrderDropped : MatchNoticeKind.RequestDenied;
        RaiseNotice(new MatchNotice(kind, Simulation.Tick, null, $"{e.Request}: {e.Message}"));
    }

    private void OnClientEnded(object? sender, SessionEndedEventArgs e)
    {
        if (!IsCurrent(sender) || e.Reason == SessionEndReason.BecameHost)
        {
            return;
        }
        StopReason reason = e.Reason switch
        {
            SessionEndReason.Left => StopReason.Quit,
            SessionEndReason.HostClosed => StopReason.HostLeft,
            SessionEndReason.ConnectionLost => StopReason.ConnectionLost,
            SessionEndReason.Kicked => StopReason.Kicked,
            SessionEndReason.WorldTooLarge => StopReason.WorldTooLarge,
            _ => StopReason.Error,
        };
        StopWith(new MatchStop(reason, Simulation.Tick, $"{e.Reason}: {e.Message}"));
    }

    private void OnBecameHost(object? sender, BecameHostEventArgs e)
    {
        if (!IsCurrent(sender))
        {
            return;
        }
        client = null;
        host = e.Host;
        HookHost(e.Host);
        Log($"SESSION this device hosts the match now (tick {Simulation.Tick})");
        RaiseNotice(new MatchNotice(MatchNoticeKind.HostChanged, Simulation.Tick, Players.ById(LocalPlayerId)));
        RaisePlayersChanged();
    }

    private void OnHostChanged(object? sender, HostChangedEventArgs e)
    {
        if (IsCurrent(sender))
        {
            RaiseNotice(new MatchNotice(MatchNoticeKind.HostChanged, Simulation.Tick, Players.ById(e.HostId)));
            RaisePlayersChanged();
        }
    }

    private void OnClientPhase(object? sender, ClientPhaseChangedEventArgs e)
    {
        if (IsCurrent(sender) && e.Phase is ClientPhase.Reconnecting or ClientPhase.Migrating)
        {
            RaiseNotice(new MatchNotice(MatchNoticeKind.Reconnecting, Simulation.Tick, null, e.Phase.ToString()));
        }
    }

    private sealed class NetworkSink(NetworkMatchRunner runner) : IOrderSink
    {
        public bool CanSubmit =>
            runner.Phase != MatchPhase.Stopped && runner.Role == LocalRole.Player
            && (runner.host?.LocalPlayer.CanSendCommands ?? runner.client?.CanSendCommands ?? false);

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
            byte[] payload;
            bool sent;
            try
            {
                payload = GameOrderCodec.Encode(order);
                sent = runner.host is not null
                    ? runner.host.SendCommand(payload) == RequestResult.Accepted
                    : runner.client!.SendCommand(payload);
            }
            catch (ArgumentException)
            {
                return OrderSubmitResult.Refused;
            }
            if (!sent)
            {
                return OrderSubmitResult.Refused;
            }
            runner.pending.AddLast((order, payload));
            if (runner.pending.Count > MaxPendingOrders)
            {
                runner.pending.RemoveFirst();
            }
            return OrderSubmitResult.Queued;
        }
    }
}
