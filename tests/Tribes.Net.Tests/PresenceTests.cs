using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class PresenceTests
{
    [Fact]
    public void JoinInProgress_EndlessMatch_LoadsSnapshotAndCatchesUp()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer first = cluster.AddClient("first", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(30) });
        cluster.StartMatch([7]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(10));
        long joinedAround = cluster.HostSimulation.Tick;

        TestPeer late = cluster.AddClient("late", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(60), Jitter = TimeSpan.FromMilliseconds(20) });
        cluster.RunUntil(() => late.Session.Phase == ClientPhase.InGame, "the late player to load");
        cluster.RunWithCommands(TimeSpan.FromSeconds(6));

        Assert.Contains(ClientPhase.Loading, late.Phases);
        MatchStartedEventArgs started = Assert.Single(late.MatchStarts);
        Assert.True(started.FromSnapshot);
        Assert.True(late.Simulation.FirstTick >= joinedAround);
        PlayerInfo player = late.Session.LocalPlayer!;
        Assert.Equal(PlayerPresence.Active, player.Presence);
        Assert.True(late.Session.CanSendCommands);
        Assert.Contains(late.Simulation.Log, e => e.Command.Slot == player.Slot && !e.Command.IsSystem);

        // Every peer saw the new tribe arrive at the same tick.
        PlayerPresenceChangedEventArgs hostSaw = Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerJoined);
        PlayerPresenceChangedEventArgs firstSaw = Assert.Single(first.Presence, p => p.Change == CommandKind.PlayerJoined);
        PlayerPresenceChangedEventArgs lateSaw = Assert.Single(late.Presence, p => p.Change == CommandKind.PlayerJoined);
        Assert.Equal(hostSaw.Tick, firstSaw.Tick);
        Assert.Equal(hostSaw.Tick, lateSaw.Tick);
        Assert.Equal(late.PublicId, hostSaw.Player!.Id);
        Assert.Equal(player.Slot, hostSaw.Slot);
        Assert.NotEqual(first.Session.LocalPlayer!.Colour, player.Colour);
        LoopbackCluster.AssertInSync(40, cluster.HostSimulation, first.Simulation, late.Simulation);
    }

    [Fact]
    public void JoinInProgress_IsRejected_InVersus_ButSpectatorsMayWatch()
    {
        using var cluster = new LoopbackCluster(RoomMode.Versus);
        TestPeer player = cluster.AddClient("player");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        TestPeer intruder = cluster.AddClient("intruder");
        TestPeer watcher = cluster.AddClient("watcher", configure: o => o with { Spectate = true });
        cluster.RunUntil(() => intruder.Session.Phase == ClientPhase.Ended && watcher.Session.Phase == ClientPhase.InGame, "the late peers");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Equal(SessionEndReason.Rejected, intruder.Session.EndReason);
        Assert.Equal(RejectReason.MatchInProgress, intruder.Session.Rejection);
        Assert.Equal(PlayerPresence.Spectator, watcher.Session.LocalPlayer!.Presence);
        Assert.False(watcher.Session.CanSendCommands);
        Assert.False(watcher.Session.SendCommand([1]));
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, player.Simulation, watcher.Simulation);
    }

    [Fact]
    public void Leaving_MakesThePlayerAbsent_AndRejoiningMakesThemActive()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer stays = cluster.AddClient("stays", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(25) });
        TestPeer leaves = cluster.AddClient("leaves", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(45) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));
        int slot = leaves.Session.LocalPlayer!.Slot;

        leaves.Session.Leave();
        Assert.Equal(SessionEndReason.Left, leaves.Session.EndReason);
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        PlayerInfo absent = cluster.Host.Room.PlayerInSlot(slot)!;
        Assert.Equal(PlayerPresence.Absent, absent.Presence);
        Assert.False(absent.IsConnected);
        Assert.Contains(slot, cluster.HostSimulation.AwaySlots);
        Assert.Contains(slot, stays.Simulation.AwaySlots);
        PlayerPresenceChangedEventArgs hostAbsent = Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerAbsent);
        PlayerPresenceChangedEventArgs staysAbsent = Assert.Single(stays.Presence, p => p.Change == CommandKind.PlayerAbsent);
        Assert.Equal(hostAbsent.Tick, staysAbsent.Tick);
        Assert.Equal(PlayerPresence.Absent, hostAbsent.Presence);

        cluster.Rejoin(leaves);
        cluster.RunUntil(() => leaves.Session.Phase == ClientPhase.InGame, "the player to return");
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Equal(PlayerPresence.Active, cluster.Host.Room.PlayerInSlot(slot)!.Presence);
        Assert.Equal(slot, leaves.Session.LocalPlayer!.Slot);
        Assert.True(leaves.Session.CanSendCommands);
        Assert.DoesNotContain(slot, cluster.HostSimulation.AwaySlots);
        Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned);
        Assert.Single(stays.Presence, p => p.Change == CommandKind.PlayerReturned);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, stays.Simulation, leaves.Simulation);
    }

    [Fact]
    public void ConnectionLoss_Reconnects_FromTheMissedBatches()
    {
        using var cluster = new LoopbackCluster();
        TestPeer other = cluster.AddClient("other");
        TestPeer flaky = cluster.AddClient(
            "flaky",
            new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(30), LossDetectionDelay = TimeSpan.FromMilliseconds(500) },
            o => o with { ReconnectDelay = TimeSpan.FromSeconds(1) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        flaky.Transport.SimulateConnectionLoss();
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.Reconnecting, "the loss to be noticed");
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.InGame, "the reconnect");
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Single(flaky.Simulations);
        Assert.Single(flaky.MatchStarts);
        Assert.Empty(flaky.Ends);
        Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerAbsent);
        Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned);
        Assert.True(flaky.Session.CanSendCommands);
        LoopbackCluster.AssertInSync(60, cluster.HostSimulation, other.Simulation, flaky.Simulation);
    }

    [Fact]
    public void ConnectionLoss_Reconnects_FromASnapshot_WhenTheHistoryIsGone()
    {
        using var cluster = new LoopbackCluster(configure: o => o with { Lockstep = new LockstepOptions { BatchHistoryTicks = 0 } });
        TestPeer flaky = cluster.AddClient("flaky", configure: o => o with { ReconnectDelay = TimeSpan.FromMilliseconds(300) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        flaky.Transport.SimulateConnectionLoss(flaky.Transport.Connections.Single());
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.Reconnecting, "the loss to be noticed");
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.InGame, "the reconnect");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Equal(2, flaky.Simulations.Count);
        Assert.True(flaky.MatchStarts[^1].FromSnapshot);
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, flaky.Simulation);
    }

    [Fact]
    public void RepeatedRandomConnectionLoss_StillEndsInSync()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless, seed: 5);
        TestPeer flaky = cluster.AddClient(
            "flaky",
            new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(20), ConnectionLossChance = 0.01 },
            o => o with { ReconnectAttempts = 50, ReconnectDelay = TimeSpan.FromMilliseconds(200) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(20));
        flaky.Transport.Link = new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(20) };
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.InGame, "the last reconnect", TimeSpan.FromSeconds(20));
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.True(cluster.HostPresence.Count(p => p.Change == CommandKind.PlayerAbsent) >= 2, "The link should have dropped several times.");
        Assert.Equal(PlayerPresence.Active, flaky.Session.LocalPlayer!.Presence);
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, flaky.Simulation);
    }

    [Fact]
    public void Reconnect_GivesUp_AfterTheAllowedAttempts()
    {
        using var cluster = new LoopbackCluster();
        TestPeer flaky = cluster.AddClient("flaky", configure: o => o with { ReconnectAttempts = 2, ReconnectDelay = TimeSpan.FromMilliseconds(100) });
        cluster.StartMatch();
        cluster.Host.Close();
        cluster.RunUntil(() => flaky.Session.Phase == ClientPhase.Ended, "the session to end");

        Assert.Equal(SessionEndReason.HostClosed, flaky.Session.EndReason);

        using var second = new LoopbackCluster(hostMigration: false);
        TestPeer lost = second.AddClient("lost", configure: o => o with { ReconnectAttempts = 2, ReconnectDelay = TimeSpan.FromMilliseconds(100) });
        second.StartMatch();
        second.HostTransport.Shutdown();
        second.RunUntil(() => lost.Session.Phase == ClientPhase.Ended, "the reconnects to fail");

        Assert.Equal(SessionEndReason.ConnectionLost, lost.Session.EndReason);
        Assert.True(lost.Phases.Count(p => p == ClientPhase.Reconnecting) >= 1);
    }

    [Fact]
    public void Forfeit_IsFinal_AndRejoiningMeansWatching()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer quitter = cluster.AddClient("quitter");
        TestPeer other = cluster.AddClient("other");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        int slot = quitter.Session.LocalPlayer!.Slot;

        Assert.Equal(RequestResult.Accepted, quitter.Session.Forfeit());
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Equal(PlayerPresence.Forfeited, quitter.Session.LocalPlayer!.Presence);
        Assert.False(quitter.Session.CanSendCommands);
        Assert.False(quitter.Session.SendCommand([1]));
        Assert.Equal(RequestResult.NotAllowedNow, quitter.Session.Forfeit());
        PlayerPresenceChangedEventArgs hostSaw = Assert.Single(cluster.HostPresence, p => p.Change == CommandKind.PlayerForfeited);
        Assert.Equal(hostSaw.Tick, Assert.Single(other.Presence, p => p.Change == CommandKind.PlayerForfeited).Tick);
        Assert.Contains(slot, other.Simulation.ForfeitedSlots);

        quitter.Session.Leave();
        cluster.Run(TimeSpan.FromSeconds(1));
        cluster.Rejoin(quitter);
        cluster.RunUntil(() => quitter.Session.Phase == ClientPhase.InGame, "the rejoin");
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        Assert.Equal(PlayerPresence.Forfeited, quitter.Session.LocalPlayer!.Presence);
        Assert.False(quitter.Session.CanSendCommands);
        Assert.DoesNotContain(cluster.HostPresence, p => p.Change == CommandKind.PlayerReturned);
        Assert.DoesNotContain(cluster.HostSimulation.Log, e => e.Command.Slot == slot && !e.Command.IsSystem && e.Tick > hostSaw.Tick);
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, other.Simulation, quitter.Simulation);
    }

    [Fact]
    public void HostForfeit_KeepsTheMatchRunning_WithTheHostWatching()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer a = cluster.AddClient("a");
        TestPeer b = cluster.AddClient("b");
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));

        Assert.Equal(RequestResult.Accepted, cluster.Host.Forfeit());
        Assert.Equal(RequestResult.NotAllowedNow, cluster.Host.Forfeit());
        long tick = cluster.HostSimulation.Tick;
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        Assert.Equal(PlayerPresence.Forfeited, cluster.Host.LocalPlayer.Presence);
        Assert.Equal(RequestResult.NotAPlayer, cluster.Host.SendCommand([1]));
        Assert.True(cluster.HostSimulation.Tick > tick + 25);
        Assert.Contains(0, a.Simulation.ForfeitedSlots);
        Assert.True(a.Session.CanSendCommands && b.Session.CanSendCommands);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, a.Simulation, b.Simulation);
    }

    [Fact]
    public void Defeat_MarksAWipedOutTribe_AsForfeited()
    {
        using var cluster = new LoopbackCluster();
        TestPeer a = cluster.AddClient("a");
        cluster.StartMatch();
        int slot = a.Session.LocalPlayer!.Slot;

        Assert.Equal(RequestResult.Accepted, cluster.Host.Defeat(slot));
        Assert.Equal(RequestResult.NotFound, cluster.Host.Defeat(3));
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Equal(PlayerPresence.Forfeited, a.Session.LocalPlayer!.Presence);
        Assert.False(a.Session.CanSendCommands);
        Assert.Contains(slot, a.Simulation.ForfeitedSlots);
    }

    [Fact]
    public void SecondConnection_OfTheSamePlayer_ReplacesTheFirst()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer original = cluster.AddClient("original");
        cluster.StartMatch();
        cluster.Run(TimeSpan.FromSeconds(1));

        TestPeer twin = cluster.AddClient("twin", id: original.Id);
        cluster.RunUntil(() => twin.Session.Phase == ClientPhase.InGame, "the second connection");
        cluster.Run(TimeSpan.FromSeconds(1));

        Assert.Equal(SessionEndReason.ReplacedByNewConnection, original.Session.EndReason);
        Assert.True(twin.Session.CanSendCommands);
        Assert.DoesNotContain(cluster.HostPresence, p => p.Change == CommandKind.PlayerAbsent);
        Assert.Equal("twin", cluster.Host.Room.FindPlayer(twin.PublicId)!.Name);
    }
}
