using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StoryProgress"/> (casting a scene's actors) with the original, plus edge cases.</summary>
public sealed class StoryProgressTests
{
    private readonly ITestOutputHelper output;

    public StoryProgressTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CastActors_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates.Concat(MissionSamples.All.Where((_, i) => i % 3 == 0)))
        {
            var game = sample.Restore();
            int chief = game.sArr2_a[0][17];
            int other = LegacySamples.LiveUnits(game).FirstOrDefault(u => u <= 50 && u != chief);
            foreach (int lead in new[] { 0, chief, other }.Distinct())
            {
                foreach (int first in new[] { 1, 3 })
                {
                    int subject = (lead + first) % 7;
                    run.Check(sample, $"CastActors lead {lead} from {first}",
                        g => g.world_a.b_III_V(lead, subject, first),
                        s => s.StoryProgress.CastActors(lead, subject, first));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void NextStep_IsNullWhileASceneOfTheStoryPlays()
    {
        GameWorld world = TestWorlds.Empty();
        var story = new RuleSystems(world).StoryProgress;
        Assert.NotNull(story.NextStep);
        world.Script.ObjectiveStage += StoryProgress.ScenePlaying;
        Assert.Null(story.NextStep);
    }
}
