using System.IO;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Wildlife;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WildlifeUpdate"/> (the animals' part of a frame) with the original.</summary>
public sealed class WildlifeUpdateTests
{
    /// <summary>Frames between two compared frames, and the birth period of the first mission (40 * 40 frames).</summary>
    private const int Stride = 53, BirthPeriod = 1600, BirthPhase = 10, LastFrame = 25000;

    private readonly ITestOutputHelper output;

    public WildlifeUpdateTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void RunFrame_MatchesTheOriginal_OnSampledStates()
    {
        var run = new ParityRun();
        int withAnimals = 0;
        foreach (LegacySample sample in LegacySamples.All.Concat(RichSamples.All))
        {
            if (LiveAnimals(sample.Restore()).Any())
            {
                withAnimals++;
            }
            run.Check(sample, "RunFrame", g => g.world_a.h__V(), s => s.WildlifeUpdate.RunFrame());
        }
        output.WriteLine(run + $"; {withAnimals} with animals");
        run.AssertAgrees(minimum: 60);
    }

    [Fact]
    public void RunFrame_MatchesTheOriginal_InEveryMission()
    {
        var run = new ParityRun();
        int withAnimals = 0;
        foreach (LegacySample sample in MissionSamples.All)
        {
            if (LiveAnimals(sample.Restore()).Any())
            {
                withAnimals++;
            }
            run.Check(sample, "RunFrame", g => g.world_a.h__V(), s => s.WildlifeUpdate.RunFrame());
        }
        output.WriteLine(run + $"; {withAnimals} with animals");
        run.AssertAgrees(minimum: 60);
    }

    [Fact]
    public void RunFrame_MatchesTheOriginal_WithProvokedAnimals()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All)
        {
            run.Check(sample, "RunFrame", g => g.world_a.h__V(), s => s.WildlifeUpdate.RunFrame());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(6L)]
    public void RunFrame_MatchesTheOriginal_DuringAGame(long seed)
    {
        string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
        ComparisonRun run = StepComparison.RunOver(resources, seed, LastFrame,
            frame => frame % Stride == 0 || frame % BirthPeriod == BirthPhase,
            (_, game) => GameRuns.InPlay(game)
                ? StepComparison.Compare(game, LegacySamples.Content, g => g.world_a.h__V(), w => new RuleSystems(w).WildlifeUpdate.RunFrame())
                : System.Array.Empty<string>());
        output.WriteLine(run.ToString());
        Assert.True(run.Failures.Count == 0, run.ToString());
        Assert.True(run.Checked > 300, run.ToString());
    }
}
