using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Session.Tests;

/// <summary>Matches on the multi-tribe rules: a tribe for every slot, controllers kept by the rules, joining tribes.</summary>
public sealed class MultiTribeTests(ITestOutputHelper output)
{
    private static readonly MatchPlan Versus = new() { Mission = 8, Difficulty = 0, Seed = 41 };

    private static PlayerCommand System(int slot, CommandKind kind) => new(slot, kind, ReadOnlyMemory<byte>.Empty);

    [Fact]
    public void EverySlotHolder_PlaysATribeOfItsOwn_WithItsTeam()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), cpu = TestContent.Computer(1, level: 1), guest = TestContent.Human(2, "Guest") with { Team = 0 };
        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(Versus, host, cpu, guest, TestContent.Spectator("Eve")));

        Assert.True(match.Simulation.IsMatch);
        Assert.Equal(3, match.Simulation.World.Tribes.Count);
        Assert.Equal(new[] { 0, 1, 2 }, new[] { 0, 1, 2 }.Select(s => match.Players.BySlot(s)!.Tribe).ToArray());
        GameWorld world = match.Simulation.World;
        Assert.Equal(TribeController.Human(0), world.Tribes[0].Controller);
        Assert.Equal(TribeControl.Computer, world.Tribes[1].Controller.Kind);
        Assert.Equal(TribeController.Human(2), world.Tribes[2].Controller);
        Assert.True(world.Relations.AreAllies(0, 2));
        Assert.True(world.Relations.AreEnemies(0, 1));
        Assert.False(match.Simulation.Options.HintsPauseGame);
    }

    [Fact]
    public void PresenceChanges_ReachTheRules_AndAForfeitIsFinal()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest"), third = TestContent.Human(2, "Third");
        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(Versus, host, guest, third));
        GameWorld world() => match.Simulation.World;

        match.Step([System(1, CommandKind.PlayerAbsent)]);
        Assert.Equal(TribeController.Absent(1), world().Tribes[1].Controller);
        match.Step([System(1, CommandKind.PlayerReturned)]);
        Assert.Equal(TribeController.Human(1), world().Tribes[1].Controller);
        match.Step([System(2, CommandKind.PlayerForfeited)]);
        Assert.Equal(TribeController.Forfeited(2), world().Tribes[2].Controller);
        Assert.True(match.Simulation.IsDefeated(2));
        Assert.Equal(PlayerState.Forfeited, match.Players.BySlot(2)!.State);
        match.Step([System(2, CommandKind.PlayerReturned), System(2, CommandKind.PlayerAbsent)]);
        Assert.Equal(TribeController.Forfeited(2), world().Tribes[2].Controller);

        // The rules refuse orders of an absent tribe too (the table already drops them).
        MultiTribeControl.Instance.SetPlayerState(match.Simulation, 1, 1, PlayerState.Absent);
        Assert.Equal(OrderOutcome.NotAvailable, match.Simulation.Step(Tribes.Core.Simulation.TickInput.WithOrders(new SetHints(1, true))).Outcomes[0]);
        MultiTribeControl.Instance.SetPlayerState(match.Simulation, 1, 1, PlayerState.Playing);
        MultiTribeControl.Instance.SetPlayerState(match.Simulation, 1, 1, PlayerState.Out);
        MultiTribeControl.Instance.SetPlayerState(match.Simulation, 9, 1, PlayerState.Absent);
        Assert.Equal(TribeController.Human(1), world().Tribes[1].Controller);
    }

    [Fact]
    public void LastSideStanding_Wins()
    {
        PlayerInfo host = TestContent.Human(0, "Host"), guest = TestContent.Human(1, "Guest");
        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(Versus, host, guest));
        LockstepTickEventArgs? decided = null;
        match.Stepped += (_, e) => decided ??= e.Decided is null ? null : e;

        match.Step([System(1, CommandKind.PlayerForfeited)]);
        for (int i = 0; i < 20 && decided is null; i++)
        {
            match.Step([]);
        }
        Assert.NotNull(decided);
        Assert.True(match.Outcome!.Won(0));
        Assert.False(match.Outcome.Won(1));
    }

    [Fact]
    public void EndlessWorld_AddsItsComputerTribes_AndNeverEnds()
    {
        var plan = new MatchPlan { Mission = -1, Seed = 9, Endless = new EndlessPlan(9, 32, 2, 1) };
        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(plan, TestContent.Human(0, "Host"), TestContent.Human(1, "Guest")));
        int tribes = match.Simulation.World.Tribes.Count;
        output.WriteLine($"endless world with {tribes} tribes");
        Assert.InRange(tribes, 3, 4);
        PlayerEntry[] computers = [.. match.Players.Where(p => p.IsComputer)];
        Assert.Equal(tribes - 2, computers.Length);
        Assert.All(computers, c => Assert.False(c.HasSlot));
        Assert.Equal(LockstepMatch.TribeId(2), match.Players.ByTribe(2)!.Id);
        Assert.False(match.Simulation.World.Relations.AreAllies(0, 1));
        Assert.False(match.Simulation.World.Match.EndsWithLastSide);

        match.Step([System(1, CommandKind.PlayerForfeited)]);
        for (int i = 0; i < 20; i++)
        {
            match.Step([]);
        }
        Assert.Null(match.Outcome);
    }

    [Fact]
    public void CampaignMaps_KeepTheOriginalTwoTribes()
    {
        LockstepMatch match = LockstepMatch.Create(TestContent.Content,
            TestContent.Start(Versus with { Mission = 2 }, TestContent.Human(0, "Host"), TestContent.Human(1, "Guest")));
        Assert.False(match.Simulation.IsMatch);
        Assert.Equal(-1, match.Players.BySlot(1)!.Tribe);
        Assert.Equal(-1, MultiTribeControl.Instance.AddTribe(match.Simulation, 2, 2));
        Assert.False(MultiTribeControl.Instance.IsDefeated(match.Simulation, 0));
        Assert.Null(MultiTribeControl.Instance.Outcome(match.Simulation));
        MultiTribeControl.Instance.SetPlayerState(match.Simulation, 0, 0, PlayerState.Absent);
    }

    [Fact]
    public void ThreePeers_EachWithATribe_TwoThousandTicks_AndAMidGameJoinGetsANewTribe()
    {
        var room = new RoomSettings { Name = "Endless", Mode = RoomMode.Endless, AllowJoinInProgress = true };
        using var cluster = new SessionCluster(MultiTribeControl.Instance, room, seed: 43);
        cluster.AddClient("A");
        cluster.AddClient("B");
        cluster.StartMatch(new MatchPlan { Mission = -1, Seed = 43, Endless = new EndlessPlan(43, 32, 1, 0) });
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(1_000);

        SessionPeer late = cluster.AddClient("Late");
        cluster.RunUntil(() => late.Runner is { Role: LocalRole.Player }, "the late player to get a tribe");
        int lateTribe = late.Runner!.LocalTribe;
        Assert.True(lateTribe >= 3, $"late tribe {lateTribe}");
        Assert.Equal(TribeController.Human(3), late.Runner.Simulation.World.Tribes[lateTribe].Controller);
        cluster.RunTicks(1_000);
        cluster.Settle();

        cluster.AssertNoStops();
        cluster.AssertInSync(5);
        foreach (SessionPeer peer in cluster.Peers)
        {
            output.WriteLine($"{peer.Name}: tribe {peer.Runner!.LocalTribe}, {peer.Queued} orders, {peer.Answered} answered");
            Assert.True(peer.Runner.LocalTribe >= 0);
            Assert.True(peer.Answered > 10, $"{peer.Name} had {peer.Answered} orders answered");
        }
    }
}
