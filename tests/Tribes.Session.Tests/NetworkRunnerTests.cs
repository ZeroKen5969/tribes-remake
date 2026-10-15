using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>Details of the network runner: orders, notices, the host giving up.</summary>
public sealed class NetworkRunnerTests
{
    private static readonly MatchPlan Plan = new() { Mission = 8, Difficulty = 0, Seed = 17 };

    [Fact]
    public void HostForfeits_KeepsHostingAndSaving_TheOthersPlayOn()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 21);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        NetworkMatchRunner host = cluster.HostPeer.Runner!;
        host.SetSpeed(400);
        cluster.RunTicks(100);

        Assert.Equal(RequestResult.Accepted, host.Forfeit());
        cluster.RunUntil(() => host.Role == LocalRole.Forfeited && guest.Runner!.Players.BySlot(0)!.State == PlayerState.Forfeited, "the host's forfeit");
        Assert.True(host.IsHost);
        Assert.False(host.Orders.CanSubmit);
        Assert.Equal(0, host.ViewTribe);
        Assert.Contains(guest.Notices, n => n.Kind == MatchNoticeKind.PlayerForfeited && n.Player!.Slot == 0);
        int answered = guest.Answered;
        cluster.RunTicks(300);
        cluster.Settle();
        Assert.True(guest.Answered > answered);
        Assert.NotNull(host.Save("1"));
        cluster.AssertNoStops();
        cluster.AssertInSync(3);
    }

    [Fact]
    public void HostThatLostTheRoom_SavesAndStops_WhileAClientIgnoresTheReport()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 23);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        NetworkMatchRunner host = cluster.HostPeer.Runner!;
        cluster.RunTicks(50);

        // A client is not hosting: the report means nothing to it.
        guest.Runner!.ReportHostingLost();
        cluster.RunTicks(10);
        Assert.Empty(guest.Stops);

        host.ReportHostingLost();
        cluster.RunUntil(() => cluster.HostPeer.Stops.Count == 1, "the host to stop");

        MatchStop stop = cluster.HostPeer.Stops[0];
        Assert.Equal(StopReason.ConnectionLost, stop.Reason);
        Assert.Equal(MatchPhase.Stopped, host.Phase);
        Assert.True(cluster.Store.Files.ContainsKey("versus-auto"), "The host saves before it stops.");
        Assert.Contains(cluster.HostPeer.Log, l => l.Contains("lost its connection while hosting", StringComparison.Ordinal));
    }

    [Fact]
    public void Orders_ForAnotherTribe_OrTooMany_AreRefused_AndReported()
    {
        // Two commands per tick and per player, aimed at most 2 x 3 + 1 ticks ahead.
        var lockstep = new LockstepOptions { MaxCommandsPerPlayerPerTick = 2, MaxInputDelayTicks = 3 };
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 22, lockstep: lockstep);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Plays = false;
        guest.Plays = false;
        NetworkMatchRunner host = cluster.HostPeer.Runner!, client = guest.Runner!;

        Assert.Equal(0, host.Orders.Tribe);
        Assert.Equal(OrderSubmitResult.WrongTribe, host.Orders.Submit(new SetHints(1, true)));
        Assert.Equal(OrderSubmitResult.WrongTribe, client.Orders.Submit(new SetHints(0, true)));
        Assert.Throws<ArgumentNullException>(() => host.Orders.Submit(null!));

        // The host's own commands beyond a tick's share go into the next ticks; beyond the latest
        // tick a command may aim at (7 ticks of 2) they are refused on the spot.
        var results = Enumerable.Range(0, 20).Select(_ => host.Orders.Submit(new SetHints(0, true))).ToList();
        Assert.Equal(14, results.Count(r => r == OrderSubmitResult.Queued));
        Assert.Contains(OrderSubmitResult.Refused, results);

        // A client's commands beyond that are dropped by the host, which tells the client.
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(OrderSubmitResult.Queued, client.Orders.Submit(new SetHints(1, i % 2 == 0)));
        }
        cluster.RunUntil(() => guest.Notices.Any(n => n.Kind == MatchNoticeKind.OrderDropped), "the client to hear of dropped orders");
        cluster.RunTicks(10);
        Assert.Equal(14, cluster.HostPeer.Answered);
        Assert.InRange(guest.Answered, 1, 19);
    }

    [Fact]
    public void ConnectionLoss_IsReported_AndTheClientComesBack()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 23);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(50);

        guest.Transport!.SimulateConnectionLoss();
        cluster.RunUntil(() => guest.Notices.Any(n => n.Kind == MatchNoticeKind.Reconnecting), "the reconnect notice");
        cluster.RunUntil(() => guest.Runner!.Client!.Phase == ClientPhase.InGame && cluster.HostPeer.Runner.Players.BySlot(1)!.State == PlayerState.Playing, "the client to be back", TimeSpan.FromSeconds(60));
        cluster.RunTicks(100);
        cluster.Settle();
        Assert.Empty(guest.Stops);
        cluster.AssertInSync(2);
    }

    [Fact]
    public void DeniedRequests_BecomeNotices()
    {
        var room = new RoomSettings { Name = "Pause", AllowClientPause = true };
        using var cluster = new SessionCluster(new TwoHumansControl(), room, seed: 24);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        NetworkMatchRunner client = guest.Runner!;
        Assert.Equal(RequestResult.Accepted, client.Forfeit());
        cluster.RunUntil(() => client.Role == LocalRole.Forfeited && cluster.Host.Room.FindPlayer(client.LocalPlayerId)!.Presence == PlayerPresence.Forfeited, "the forfeit");
        Assert.False(client.CanPause);

        // Asking anyway (as an outdated screen might): the host says no.
        client.Client!.RequestPause();
        cluster.RunUntil(() => guest.Notices.Any(n => n.Kind == MatchNoticeKind.RequestDenied), "the denial");
        Assert.False(cluster.HostPeer.Runner!.IsPaused);
    }

    [Fact]
    public void Autosave_TakenWhileRunning_ResumesAtItsTickWithTheSameState()
    {
        byte[] save;
        long savedTick = 0;
        uint savedHash = 0;
        Guid hostKey;
        using (var cluster = new SessionCluster(new TwoHumansControl(), seed: 26, autosaveTicks: 250))
        {
            SessionPeer guest = cluster.AddClient("Guest");
            cluster.StartMatch(Plan);
            NetworkMatchRunner host = cluster.HostPeer.Runner!;
            host.SetSpeed(400);
            host.Notice += (_, e) =>
            {
                if (e.Notice.Kind == MatchNoticeKind.Autosaved && savedTick == 0)
                {
                    savedTick = host.Simulation.Tick;
                    savedHash = host.Lockstep.StateHash();
                }
            };
            cluster.RunTicks(300);
            Assert.True(savedTick >= 250);
            save = cluster.Store.Files["versus-auto"];
            hostKey = cluster.HostPeer.Key;
            Assert.Equal(savedTick, MatchSaveFile.ReadHeader(save).Tick);
        }

        using var again = new SessionCluster(new TwoHumansControl(), seed: 27);
        again.Host.Close();
        again.Host.Dispose();
        again.ResumeHost(save, hostKey);
        Assert.Equal(savedTick, again.HostPeer.Runner!.Simulation.Tick);
        Assert.Equal(savedHash, again.HostPeer.Runner.Lockstep.StateHash());
        Assert.Equal(RequestResult.Accepted, again.HostPeer.Runner.ContinueResumed());
        again.RunTicks(50);
        Assert.True(again.HostPeer.Runner.Simulation.Tick > savedTick);
    }

    [Fact]
    public void Constructors_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new NetworkMatchRunner((HostSession)null!, TestContent.Runner()));
        Assert.Throws<ArgumentNullException>(() => new NetworkMatchRunner((ClientSession)null!, TestContent.Runner()));
    }

    [Fact]
    public void ResumedHost_SeesWhoIsBack_AndFirstTicksMarkTheMissingAsAbsent()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 25);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        cluster.RunTicks(30);
        byte[] save = cluster.Store.Files[cluster.HostPeer.Runner!.Save("2")!.Stem];
        cluster.HostPeer.Runner.Quit();
        cluster.RunUntil(() => guest.Stops.Count == 1, "the guest to stop");

        cluster.ResumeHost(save);
        NetworkMatchRunner host = cluster.HostPeer.Runner!;
        ExpectedPlayer expected = Assert.Single(host.ExpectedPlayers);
        Assert.Equal("Guest", expected.Player.Name);
        Assert.False(expected.IsConnected);
        Assert.Equal(1, host.TickAlpha);
        Assert.Equal(RequestResult.Accepted, host.ContinueResumed());
        cluster.RunTicks(20);
        Assert.Equal(PlayerState.Absent, host.Players.BySlot(1)!.State);
        Assert.Contains(cluster.HostPeer.Notices, n => n.Kind == MatchNoticeKind.PlayerAbsent);
    }
}

/// <summary>Rules that fail at a given tick, as a rules bug would on every peer.</summary>
internal sealed class FailingControl(long failAt) : ITribeControl
{
    private readonly TwoHumansControl inner = new();

    public MatchWorld Start(Tribes.Core.Content.GameContent content, MatchPlan plan, System.Collections.Generic.IReadOnlyList<PlayerInfo> roster) =>
        inner.Start(content, plan, roster);

    public void SetPlayerState(Tribes.Core.Simulation.GameSimulation simulation, int tribe, int slot, PlayerState state)
    {
    }

    public int AddTribe(Tribes.Core.Simulation.GameSimulation simulation, int slot, int team) => -1;

    public bool IsDefeated(Tribes.Core.Simulation.GameSimulation simulation, int tribe) =>
        simulation.Tick >= failAt ? throw new ArgumentOutOfRangeException(nameof(tribe), "rules bug") : false;

    public MatchOutcome? Outcome(Tribes.Core.Simulation.GameSimulation simulation) => null;
}
