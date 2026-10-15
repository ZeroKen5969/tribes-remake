using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Session.Tests;

/// <summary>Whole matches of several devices in one process over the loopback transport.</summary>
public sealed class MultiPeerTests(ITestOutputHelper output)
{
    private static readonly MatchPlan Plan = new() { Mission = 8, Difficulty = 0, Seed = 31 };

    [Fact]
    public void FourPeers_TenThousandTicks_WithRandomOrders_StayInSync()
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 4);
        for (int i = 1; i <= 3; i++)
        {
            cluster.AddClient("P" + i);
        }
        cluster.StartMatch(Plan);
        Assert.Equal(RequestResult.Accepted, cluster.HostPeer.Runner!.SetSpeed(400));

        cluster.RunTicks(10_000);
        cluster.Settle();

        cluster.AssertNoStops();
        int shared = cluster.AssertInSync(90);
        foreach (SessionPeer peer in cluster.Peers)
        {
            output.WriteLine($"{peer.Name}: tick {peer.Tick}, tribe {peer.Runner!.LocalTribe}, orders {peer.Queued} queued / {peer.Answered} answered, {peer.RawSent} without tribe, garbage {peer.GarbageSent}, checks {peer.Hashes.Count}");
            Assert.True(peer.Queued + peer.RawSent > 100, $"{peer.Name} gave only {peer.Queued + peer.RawSent} orders");
            Assert.True(peer.GarbageSent > 0);
        }
        // Every human plays a tribe of their own and sees the outcomes of their own orders.
        Assert.All(cluster.Peers, p => Assert.True(p.Answered > 100 && p.Runner!.LocalTribe >= 0, $"{p.Name}: tribe {p.Runner!.LocalTribe}, {p.Answered} answered"));
        Assert.Equal(4, cluster.HostPeer.Runner!.Simulation.World.Tribes.Count);
        output.WriteLine($"shared checks per pair: {shared}");
    }

    [Fact]
    public void ThreePeers_WithLatencyAndJitter_StayInSync()
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 9);
        var link = new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(60), Jitter = TimeSpan.FromMilliseconds(40) };
        cluster.AddClient("Far", link: link);
        cluster.AddClient("Farther", link: link with { Latency = TimeSpan.FromMilliseconds(150) });
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(3_000);
        cluster.Settle();

        cluster.AssertNoStops();
        cluster.AssertInSync(25);
        Assert.True(cluster.Clients[0].Answered > 20);
        Assert.True(cluster.Clients[1].Runner!.Client!.InputDelayTicks > 1);
    }

    [Fact]
    public void LeaveRejoinForfeitSpectateAndJoin_MidMatch_EveryPeerAgrees()
    {
        var room = new RoomSettings { Name = "Endless", Mode = RoomMode.Endless, AllowJoinInProgress = false };
        using var cluster = new SessionCluster(new TwoHumansControl(), room, seed: 5);
        SessionPeer leaver = cluster.AddClient("Leaver");
        SessionPeer quitter = cluster.AddClient("Quitter");
        cluster.StartMatch(Plan with { Mission = -1, Endless = new EndlessPlan(5, 32, 1, 0) });
        cluster.HostPeer.Runner!.SetSpeed(400);
        Assert.Equal(MatchKind.Endless, cluster.HostPeer.Runner.Match);
        cluster.RunTicks(400);

        // Leaving: the tribe stays without orders (no computer takes it), shown as absent everywhere.
        leaver.Runner!.Quit();
        Assert.Equal(StopReason.Quit, leaver.Stops.Single().Reason);
        cluster.RunUntil(() => cluster.HostPeer.Runner.Players.ById(leaver.Runner.LocalPlayerId)!.State == PlayerState.Absent, "the leaver to be absent");
        cluster.RunTicks(200);
        Assert.Contains(cluster.HostPeer.Notices, n => n.Kind == MatchNoticeKind.PlayerAbsent && n.Player!.Name == "Leaver");
        Assert.Contains(quitter.Notices, n => n.Kind == MatchNoticeKind.PlayerAbsent && n.Player!.Name == "Leaver");
        long absentTick = cluster.HostPeer.Notices.First(n => n.Kind == MatchNoticeKind.PlayerAbsent).Tick;
        Assert.Equal(absentTick, quitter.Notices.First(n => n.Kind == MatchNoticeKind.PlayerAbsent).Tick);

        // Coming back with the same id: same slot and tribe, orders count again.
        Guid leaverId = leaver.Runner.LocalPlayerId;
        int leaverTribe = cluster.HostPeer.Runner.Players.ById(leaverId)!.Tribe;
        cluster.Connect(leaver);
        cluster.RunUntil(() => leaver.Runner is not null && cluster.HostPeer.Runner.Players.ById(leaverId)!.State == PlayerState.Playing, "the leaver to play again");
        Assert.Equal(leaverTribe, leaver.Runner!.LocalTribe);
        Assert.Contains(cluster.HostPeer.Notices, n => n.Kind == MatchNoticeKind.PlayerReturned);
        int answered = leaver.Answered;
        cluster.RunTicks(300);
        Assert.True(leaver.Answered > answered);

        // Giving up is final: no more orders, the player watches their former tribe.
        Assert.Equal(RequestResult.Accepted, quitter.Runner!.Forfeit());
        cluster.RunUntil(() => quitter.Runner.Role == LocalRole.Forfeited, "the forfeit");
        Assert.False(quitter.Runner.Orders.CanSubmit);
        Assert.Equal(OrderSubmitResult.NotAPlayer, quitter.Runner.Orders.Submit(new Tribes.Core.Commands.SetHints(1, true)));
        Assert.Equal(RequestResult.NotAllowedNow, quitter.Runner.Forfeit());
        // In the two-tribe world the third human has no tribe, so it watches the first human tribe.
        Assert.Equal(0, quitter.Runner.ViewTribe);
        Assert.Equal(-1, quitter.Runner.LocalTribe);

        // Rejoining after a forfeit only watches.
        cluster.Connect(quitter);
        cluster.RunUntil(() => quitter.Runner is not null, "the forfeited player to rejoin");
        Assert.Equal(LocalRole.Forfeited, quitter.Runner!.Role);
        Assert.False(quitter.Runner.Orders.CanSubmit);

        // A spectator joins the running match.
        SessionPeer watcher = cluster.AddClient("Watcher", spectate: true);
        cluster.RunUntil(() => watcher.Runner is not null, "the spectator to watch");
        Assert.Equal(LocalRole.Spectator, watcher.Runner!.Role);
        Assert.Equal(0, watcher.Runner.ViewTribe);
        Assert.False(watcher.Runner.Orders.CanSubmit);
        Assert.Equal(RequestResult.NotAllowedNow, watcher.Runner.SetPaused(true));

        // The host invites: a new player joins the running endless match and takes a free slot.
        Assert.False(cluster.HostPeer.Runner.IsInviteOpen);
        Assert.Equal(RequestResult.Accepted, cluster.HostPeer.Runner.SetInviteOpen(true));
        Assert.True(cluster.HostPeer.Runner.IsInviteOpen);
        SessionPeer joiner = cluster.AddClient("Joiner");
        cluster.RunUntil(() => joiner.Runner is not null && cluster.HostPeer.Runner.Players.Any(p => p.Name == "Joiner"), "the new player to join");
        cluster.RunTicks(300);
        cluster.Settle();
        Assert.Contains(watcher.Notices, n => n.Kind == MatchNoticeKind.PlayerJoined && n.Player!.Name == "Joiner");
        long joinedTick = cluster.HostPeer.Notices.Single(n => n.Kind == MatchNoticeKind.PlayerJoined).Tick;
        Assert.Equal(joinedTick, leaver.Notices.Single(n => n.Kind == MatchNoticeKind.PlayerJoined).Tick);
        PlayerEntry joined = joiner.Runner!.Players.Single(p => p.Name == "Joiner");
        Assert.Equal(-1, joined.Tribe);
        Assert.Equal(LocalRole.Spectator, joiner.Runner.Role);
        Assert.Equal(RequestResult.Accepted, cluster.HostPeer.Runner.SetInviteOpen(false));

        Assert.All(cluster.Peers, p => Assert.NotEqual(MatchPhase.Stopped, p.Runner!.Phase));
        cluster.AssertInSync(3);
        Assert.Equal(cluster.HostPeer.Runner.Lockstep.StateHash(), watcher.Runner.Lockstep.StateHash());
    }

    [Fact]
    public void HostAutosaves_Quits_Resumes_PlayersRejoin_AndHashesContinueEqual()
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 7, autosaveTicks: 500);
        SessionPeer guest = cluster.AddClient("Guest");
        SessionPeer giver = cluster.AddClient("Giver");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(1_100);
        Assert.Equal(2, cluster.Store.Written.Count(s => s.Slot == SaveSlot.Auto));
        Assert.Equal(2, cluster.HostPeer.Notices.Count(n => n.Kind == MatchNoticeKind.Autosaved));
        Assert.Null(guest.Runner!.Save("1"));
        Assert.Equal(RequestResult.Accepted, giver.Runner!.Forfeit());
        cluster.RunTicks(50);
        cluster.Settle();
        Guid guestId = guest.Runner.LocalPlayerId, giverId = giver.Runner.LocalPlayerId;

        // The host quits: the match is saved, everyone else is told the host left (LAN has no host migration).
        long quitTick = cluster.HostPeer.Tick;
        cluster.HostPeer.Runner.Quit();
        cluster.RunUntil(() => guest.Stops.Count == 1 && giver.Stops.Count == 1, "the clients to stop");
        Assert.Equal(StopReason.HostLeft, guest.Stops[0].Reason);
        byte[] save = cluster.Store.Files["versus-auto"];
        SaveHeader header = MatchSaveFile.ReadHeader(save);
        Assert.Equal((SaveKind.HostedMatch, MatchKind.Versus, quitTick), (header.Kind, header.Match, header.Tick));
        Assert.Equal(PlayerState.Forfeited, header.Players.ById(giverId)!.State);
        uint savedHash = cluster.HostPeer.Runner.Lockstep.StateHash();

        // Resume: the host loads, the match waits paused; players come back by their ids.
        cluster.ResumeHost(save);
        NetworkMatchRunner host = cluster.HostPeer.Runner!;
        Assert.Equal(MatchPhase.WaitingForPlayers, host.Phase);
        Assert.Equal(savedHash, host.Lockstep.StateHash());
        Assert.Equal(quitTick, host.Simulation.Tick);
        Assert.Equal(2, host.ExpectedPlayers.Count);
        Assert.All(host.ExpectedPlayers, p => Assert.False(p.IsConnected));
        cluster.Connect(guest);
        cluster.Connect(giver);
        cluster.RunUntil(() => guest.Runner is not null && giver.Runner is not null, "the players to rejoin");
        cluster.RunUntil(() => host.ExpectedPlayers.All(p => p.IsConnected), "the room to show them back");
        Assert.Equal(quitTick, host.Simulation.Tick);
        Assert.Equal(LocalRole.Player, guest.Runner!.Role);
        Assert.Equal(guestId, guest.Runner.LocalPlayerId);
        Assert.Equal(LocalRole.Forfeited, giver.Runner!.Role);

        Assert.Equal(RequestResult.Accepted, host.ContinueResumed());
        Assert.Equal(MatchPhase.Running, host.Phase);
        Assert.Equal(RequestResult.NotAllowedNow, host.ContinueResumed());
        cluster.RunTicks(1_200);
        cluster.Settle();

        cluster.AssertInSync(11, [cluster.HostPeer, guest, giver]);
        Assert.Equal(host.Lockstep.StateHash(), guest.Runner.Lockstep.StateHash());
        Assert.True(guest.Runner.Simulation.Tick > quitTick + 1_000);
        Assert.Empty(cluster.HostPeer.Stops);
    }

    [Fact]
    public void ResumedSave_FromAnotherPlayerOrDamaged_IsRefused()
    {
        byte[] save;
        Guid hostKey;
        using (var cluster = new SessionCluster(new TwoHumansControl(), seed: 3))
        {
            cluster.AddClient("Guest");
            cluster.StartMatch(Plan);
            cluster.RunTicks(20);
            save = cluster.HostPeer.Runner!.Save("1") is { } slot ? cluster.Store.Files[slot.Stem] : throw new InvalidOperationException();
            hostKey = cluster.HostPeer.Key;
        }
        using var other = new SessionCluster(new TwoHumansControl(), seed: 4);
        other.Host.Close();
        other.Host.Dispose();
        var transport = new LoopbackTransport(other.Network, "elsewhere");
        SessionPeer peer = other.HostPeer;
        HostOptions mine = other.HostOptions(peer) with { PlayerId = hostKey };

        Assert.Equal(SaveProblem.NotTheHost, Refused(() => NetworkMatchRunner.Resume(transport, other.HostOptions(peer), save, other.Factory, other.RunnerOptions(peer), other.Clock)));
        byte[] single = MatchSaveFile.Write(MatchSaveFile.ReadHeader(save) with { Kind = SaveKind.SinglePlayer }, [1]);
        Assert.Equal(SaveProblem.NotASave, Refused(() => NetworkMatchRunner.Resume(transport, mine, single, other.Factory, other.RunnerOptions(peer), other.Clock)));
        byte[] garbled = MatchSaveFile.Write(MatchSaveFile.ReadHeader(save), [1, 2, 3]);
        Assert.Equal(SaveProblem.Damaged, Refused(() => NetworkMatchRunner.Resume(transport, mine, garbled, other.Factory, other.RunnerOptions(peer), other.Clock)));
        Assert.Equal(SaveProblem.Damaged, Refused(() => NetworkMatchRunner.Resume(transport, mine, save, _ => new BrokenSimulation(), other.RunnerOptions(peer), other.Clock)));

        NetworkMatchRunner resumed = NetworkMatchRunner.Resume(transport, mine, save, other.Factory, other.RunnerOptions(peer), other.Clock);
        Assert.Equal(MatchPhase.WaitingForPlayers, resumed.Phase);
        Assert.Equal(RequestResult.Accepted, resumed.SetPaused(false));
        Assert.Equal(MatchPhase.Running, resumed.Phase);
        resumed.Dispose();
    }

    [Fact]
    public void Desync_StopsEveryDevice_WithBothHashesAndTheTick_AndTheHostSaves()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 8);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(100);

        // A peer whose world changed outside the rules no longer agrees with the host.
        guest.Runner!.Simulation.World.Frame += 1;
        cluster.RunUntil(() => cluster.HostPeer.Stops.Count == 1 && guest.Stops.Count == 1, "both to stop");

        MatchStop stop = cluster.HostPeer.Stops[0];
        Assert.Equal(StopReason.Desync, stop.Reason);
        Assert.Equal(StopReason.Desync, guest.Stops[0].Reason);
        DesyncInfo info = stop.Desync!;
        Assert.NotEqual(info.ExpectedHash, info.ReportedHash);
        Assert.Equal(guest.Runner.LocalPlayerId, info.PlayerId);
        Assert.True(info.Tick > info.LastMatchingTick);
        string logged = Assert.Single(cluster.HostPeer.Log, l => l.Contains("DESYNC", StringComparison.Ordinal));
        Assert.Contains($"0x{info.ExpectedHash:x8}", logged, StringComparison.Ordinal);
        Assert.Contains($"0x{info.ReportedHash:x8}", logged, StringComparison.Ordinal);
        Assert.Contains($"tick {info.Tick}", logged, StringComparison.Ordinal);
        Assert.Contains("versus-auto", cluster.Store.Files.Keys);
        Assert.Equal(MatchPhase.Stopped, cluster.HostPeer.Runner.Phase);
        Assert.False(cluster.HostPeer.Runner.CanPause);
        Assert.Equal(RequestResult.NotAllowedNow, cluster.HostPeer.Runner.SetPaused(true));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.HostPeer.Runner.SetInviteOpen(true));
        Assert.Equal(RequestResult.NotAllowedNow, cluster.HostPeer.Runner.Forfeit());
        Assert.Contains("tick", info.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PauseAndSpeed_FollowTheRoomSettings()
    {
        var strict = new RoomSettings { Name = "Strict", AllowClientPause = false, AcceptSpeedRequests = false };
        using (var cluster = new SessionCluster(new TwoHumansControl(), strict, seed: 2))
        {
            SessionPeer guest = cluster.AddClient("Guest");
            cluster.StartMatch(Plan);
            NetworkMatchRunner host = cluster.HostPeer.Runner!, client = guest.Runner!;
            Assert.True(host.CanPause && host.CanChangeSpeed);
            Assert.False(client.CanPause);
            Assert.False(client.CanChangeSpeed);
            Assert.Equal(RequestResult.NotAllowedNow, client.SetPaused(true));
            Assert.Equal(RequestResult.NotAllowedNow, client.SetSpeed(200));
            Assert.Equal(RequestResult.InvalidValue, client.SetSpeed(5));

            Assert.Equal(RequestResult.Accepted, host.SetPaused(true));
            cluster.RunUntil(() => client.IsPaused, "the client to see the pause");
            long tick = host.Simulation.Tick;
            cluster.RunUntil(() => client.Simulation.Tick == tick, "the client to stop at the host's tick");
            for (int i = 0; i < 100; i++)
            {
                cluster.Step();
            }
            Assert.Equal(tick, host.Simulation.Tick);
            Assert.Equal(1, client.TickAlpha);
            Assert.Equal(RequestResult.Accepted, host.SetPaused(false));
            Assert.Equal(RequestResult.Accepted, host.SetSpeed(200));
            cluster.RunUntil(() => client.SpeedPercent == 200 && !client.IsPaused, "the client to see the speed");
            Assert.Equal(0.06, client.TickSeconds, 6);
            Assert.InRange(host.TickAlpha, 0, 1);
            cluster.RunTicks(50);
            Assert.True(host.Simulation.Tick > tick);
        }

        var open = new RoomSettings { Name = "Open", AllowClientPause = true, AcceptSpeedRequests = true };
        using (var cluster = new SessionCluster(new TwoHumansControl(), open, seed: 2))
        {
            SessionPeer guest = cluster.AddClient("Guest");
            cluster.StartMatch(Plan);
            NetworkMatchRunner client = guest.Runner!;
            Assert.True(client.CanPause && client.CanChangeSpeed);
            Assert.Equal(RequestResult.Accepted, client.SetPaused(true));
            cluster.RunUntil(() => cluster.HostPeer.Runner!.IsPaused, "the host to pause");
            Assert.Equal(RequestResult.Accepted, client.SetPaused(false));
            Assert.Equal(RequestResult.Accepted, client.SetSpeed(300));
            cluster.RunUntil(() => cluster.HostPeer.Runner!.SpeedPercent == 300 && !cluster.HostPeer.Runner.IsPaused, "the host to take the speed");
            client.SetHeld(true);
            client.RosterOpen = true;
            Assert.False(client.IsPaused);
        }
    }

    [Fact]
    public void DefeatedTribe_GoesOut_TheHostMarksItInTheRoom_AndTheResultReachesEveryone()
    {
        var control = new TimedControl { DefeatTribe = 1, DefeatAt = 300, WinAt = 600 };
        using var cluster = new SessionCluster(control, seed: 6);
        SessionPeer loser = cluster.AddClient("Loser");
        SessionPeer third = cluster.AddClient("Third");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(350);
        cluster.Settle();

        Assert.Equal(LocalRole.Out, loser.Runner!.Role);
        Assert.False(loser.Runner.Orders.CanSubmit);
        Assert.Equal(1, loser.Runner.ViewTribe);
        Assert.Contains(third.Notices, n => n.Kind == MatchNoticeKind.PlayerOut && n.Tick == 299);
        cluster.RunUntil(() => cluster.Host.Room.FindPlayer(loser.Runner.LocalPlayerId)!.Presence == PlayerPresence.Forfeited, "the host to mark the defeat");
        Assert.False(loser.Client!.CanSendCommands);

        cluster.RunTicks(350);
        cluster.Settle();
        foreach (SessionPeer peer in cluster.Peers)
        {
            MatchOverEventArgs result = Assert.Single(peer.Results);
            Assert.True(result.Outcome.Won(0));
            Assert.Equal(peer == cluster.HostPeer, result.LocalVictory);
            Assert.Equal(MatchPhase.Over, peer.Runner!.Phase);
        }
        cluster.AssertNoStops();
        cluster.AssertInSync(5);
        // An over match is not autosaved on quit.
        cluster.HostPeer.Runner.Quit();
        Assert.Empty(cluster.Store.Written);
    }

    [Fact]
    public void HostMigration_TheNextPlayerHostsAndTheMatchGoesOn()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), hostMigration: true, seed: 12);
        SessionPeer heir = cluster.AddClient("Heir");
        SessionPeer other = cluster.AddClient("Other");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(200);
        cluster.Settle();

        cluster.HostPeer.Runner.Quit();
        cluster.RunUntil(() => heir.Runner!.IsHost, "the next player to host");
        Assert.Contains(heir.Notices, n => n.Kind == MatchNoticeKind.HostChanged);
        Assert.Null(heir.Runner!.Client);
        Assert.NotNull(heir.Runner.Host);
        cluster.RunUntil(() => other.Notices.Any(n => n.Kind == MatchNoticeKind.HostChanged), "the others to follow");
        long tick = heir.Runner.Simulation.Tick;
        cluster.RunUntil(() => heir.Runner.Simulation.Tick > tick + 300, "the match to go on");
        cluster.RunUntil(() => other.Runner!.Simulation.Tick >= heir.Runner.Simulation.Tick - 2, "the other to catch up");
        Assert.Empty(heir.Stops);
        Assert.Empty(other.Stops);
        cluster.AssertInSync(4, [heir, other]);
        Assert.NotNull(heir.Runner.Save("1"));
    }

    [Fact]
    public void Runner_NeedsALockstepMatch()
    {
        var network = new LoopbackNetwork(new ManualClock(), 1, false);
        using var host = new HostSession(new LoopbackTransport(network, "h"), new HostOptions
        {
            GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "H",
            Room = new RoomSettings { Mode = RoomMode.Endless },
        }, _ => new BrokenSimulation());
        Assert.Throws<InvalidOperationException>(() => new NetworkMatchRunner(host, TestContent.Runner()));
        host.Start(new byte[] { 1 });
        Assert.Throws<InvalidOperationException>(() => new NetworkMatchRunner(host, TestContent.Runner()));
    }

    private static SaveProblem Refused(Action resume) => Assert.Throws<SaveFileException>(resume).Problem;
}

