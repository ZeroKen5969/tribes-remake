using System.IO;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Resources;
using Tribes.Core.Systems.Story;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ResourceUpdate"/> (resource nodes and the story's part of a frame) with the original.</summary>
public sealed class ResourceUpdateTests
{
    /// <summary>Frames between two compared frames in a running game, and its last frame.</summary>
    private const int Stride = 41, LastFrame = 25000;

    private readonly ITestOutputHelper output;

    public ResourceUpdateTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void RunFrame_MatchesTheOriginal_OnSampledStates()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All.Concat(MissionSamples.All).Concat(WildlifeSamples.All.Where((_, i) => i % 4 == 0)))
        {
            bool rosterOpen = sample.Restore().z_F;
            run.Check(sample, "RunFrame", g => g.world_a.i__V(), s => s.ResourceUpdate.RunFrame(new TutorialInputs(rosterOpen)));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(6L)]
    public void RunFrame_MatchesTheOriginal_DuringAGame(long seed)
    {
        string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
        ComparisonRun run = StepComparison.RunOver(resources, seed, LastFrame,
            frame => frame % Stride == 0 || (frame & NodeUpkeep.GrowthPeriodMask) <= 2,
            (_, game) => GameRuns.InPlay(game)
                ? StepComparison.Compare(game, LegacySamples.Content, g => g.world_a.i__V(),
                    w => new RuleSystems(w).ResourceUpdate.RunFrame(new TutorialInputs(game.z_F)))
                : System.Array.Empty<string>());
        output.WriteLine(run.ToString());
        Assert.True(run.Failures.Count == 0, run.ToString());
        Assert.True(run.Checked > 300, run.ToString());
    }
}
