using System;
using System.Diagnostics;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.Tests.MultiTribe;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

/// <summary>Speed and memory of big worlds (printed; the asserts are loose floors for a busy machine).</summary>
[Trait("Speed", "Long")]
public sealed class EndlessPerformanceTests
{
    private readonly ITestOutputHelper output;

    public EndlessPerformanceTests(ITestOutputHelper output) => this.output = output;

    private double HashMilliseconds(GameSimulation game, int repeats = 100)
    {
        for (int i = 0; i < repeats; i++)
        {
            game.StateHash();
        }
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < repeats; i++)
        {
            game.StateHash();
        }
        return watch.Elapsed.TotalMilliseconds / repeats;
    }

    private double TicksPerSecond(GameSimulation game, int ticks, Func<GameSimulation, long, TickInput>? orders = null)
    {
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < ticks; i++)
        {
            game.Step(orders?.Invoke(game, game.Tick) ?? TickInput.Idle);
        }
        return ticks / watch.Elapsed.TotalSeconds;
    }

    [Fact]
    public void Hash_OfACampaignAndAVersusWorld()
    {
        GameSimulation campaign = GameSimulation.StartMission(MatchWorlds.Content, 7, 1);
        TicksPerSecond(campaign, 3000);
        GameSimulation versus = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 3, MatchWorlds.Controllers(2, 2)));
        TicksPerSecond(versus, 3000);

        double a = HashMilliseconds(campaign), b = HashMilliseconds(versus);

        output.WriteLine($"hash: campaign {a:0.000} ms, 4-tribe versus {b:0.000} ms");
        Assert.True(a < 50 && b < 50);
    }

    [Fact]
    public void BigEndlessWorld_FourPeopleAndThirtyTwoComputers()
    {
        GameSimulation game = GameSimulation.StartEndless(EndlessWorlds.Content, new EndlessSetup
        {
            Seed = 101,
            Tribes = Enumerable.Range(0, 4).Select(h => new MatchTribe(TribeController.Human(h))).ToArray(),
            ComputerTribes = 32,
            NewTribes = 1,
        });
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var watch = Stopwatch.StartNew();
        double early = TicksPerSecond(game, 2000, BuildingPlayer.Orders);
        // A large explored area: the people's fog covers all the land made so far plus a wide ring.
        game.GenerateLand(GridRect.FromEdges(-1024, -1024, 1024, 1024));
        foreach (int human in Enumerable.Range(0, 4))
        {
            FogOfWar fog = game.World.Fog(human);
            foreach (Maps.MapChunk chunk in game.World.Map.Chunks)
            {
                for (int y = chunk.Cells.Top; y < chunk.Cells.Bottom; y += 2)
                {
                    for (int x = chunk.Cells.Left; x < chunk.Cells.Right; x++)
                    {
                        fog.SetExplored(new GridPoint(x, y), true);
                    }
                }
            }
        }
        double late = TicksPerSecond(game, 3000, BuildingPlayer.Orders);
        long after = GC.GetTotalMemory(forceFullCollection: true);
        double hash = HashMilliseconds(game, 10);
        var saveWatch = Stopwatch.StartNew();
        byte[] save = game.Save();
        double saveMs = saveWatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"{game.World.Tribes.Count} tribes, {game.World.Map.Chunks.Count} chunks, {game.World.AnimalSlots} animal slots, "
            + $"{game.World.Map.ResourceNodes.Count} node slots; ticks/s early {early:0} late {late:0}; total {watch.Elapsed.TotalSeconds:0.0} s");
        output.WriteLine($"memory {before / 1048576.0:0.0} MB -> {after / 1048576.0:0.0} MB; hash {hash:0.00} ms; save {save.Length / 1024} KB in {saveMs:0.0} ms");
        Assert.True(late > 20, $"{late} ticks/s");
    }
}
