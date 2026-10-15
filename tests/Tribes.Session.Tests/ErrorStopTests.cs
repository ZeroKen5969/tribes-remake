using System;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>A failure inside the rules stops the match with a message instead of crashing the game.</summary>
public sealed class ErrorStopTests
{
    [Fact]
    public void RulesFailure_StopsEveryDevice_WithAnError()
    {
        using var cluster = new SessionCluster(new FailingControl(200), seed: 51);
        SessionPeer guest = cluster.AddClient("Guest");
        cluster.StartMatch(new MatchPlan { Mission = 8, Seed = 51 });
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunUntil(() => cluster.HostPeer.Stops.Count == 1 && guest.Stops.Count == 1, "both to stop");

        Assert.Equal(StopReason.Error, cluster.HostPeer.Stops[0].Reason);
        // The host runs ahead: it fails first and closes the match, or the guest fails on its own.
        Assert.True(guest.Stops[0].Reason is StopReason.Error or StopReason.HostLeft, guest.Stops[0].ToString());
        Assert.Contains("rules bug", cluster.HostPeer.Stops[0].Message, StringComparison.Ordinal);
        Assert.Contains(cluster.HostPeer.Log, l => l.Contains("SESSION failed", StringComparison.Ordinal));
        for (int i = 0; i < 50; i++)
        {
            cluster.Step();
        }
    }

    [Fact]
    public void LocalRulesFailure_StopsTheGame()
    {
        using LocalMatchRunner runner = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8 }, TestContent.Runner());
        var stops = new System.Collections.Generic.List<MatchStop>();
        runner.Stopped += (_, e) => stops.Add(e.Stop);
        runner.Orders.Submit(new Tribes.Core.Commands.SetHints(0, true));
        // A world the rules cannot run: a building slot that points nowhere makes the frame fail.
        runner.Simulation.World.Building(1).HitPoints = 1;
        runner.Simulation.World.Building(1).X = -50_000;
        for (int i = 0; i < 20 && stops.Count == 0; i++)
        {
            runner.StepOnce();
        }
        if (stops.Count == 0)
        {
            Assert.Fail("no failure");
        }
        Assert.Equal(StopReason.Error, stops[0].Reason);
        Assert.Equal(MatchPhase.Stopped, runner.Phase);
    }
}
