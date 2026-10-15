using System;
using System.Collections.Generic;
using Tribes.Core.Commands;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>An endless world played alone, opened to network players mid-game.</summary>
public sealed class OpenToNetworkTests
{
    private static LocalMatchRunner Endless() => LocalMatchRunner.Start(TestContent.Content,
        new LocalMatchSetup { Match = MatchKind.Endless, Seed = 77, PlayerName = "Solo", Colour = 5 }, TestContent.Runner());

    [Fact]
    public void LocalEndlessWorld_GoesOnAsAHostedMatch_AndAPlayerJoins()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), seed: 31);
        cluster.Host.Close();
        var switcher = new RunnerSwitch(Endless());
        var script = new ScriptedOrders(4);
        for (int i = 0; i < 300; i++)
        {
            if (script.Next(switcher.Simulation, switcher.LocalTribe) is { } order)
            {
                switcher.Orders.Submit(order);
            }
            ((LocalMatchRunner)switcher.Current).StepOnce();
        }
        ulong worldHash = switcher.Simulation.StateHash();
        long tick = switcher.Simulation.Tick;
        var events = new List<string>();
        switcher.SimulationReplaced += (_, _) => events.Add("replaced");
        switcher.Stopped += (_, e) => events.Add("stopped " + e.Stop.Reason);
        switcher.PhaseChanged += (_, _) => events.Add("phase");
        Guid key = Guid.NewGuid();
        HostOptions options = cluster.HostOptions(cluster.HostPeer) with { PlayerId = key, Room = new RoomSettings { Name = "Was alone" } };
        IMatchRunner local = switcher.Current;

        IMatchRunner hosted = switcher.SwitchTo(old => NetworkMatchRunner.HostLocalGame((LocalMatchRunner)old,
            new LoopbackTransport(cluster.Network, SessionCluster.HostAddress), options, cluster.Factory, cluster.RunnerOptions(cluster.HostPeer), cluster.Clock));

        Assert.Equal(["replaced", "phase"], events);
        Assert.Equal(StopReason.HandedOver, local.StopInfo!.Reason);
        Assert.Same(hosted, switcher.Current);
        Assert.True(switcher.IsNetwork && switcher.IsHost);
        Assert.Equal(MatchKind.Endless, switcher.Match);
        Assert.Equal(tick, switcher.Simulation.Tick);
        Assert.Equal(worldHash, switcher.Simulation.StateHash());
        Assert.False(switcher.Simulation.Options.HintsPauseGame);
        Assert.Equal(0, switcher.LocalTribe);
        Assert.Equal(5, switcher.Players.BySlot(0)!.Colour);
        var network = (NetworkMatchRunner)hosted;
        Assert.True(network.IsInviteOpen);
        cluster.AdoptHost(network, key);

        SessionPeer friend = cluster.AddClient("Friend");
        cluster.RunUntil(() => friend.Runner is not null, "the friend to join");
        cluster.RunTicks(300);
        cluster.Settle();
        Assert.Contains(cluster.HostPeer.Notices, n => n.Kind == MatchNoticeKind.PlayerJoined && n.Player!.Name == "Friend");
        Assert.Equal(switcher.Players.Count, friend.Runner!.Players.Count);
        Assert.True(switcher.Simulation.Tick > tick + 250);
        cluster.AssertInSync(2);
        Assert.NotNull(switcher.Save("3"));
        Assert.Equal(SaveKind.HostedMatch, MatchSaveFile.ReadHeader(cluster.Store.Files["endless-3"]).Kind);
    }

    [Fact]
    public void OnlyRunningEndlessGames_CanBeOpened()
    {
        var network = new LoopbackNetwork(new ManualClock(), 1, false);
        var options = new HostOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "H" };
        using LocalMatchRunner custom = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8 }, TestContent.Runner());
        Assert.Throws<InvalidOperationException>(() => NetworkMatchRunner.HostLocalGame(custom, new LoopbackTransport(network, "a"), options, LockstepMatch.Factory(TestContent.Content), TestContent.Runner()));
        LocalMatchRunner stopped = Endless();
        stopped.Quit();
        Assert.Throws<InvalidOperationException>(() => NetworkMatchRunner.HostLocalGame(stopped, new LoopbackTransport(network, "b"), options, LockstepMatch.Factory(TestContent.Content), TestContent.Runner()));
        using LocalMatchRunner endless = Endless();
        Assert.Throws<InvalidOperationException>(() => NetworkMatchRunner.HostLocalGame(endless, new LoopbackTransport(network, "c"), options, _ => throw new InvalidOperationException("no rules"), TestContent.Runner()));
        Assert.Equal(MatchPhase.Running, endless.Phase);
        Assert.Throws<ArgumentNullException>(() => NetworkMatchRunner.HostLocalGame(null!, new LoopbackTransport(network, "d"), options, LockstepMatch.Factory(TestContent.Content), TestContent.Runner()));
    }

    [Fact]
    public void Switch_ForwardsEverything_AndKeepsTheRunnerWhenOpeningFails()
    {
        var store = new MemorySaveStore();
        LocalMatchRunner local = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Endless }, TestContent.Runner(store));
        using var switcher = new RunnerSwitch(local);
        var seen = new List<string>();
        switcher.TickRan += (_, _) => seen.Add("tick");
        switcher.Notice += (_, _) => seen.Add("notice");
        switcher.Stopped += (_, _) => seen.Add("stopped");
        switcher.PlayersChanged += (_, _) => seen.Add("players");
        switcher.MatchOver += (_, _) => seen.Add("over");

        Assert.Throws<InvalidOperationException>(() => switcher.SwitchTo(_ => throw new InvalidOperationException("no network")));
        Assert.Same(local, switcher.Current);
        Assert.Equal(local.Phase, switcher.Phase);
        Assert.Same(local.Orders, switcher.Orders);
        Assert.Equal((local.LocalPlayerId, local.LocalTribe, local.ViewTribe, local.Role), (switcher.LocalPlayerId, switcher.LocalTribe, switcher.ViewTribe, switcher.Role));
        Assert.Equal((local.IsPaused, local.CanPause, local.CanChangeSpeed), (switcher.IsPaused, switcher.CanPause, switcher.CanChangeSpeed));
        Assert.Equal(RequestResult.Accepted, switcher.SetSpeed(200));
        Assert.Equal(200, switcher.SpeedPercent);
        Assert.Equal(local.TickSeconds, switcher.TickSeconds);
        switcher.SetHeld(true);
        Assert.True(switcher.IsPaused);
        switcher.SetHeld(false);
        Assert.Equal(RequestResult.Accepted, switcher.SetPaused(false));
        switcher.RosterOpen = true;
        Assert.True(local.RosterOpen && switcher.RosterOpen);
        switcher.Update(0.25);
        Assert.Contains("tick", seen);
        Assert.InRange(switcher.TickAlpha, 0, 1);
        Assert.Same(local.Players, switcher.Players);
        Assert.Equal(RequestResult.NotAllowedNow, switcher.Forfeit());
        Assert.NotNull(switcher.Save("1"));
        Assert.Null(switcher.Outcome);
        Assert.Null(switcher.StopInfo);
        Assert.False(switcher.IsNetwork);
        Assert.True(switcher.IsHost);
        Assert.Equal(MatchKind.Endless, switcher.Match);
        Assert.Throws<ArgumentNullException>(() => switcher.SwitchTo(null!));
        Assert.Throws<ArgumentNullException>(() => new RunnerSwitch(null!));

        switcher.Quit();
        Assert.Contains("stopped", seen);
        Assert.Equal(StopReason.Quit, switcher.StopInfo!.Reason);
        Assert.Same(local.Simulation, switcher.Simulation);
    }

    [Fact]
    public void ContinuingPlan_RoundTripsTheWorld_AndRejectsBadWorlds()
    {
        using LocalMatchRunner local = Endless();
        for (int i = 0; i < 50; i++)
        {
            local.StepOnce();
        }
        MatchPlan plan = MatchPlan.Read(MatchPlan.Continuing(local.Simulation).ToBytes());
        Tribes.Core.Simulation.GameSimulation world = plan.LoadWorld(TestContent.Content)!;
        Assert.Equal(local.Simulation.StateHash(), world.StateHash());
        Assert.Equal(local.Simulation.Tick, world.Tick);
        Assert.Null(new MatchPlan().LoadWorld(TestContent.Content));
        Assert.Throws<System.IO.InvalidDataException>(() => (plan with { World = [1, 2, 3, 4] }).LoadWorld(TestContent.Content));
        Assert.Throws<ArgumentNullException>(() => MatchPlan.Continuing(null!));
        Assert.Throws<ArgumentNullException>(() => OriginalTribeControl.NewWorld(null!, plan));
        Assert.Throws<ArgumentNullException>(() => OriginalTribeControl.NewWorld(TestContent.Content, null!));

        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(plan, TestContent.Human(0, "Solo")));
        Assert.Equal(local.Simulation.Tick, match.Tick);
        Assert.Equal(0, match.Players.BySlot(0)!.Tribe);
        match.Step([new PlayerCommand(0, CommandKind.Game, GameOrderCodec.Encode(new SetHints(0, false)))]);
        Assert.Equal(local.Simulation.Tick + 1, match.Tick);
    }
}
