using System;
using System.Diagnostics;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>Many tribes on one map (custom map 1 has room for 19 camps): the tribe count is bounded only by that room.</summary>
public sealed class ManyTribesTests
{
    private const int Tribes = 16, WarmUp = 2000, Measured = 6000;

    private readonly ITestOutputHelper output;

    public ManyTribesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SixteenComputerTribes_PlayAMatch()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 16, MatchWorlds.Controllers(0, Tribes)));
        Assert.Equal(Tribes, game.World.Tribes.Count);
        Assert.Equal(Tribes, game.World.Tribes.Select(t => t.Camp).Distinct().Count());
        for (int i = 0; i < WarmUp; i++)
        {
            game.Step();
        }
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Measured && !game.IsOver; i++)
        {
            game.Step();
        }
        clock.Stop();
        double rate = Measured / clock.Elapsed.TotalSeconds;
        output.WriteLine($"{Tribes} computer tribes, {game.World.UnitSlots} unit slots: {rate:F0} ticks/s after {WarmUp} ticks");
        int alive = game.World.Tribes.Count(t => t[TribeCounter.Population] > 0);
        int fought = game.World.Tribes.Sum(t => t[TribeCounter.UnitsKilled]);
        output.WriteLine($"tribes with people {alive}, kills {fought}, decided {game.Match.IsDecided}");
        Assert.True(rate > 100, $"{rate:F0} ticks/s");
        Assert.True(alive > 1);
        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
        Assert.Equal(game.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void FourTribeMatch_SpeedIsReported()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 4, MatchWorlds.Controllers(2, 2)));
        for (int i = 0; i < WarmUp; i++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 4));
        }
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Measured && !game.IsOver; i++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 4));
        }
        clock.Stop();
        double rate = Measured / clock.Elapsed.TotalSeconds;
        output.WriteLine($"4 tribes (2 scripted humans, 2 computers): {rate:F0} ticks/s after {WarmUp} ticks");
        Assert.True(rate > 250, $"{rate:F0} ticks/s (a guard against gross slowdowns; real numbers come from uninstrumented runs)");
    }
}
