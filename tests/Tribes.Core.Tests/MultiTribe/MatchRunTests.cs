using System;
using System.Collections.Generic;
using System.Diagnostics;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// Long matches of three and four tribes on every custom map: humans give scripted orders,
/// computers play by their plans, the world going on after one side is left. Nothing may throw; a
/// second run from the same setup reaches the same states, and a game saved part way and loaded
/// goes on exactly as the original does.
/// </summary>
public sealed class MatchRunTests
{
    private const int LongRun = 30000, CheckEvery = 2500, SaveAt = 12000;

    private readonly ITestOutputHelper output;

    public MatchRunTests(ITestOutputHelper output) => this.output = output;

    public static TheoryData<int, int, int> Matches()
    {
        var data = new TheoryData<int, int, int>();
        for (int map = 0; map < MatchStart.MapCount(MatchWorlds.Content); map++)
        {
            data.Add(map, 1, 2);
            data.Add(map, 2, 2);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void LongMatch_RunsWithoutError_IsDeterministic_AndSurvivesSaveAndLoad(int map, int humans, int computers)
    {
        MatchSetup setup = MatchWorlds.Setup(map, 11 + map, MatchWorlds.Controllers(humans, computers)) with { EndsWithLastSide = false };
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        var hashes = new Dictionary<long, ulong>();
        GameSimulation? loaded = null;
        var clock = Stopwatch.StartNew();
        while (game.Tick < LongRun && !game.IsOver)
        {
            if (game.Tick == SaveAt)
            {
                loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
                Assert.Equal(game.StateHash(), loaded.StateHash());
            }
            TickInput input = MatchWorlds.Orders(game, game.Tick, map);
            game.Step(input);
            loaded?.Step(input);
            if (game.Tick % CheckEvery == 0)
            {
                hashes[game.Tick] = game.StateHash();
                Assert.Equal(hashes[game.Tick], loaded?.StateHash() ?? hashes[game.Tick]);
            }
        }
        clock.Stop();
        Report(game, map, humans, computers, clock.Elapsed);
        Assert.Equal(LongRun, game.Tick);

        GameSimulation again = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        while (again.Tick < Math.Min(SaveAt, game.Tick))
        {
            again.Step(MatchWorlds.Orders(again, again.Tick, map));
            if (hashes.TryGetValue(again.Tick, out ulong expected))
            {
                Assert.Equal(expected, again.StateHash());
            }
        }
    }

    private void Report(GameSimulation game, int map, int humans, int computers, TimeSpan elapsed)
    {
        output.WriteLine($"map {map}, {humans} humans + {computers} computers: {game.Tick} ticks, "
            + $"{game.Tick / Math.Max(elapsed.TotalSeconds, 1e-6):F0} ticks/s (with the loaded copy), decided {game.Match.IsDecided}");
        foreach (TribeResult result in game.Match.Tribes)
        {
            TribeState tribe = game.World.Tribes[result.Tribe];
            output.WriteLine($"  tribe {result.Tribe} {result.Controller.Kind} {result.Outcome} at {result.Frame}: "
                + $"population {tribe[TribeCounter.Population]}, buildings {tribe[TribeCounter.Buildings]}, "
                + $"kills {tribe[TribeCounter.UnitsKilled]}, razed {tribe[TribeCounter.BuildingsDestroyed]}");
        }
    }
}
