using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

public sealed class EndlessWaitingTribesTests
{
    private const int FewAtStart = 2;

    /// <summary>A generous bound for a start (the machine may be busy with other work).</summary>
    private const double MaxStartMilliseconds = 30_000;

    private static readonly Lazy<GameContent> Few = new(() => PlacingAtStart(FewAtStart));

    private readonly ITestOutputHelper output;

    public EndlessWaitingTribesTests(ITestOutputHelper output) => this.output = output;

    /// <summary>The game's content with endless rules that place <paramref name="count"/> computer tribes at the start.</summary>
    private static GameContent PlacingAtStart(int count)
    {
        GameContent c = EndlessWorlds.Content;
        EndlessRules rules = c.Endless!;
        return new GameContent(c.Missions.Values, c.Maps.Values, c.Terrain, c.ResourceNodeKinds.Values, c.Rules)
        {
            Endless = rules with { Camps = rules.Camps with { PlacedAtStart = count } },
        };
    }

    [Fact]
    public void Start_PlacesTheFirstComputerTribesAndTheRestWait()
    {
        GameSimulation game = GameSimulation.StartEndless(Few.Value, EndlessWorlds.Setup(humans: 1, computers: 5, seed: 12));

        Assert.Equal(1 + FewAtStart, game.World.Tribes.Count);
        Assert.Equal(5 - FewAtStart, game.World.Endless!.WaitingTribes);
        GameSimulation loaded = GameSimulation.Load(Few.Value, game.Save());
        Assert.Equal(game.World.Endless.WaitingTribes, loaded.World.Endless!.WaitingTribes);
        Assert.Equal(game.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void Start_PlacesEveryComputerTribeWhenTheyFit()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 3, seed: 12);

        Assert.Equal(4, game.World.Tribes.Count);
        Assert.Equal(0, game.World.Endless!.WaitingTribes);
    }

    [Fact]
    public void WaitingTribes_ComeIntoLandThePeopleUncover_EvenWithoutNewTribes()
    {
        EndlessSetup setup = EndlessWorlds.Setup(humans: 1, computers: FewAtStart + 2, seed: 8, newTribes: 0, difficulty: 1);
        GameSimulation game = GameSimulation.StartEndless(Few.Value, setup);
        GameWorld world = game.World;
        game.Step();
        int explorer = world.UnitsOf(0).Select(u => u.Id).First(id => world.Unit(id).InUse);
        var appeared = new List<TribeAppeared>();
        for (int leg = 1; leg <= 16 && world.Endless!.WaitingTribes > 0; leg++)
        {
            var goal = new GridPoint(world.Tribes[0].Camp.X + (leg * 40), world.Tribes[0].Camp.Y);
            appeared.AddRange(game.Step(TickInput.WithOrders(new MoveUnits(0, new UnitSet(new[] { explorer }), goal, 0, true)))
                .Events.OfType<TribeAppeared>());
            for (int tick = 0; tick < 400; tick++)
            {
                appeared.AddRange(game.Step().Events.OfType<TribeAppeared>());
            }
        }

        Assert.Equal(0, world.Endless!.WaitingTribes);
        Assert.Equal(2, appeared.Count);
        Assert.Equal(1 + FewAtStart + 2, world.Tribes.Count);
        foreach (TribeAppeared tribe in appeared)
        {
            Assert.True(tribe.Computer);
            Assert.Equal(setup.Difficulty, world.Tribes[tribe.Tribe].Controller.Difficulty ?? world.Difficulty);
            Assert.True(world.Tribes[tribe.Tribe].ArrivedAtFrame > 0);
        }
    }

    [Fact]
    [Trait("Speed", "Long")]
    public void Start_WithFiveThousandComputerTribes_IsQuickAndPeersStayAlike()
    {
        const int Asked = 5000;
        int atOnce = EndlessWorlds.Content.Endless!.Camps.PlacedAtStart;
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var clock = Stopwatch.StartNew();
        GameSimulation a = EndlessWorlds.Start(humans: 1, computers: Asked, seed: 44);
        double startMs = clock.Elapsed.TotalMilliseconds;
        long memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
        clock.Restart();
        GameSimulation b = EndlessWorlds.Start(humans: 1, computers: Asked, seed: 44);
        double againMs = clock.Elapsed.TotalMilliseconds;

        Assert.Equal(1 + atOnce, a.World.Tribes.Count);
        Assert.Equal(Asked - atOnce, a.World.Endless!.WaitingTribes);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(againMs < MaxStartMilliseconds, $"the start took {againMs:F0} ms");
        clock.Restart();
        for (int tick = 0; tick < 200; tick++)
        {
            a.Step();
            b.Step();
        }
        double tickMs = clock.Elapsed.TotalMilliseconds / 400;
        Assert.Equal(a.StateHash(), b.StateHash());
        byte[] saved = a.Save();
        GameSimulation loaded = GameSimulation.Load(EndlessWorlds.Content, saved);
        Assert.Equal(a.StateHash(), loaded.StateHash());
        Assert.Equal(a.World.Endless.WaitingTribes, loaded.World.Endless!.WaitingTribes);
        output.WriteLine($"{Asked} asked: start {startMs:F0} ms (again {againMs:F0} ms), {a.World.Tribes.Count} tribes placed, {a.World.Map.Chunks.Count} chunks, "
            + $"memory +{(memoryAfter - memoryBefore) / (1024.0 * 1024.0):F1} MB, tick {tickMs:F2} ms, save {saved.Length / 1024} KB");
    }
}
