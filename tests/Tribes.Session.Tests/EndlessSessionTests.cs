using System;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Session.Tests;

/// <summary>Endless worlds (made as they are played) through the session: alone, hosted, joined, saved and resumed.</summary>
public sealed class EndlessSessionTests
{
    private static readonly MatchPlan Plan = new() { Mission = -1, Seed = 61, Endless = new EndlessPlan(61, 0, 3, 3) };

    private readonly ITestOutputHelper output;

    public EndlessSessionTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Alone_TheWorldIsMadeAsItIsPlayed()
    {
        using LocalMatchRunner runner = LocalMatchRunner.Start(TestContent.Content,
            new LocalMatchSetup { Match = MatchKind.Endless, Seed = 9, ComputerTribes = 12, NewTribes = 1, StartAreaSize = 64 }, TestContent.Runner());

        GameSimulation world = runner.Simulation;
        Assert.True(world.IsEndless);
        Assert.True(world.World.Map.IsChunked);
        Assert.Equal(13, world.World.Tribes.Count);
        Assert.Equal(1, world.World.Endless!.NewTribes);
        Assert.Equal(0, runner.LocalTribe);
    }

    [Fact]
    [Trait("Speed", "Long")]
    public void Alone_ThousandsOfComputerTribesStartAtOnce_AndSaveWithTheirEntries()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = LocalMatchRunner.Start(TestContent.Content,
            new LocalMatchSetup { Match = MatchKind.Endless, Seed = 4, ComputerTribes = 5000, NewTribes = 0 }, TestContent.Runner(store));

        GameWorld world = runner.Simulation.World;
        int placed = world.Tribes.Count - 1;
        Assert.Equal(5000 - placed, world.Endless!.WaitingTribes);
        Assert.Equal(world.Tribes.Count, runner.Players.Count);
        Assert.All(Enumerable.Range(1, placed), t => Assert.True(runner.Players.ByTribe(t)!.IsComputer));
        for (int tick = 0; tick < 20; tick++)
        {
            runner.StepOnce();
        }
        SaveSlot slot = runner.Save("1")!;
        using LocalMatchRunner loaded = LocalMatchRunner.Load(TestContent.Content, store.Files[slot.Stem], TestContent.Runner());
        Assert.Equal(runner.Simulation.StateHash(), loaded.Simulation.StateHash());
        Assert.Equal(runner.Players.Count, loaded.Players.Count);
        Assert.Equal(world.Endless.WaitingTribes, loaded.Simulation.World.Endless!.WaitingTribes);
    }

    [Fact]
    public void Hosted_PlayersJoinMidGame_TheHostSavesAndResumes_AndHashesStayEqual()
    {
        var room = new RoomSettings { Name = "Endless", Mode = RoomMode.Endless, AllowJoinInProgress = true };
        using var cluster = new SessionCluster(MultiTribeControl.Instance, room, seed: 61, autosaveTicks: 400);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(500);
        Assert.True(cluster.HostPeer.Runner.Simulation.IsEndless);
        Assert.Equal(5, cluster.HostPeer.Runner.Simulation.World.Tribes.Count(t => t.ArrivedAtFrame == 0));

        SessionPeer late = cluster.AddClient("Late");
        cluster.RunUntil(() => late.Runner is { Role: LocalRole.Player }, "the late player to get a tribe");
        int lateTribe = late.Runner!.LocalTribe;
        Assert.True(late.Runner.Simulation.World.Map.InPlay(late.Runner.Simulation.World.Tribes[lateTribe].Camp));
        cluster.RunTicks(400);
        cluster.Settle();
        cluster.AssertInSync(3);

        long quitTick = cluster.HostPeer.Tick;
        uint savedHash = cluster.HostPeer.Runner.Lockstep.StateHash();
        cluster.HostPeer.Runner.Quit();
        cluster.RunUntil(() => guest.Stops.Count == 1 && late.Stops.Count == 1, "the clients to stop");
        byte[] save = cluster.Store.Files["endless-auto"];
        Assert.Equal(MatchKind.Endless, MatchSaveFile.ReadHeader(save).Match);

        cluster.ResumeHost(save);
        NetworkMatchRunner host = cluster.HostPeer.Runner!;
        Assert.Equal(savedHash, host.Lockstep.StateHash());
        Assert.True(host.Simulation.IsEndless);
        cluster.Connect(guest);
        cluster.Connect(late);
        cluster.RunUntil(() => guest.Runner is not null && late.Runner is not null, "the players to rejoin");
        Assert.Equal(RequestResult.Accepted, host.ContinueResumed());
        cluster.RunTicks(600);
        cluster.Settle();

        cluster.AssertInSync(3, [cluster.HostPeer, guest, late]);
        Assert.Equal(lateTribe, late.Runner!.LocalTribe);
        Assert.True(host.Simulation.Tick > quitTick + 500);
        output.WriteLine($"{host.Simulation.World.Tribes.Count} tribes, {host.Simulation.World.Map.Chunks.Count} chunks at tick {host.Simulation.Tick}");
        Assert.Empty(cluster.HostPeer.Stops);
    }
}
