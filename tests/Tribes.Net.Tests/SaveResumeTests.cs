using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class SaveResumeTests
{
    [Fact]
    public void Host_SavesQuitsAndResumes_ThenPlayersRejoin()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer a = cluster.AddClient("a", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(30) });
        TestPeer b = cluster.AddClient("b", new LoopbackLinkOptions { Latency = TimeSpan.FromMilliseconds(55) });
        int computerSlot = cluster.Host.AddComputerPlayer(aiLevel: 4);
        cluster.StartMatch([5, 6]);
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));
        int slotA = a.Session.LocalPlayer!.Slot;
        int slotB = b.Session.LocalPlayer!.Slot;
        Guid matchId = cluster.Host.Match!.MatchId;

        byte[] save = cluster.Host.SaveMatch();
        long savedTick = cluster.HostSimulation.Tick;
        uint savedHash = cluster.HostSimulation.Hash;
        SavedMatchInfo info = HostSession.ReadSaveInfo(save);
        Assert.Equal(matchId, info.MatchId);
        Assert.Equal(savedTick, info.Tick);
        Assert.Equal(4, info.Room.OccupiedSlots);
        Assert.Equal(LoopbackCluster.GameVersion, info.GameVersion);

        cluster.Host.Close();
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.Ended && b.Session.Phase == ClientPhase.Ended, "the clients to see the host quit");
        Assert.Equal(SessionEndReason.HostClosed, a.Session.EndReason);

        cluster.ReplaceHost(() => HostSession.ResumeFromSave(cluster.HostTransport, cluster.HostOptions, save, cluster.CreateHostSimulation, cluster.Clock));
        HostSession resumed = cluster.Host;
        Assert.True(resumed.IsPaused);
        Assert.Equal(matchId, resumed.Match!.MatchId);
        Assert.Equal(savedTick, resumed.Simulation!.Tick);
        Assert.Equal(savedHash, resumed.Simulation.StateHash());
        Assert.Equal(RoomPhase.InGame, resumed.Room.Phase);
        Assert.All([slotA, slotB], slot =>
        {
            PlayerInfo player = resumed.Room.PlayerInSlot(slot)!;
            Assert.Equal(PlayerPresence.Absent, player.Presence);
            Assert.False(player.IsConnected);
        });
        Assert.Equal(PlayerKind.Computer, resumed.Room.PlayerInSlot(computerSlot)!.Kind);

        cluster.Run(TimeSpan.FromSeconds(1));
        Assert.Equal(savedTick, resumed.Simulation.Tick);

        cluster.Rejoin(a);
        cluster.Rejoin(b);
        cluster.RunUntil(() => a.Session.Phase == ClientPhase.InGame && b.Session.Phase == ClientPhase.InGame, "the players to rejoin");
        Assert.True(a.Session.IsPaused);
        Assert.Equal(RequestResult.Accepted, resumed.Resume());
        cluster.RunWithCommands(TimeSpan.FromSeconds(5));

        PlayerPresenceChangedEventArgs[] absent = [.. cluster.HostPresence.Where(p => p.Change == CommandKind.PlayerAbsent)];
        Assert.Equal([slotA, slotB], absent.Select(p => p.Slot).Order());
        Assert.All(absent, p => Assert.Equal(savedTick, p.Tick));
        Assert.Equal([slotA, slotB], cluster.HostPresence.Where(p => p.Change == CommandKind.PlayerReturned).Select(p => p.Slot).Order());
        Assert.True(a.Session.CanSendCommands && b.Session.CanSendCommands);
        Assert.Contains(computerSlot, a.Simulation.ComputerPlayerSlots);
        Assert.True(a.Simulation.FirstTick >= savedTick);
        LoopbackCluster.AssertInSync(30, cluster.HostSimulation, a.Simulation, b.Simulation);
    }

    [Fact]
    public void Save_KeepsTheCommandsWaitingForTheirTick()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        Assert.Equal(RequestResult.Accepted, cluster.Host.Start());
        cluster.Run(TimeSpan.FromSeconds(1));
        long tick = cluster.HostSimulation.Tick;
        Assert.Equal(RequestResult.Accepted, cluster.Host.SendCommand([0xAB, 0xCD]));
        Assert.Equal(RequestResult.Accepted, cluster.Host.SetSpeed(150));
        byte[] save = cluster.Host.SaveMatch();
        cluster.Host.Dispose();

        cluster.ReplaceHost(() => HostSession.ResumeFromSave(cluster.HostTransport, cluster.HostOptions, save, cluster.CreateHostSimulation, cluster.Clock));
        Assert.Equal(150, cluster.Host.SpeedPercent);
        cluster.Host.Resume();
        cluster.Run(TimeSpan.FromSeconds(1));

        (long ranAt, PlayerCommand command) = Assert.Single(cluster.HostSimulation.Log, e => !e.Command.IsSystem);
        Assert.Equal(tick + 1, ranAt);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, command.Payload.ToArray());
    }

    [Fact]
    public void ResumeFromSave_RefusesOtherVersions_OtherHosts_AndDamagedData()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        cluster.Host.Start();
        cluster.Run(TimeSpan.FromMilliseconds(500));
        byte[] save = cluster.Host.SaveMatch();
        cluster.Host.Dispose();
        var transport = new LoopbackTransport(cluster.Network, "other");

        Assert.Throws<ArgumentException>(() => HostSession.ResumeFromSave(
            transport, cluster.HostOptions with { GameVersion = "2.0.0" }, save, cluster.CreateHostSimulation, cluster.Clock));
        Assert.Throws<ArgumentException>(() => HostSession.ResumeFromSave(
            transport, cluster.HostOptions with { PlayerId = Guid.NewGuid() }, save, cluster.CreateHostSimulation, cluster.Clock));
        Assert.Throws<ProtocolException>(() => HostSession.ResumeFromSave(
            transport, cluster.HostOptions, save.AsSpan(0, save.Length / 2), cluster.CreateHostSimulation, cluster.Clock));
        byte[] wrongMagic = [.. save];
        wrongMagic[0] = (byte)'X';
        Assert.Throws<ProtocolException>(() => HostSession.ReadSaveInfo(wrongMagic));
        byte[] wrongFormat = [.. save];
        wrongFormat[4] = 9;
        Assert.Throws<ProtocolException>(() => HostSession.ReadSaveInfo(wrongFormat));
        Assert.Throws<ProtocolException>(() => HostSession.ReadSaveInfo([1, 2]));
        Assert.Equal(Net.Transport.TransportRole.None, transport.Role);
    }

    [Fact]
    public void Resume_FailsClearly_WhenTheSimulationLoadsTheWrongTick()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        cluster.Host.Start();
        cluster.Run(TimeSpan.FromMilliseconds(500));
        byte[] save = cluster.Host.SaveMatch();
        cluster.Host.Dispose();

        var error = Assert.Throws<InvalidOperationException>(() => HostSession.ResumeFromSave(
            cluster.HostTransport, cluster.HostOptions, save, start => new BrokenSimulation(start), cluster.Clock));
        Assert.Contains("tick", error.Message, StringComparison.Ordinal);
    }

    private sealed class BrokenSimulation(MatchStart start) : ILockstepSimulation
    {
        private readonly HashChainSimulation inner = new(start);

        public long Tick => inner.Tick + 1;

        public void Step(System.Collections.Generic.IReadOnlyList<PlayerCommand> commands) => inner.Step(commands);

        public uint StateHash() => inner.StateHash();

        public byte[] SaveSnapshot() => inner.SaveSnapshot();

        public void LoadSnapshot(byte[] data) => inner.LoadSnapshot(data);
    }
}
