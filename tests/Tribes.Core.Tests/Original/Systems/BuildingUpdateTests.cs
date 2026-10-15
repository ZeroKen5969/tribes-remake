using System.IO;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Buildings;
using Tribes.Core.World;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="BuildingUpdate"/> (the buildings' part of a frame) with the original.</summary>
public sealed class BuildingUpdateTests
{
    /// <summary>Frames between two compared frames in a running game, and its last frame.</summary>
    private const int Stride = 47, LastFrame = 25000;

    private readonly ITestOutputHelper output;

    public BuildingUpdateTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void RunFrame_MatchesTheOriginal_OnSampledStates()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All.Concat(RichSamples.All).Concat(MissionSamples.All.Where((_, i) => i % 2 == 0)))
        {
            run.Check(sample, "RunFrame", g => g.world_a.g__V(), s => s.BuildingUpdate.RunFrame());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 120);
    }

    [Fact]
    public void RunFrame_MatchesTheOriginal_WithBuildingsAtWork()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in BuildingSamples.All)
        {
            run.Check(sample, "RunFrame", g => g.world_a.g__V(), s => s.BuildingUpdate.RunFrame());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(6L)]
    public void RunFrame_MatchesTheOriginal_DuringAGame(long seed)
    {
        string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
        ComparisonRun run = StepComparison.RunOver(resources, seed, LastFrame, frame => frame % Stride == 0,
            (_, game) => GameRuns.InPlay(game)
                ? StepComparison.Compare(game, LegacySamples.Content, g => g.world_a.g__V(), w => new RuleSystems(w).BuildingUpdate.RunFrame())
                : System.Array.Empty<string>());
        output.WriteLine(run.ToString());
        Assert.True(run.Failures.Count == 0, run.ToString());
        Assert.True(run.Checked > 300, run.ToString());
    }

    [Fact]
    public void TakeTurn_LeavesASiteMissingFromTheConstructionListAlone()
    {
        GameWorld world = TestWorlds.Empty();
        Building site = world.Building(4);
        (site.HitPoints, site.Kind, site.Aux, site.Level, site.OccupantCount) = (30, 18, 2, 3, 0);
        new RuleSystems(world).BuildingUpdate.TakeTurn(site);
        Assert.Equal((18, 2), (site.Kind, site.Aux));
    }

    [Fact]
    public void TakeTurn_FinishesACompleteSiteInItsSlot()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Building site = world.Building(4);
        (site.HitPoints, site.Kind, site.Aux, site.Level, site.X, site.Y) = (25, 18, 2, 3, 6, 6);
        world.Construction[0].Building = 4;
        systems.BuildingUpdate.TakeTurn(site);
        Assert.Equal(2, site.Kind);
        Assert.Equal(1, site.Level);
        Assert.Equal(0, world.Construction[0].Building);
        Assert.Contains(world.Dust, d => d.Building == 4);
    }

    [Fact]
    public void TakeTurn_EndsAFinishedRepair()
    {
        GameWorld world = TestWorlds.Empty();
        Building hut = world.Building(5);
        (hut.HitPoints, hut.MaxHitPoints, hut.Kind, hut.Level, hut.WorkMode, hut.Aux) = (25, 25, 2, 1, BuildingWorkMode.Repairing, 0);
        world.Upgrades[0].Building = 5;
        new RuleSystems(world).BuildingUpdate.TakeTurn(hut);
        Assert.Equal(BuildingWorkMode.Normal, hut.WorkMode);
        Assert.Equal(0, world.Upgrades[0].Building);
    }

    [Fact]
    public void TakeTurn_LeavesABusyBuildingWithoutAJobAlone()
    {
        GameWorld world = TestWorlds.Empty();
        Building hut = world.Building(5);
        (hut.HitPoints, hut.MaxHitPoints, hut.Kind, hut.Level, hut.WorkMode) = (25, 25, 2, 1, BuildingWorkMode.Repairing);
        new RuleSystems(world).BuildingUpdate.TakeTurn(hut);
        Assert.Equal(BuildingWorkMode.Repairing, hut.WorkMode);
    }
}