/// <summary>A simulation that is no <see cref="LockstepMatch"/> and loads nothing.</summary>
internal sealed class BrokenSimulation : ILockstepSimulation
{
    public long Tick { get; private set; }

    public void Step(System.Collections.Generic.IReadOnlyList<PlayerCommand> commands) => Tick++;

    public uint StateHash() => 0;

    public byte[] SaveSnapshot() => [];

    public void LoadSnapshot(byte[] data) => throw new InvalidOperationException("cannot load");
}

/// <summary>Defeats one tribe and lets tribe 0 win at fixed ticks (the same on every peer).</summary>
internal sealed class TimedControl : ITribeControl
{
    private readonly TwoHumansControl inner = new();

    public int DefeatTribe { get; init; }

    public long DefeatAt { get; init; }

    public long WinAt { get; init; }

    public MatchWorld Start(Tribes.Core.Content.GameContent content, MatchPlan plan, System.Collections.Generic.IReadOnlyList<PlayerInfo> roster) =>
        inner.Start(content, plan, roster);

    public void SetPlayerState(Tribes.Core.Simulation.GameSimulation simulation, int tribe, int slot, PlayerState state)
    {
    }

    public int AddTribe(Tribes.Core.Simulation.GameSimulation simulation, int slot, int team) => -1;

    public bool IsDefeated(Tribes.Core.Simulation.GameSimulation simulation, int tribe) => tribe == DefeatTribe && simulation.Tick >= DefeatAt;

    public MatchOutcome? Outcome(Tribes.Core.Simulation.GameSimulation simulation) => simulation.Tick >= WinAt ? new MatchOutcome([0]) : null;
}
