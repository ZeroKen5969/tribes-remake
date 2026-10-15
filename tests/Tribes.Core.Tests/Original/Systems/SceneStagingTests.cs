using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="SceneStaging"/> with the original's scene moves, plus edge cases.</summary>
public sealed class SceneStagingTests
{
    private static readonly GridPoint[] Cells = { new(20, 20), new(5, 5), new(40, 30) };

    private readonly ITestOutputHelper output;

    public SceneStagingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SendActors_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int i = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            int[] actors = LegacySamples.LiveUnits(sample.Restore()).Take(10).ToArray();
            GridPoint cell = Cells[i % Cells.Length];
            int lead = i++ % 6;
            run.Check(sample, $"SendActors {lead}", g =>
            {
                ScenarioSamples.CastOriginal(g, actors);
                g.d_III(cell.X, cell.Y, lead);
            }, s =>
            {
                ScenarioSamples.Cast(s.World, actors);
                s.SceneStaging.SendActors(cell, lead);
            });
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void GatherPlayerTribe_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int i = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            GridPoint cell = Cells[i++ % Cells.Length];
            run.Check(sample, "Gather", g => g.b_II(cell.X, cell.Y), s => s.SceneStaging.GatherPlayerTribe(cell));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void ClearWorld_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 3))
        {
            run.Check(sample, "ClearWorld", g =>
            {
                g.z_s = true;
                g.world_a.l__V();
            }, s =>
            {
                s.World.Script.Active = true;
                s.SceneStaging.ClearWorld();
            });
            run.Check(sample, "ClearEnemy", g => g.world_a.m__V(), s => s.SceneStaging.ClearEnemyAndAnimals());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void StopAndIdle_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            foreach (int unit in SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), 4))
            {
                run.Check(sample, "StopAndIdle", g => g.world_a.a_IZ(unit, true), s => s.SceneStaging.StopAndIdle(s.World.Unit(unit)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void SendActors_SkipsSlotsThatHoldNoUnit()
    {
        RuleSystems systems = ScenarioSamples.Started(1);
        GameWorld world = systems.World;
        world.Script.SetActor(SceneStaging.FirstExtra, CellCode.FirstAnimal + 2);
        world.Script.SetActor(0, 1);
        systems.SceneStaging.SendActors(new GridPoint(10, 10), 0);
        Assert.Equal(10, world.Unit(1).DestinationX);
        Assert.Null(SceneActors.Unit(world, SceneStaging.FirstExtra));
    }
}
