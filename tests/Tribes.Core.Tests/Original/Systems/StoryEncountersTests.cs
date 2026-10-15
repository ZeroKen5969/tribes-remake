using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Systems.Units.Work;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StoryEncounters"/> with the original, plus edge cases.</summary>
public sealed class StoryEncountersTests
{
    private readonly ITestOutputHelper output;

    public StoryEncountersTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Meet_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            int[] live = Live(sample.Restore()).ToArray();
            foreach (int id in Some(sample, 4).Where(u => TribeOf(u) == 0))
            {
                int enemy = live.FirstOrDefault(e => TribeOf(e) == 1);
                var steps = LegacySamples.Content.Rules.Missions.Missions[sample.Game.b_q].StorySteps;
                foreach (int stage in Enumerable.Range(0, steps.Count).Where(i => steps[i].Trigger == StoryEncounters.AreaTrigger))
                {
                    int s0 = stage;
                    run.Check(sample, $"Meet unit {id} stage {stage}", g => g.world_a.b_f = (sbyte)s0,
                        g => { BeginTurn(g, id); g.world_a.b_IIII_V(id, enemy, Get(g, X, id), Get(g, Y, id)); },
                        s => { s.StoryEncounters.Meet(s.World.Unit(id), enemy, new GridPoint(s.World.Unit(id).X, s.World.Unit(id).Y)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void Meet_DoesNothingWithoutAnAreaStep()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        systems.World.Mission = 5;
        systems.StoryEncounters.Meet(systems.World.Unit(1), 0, new GridPoint(1, 1));
        Assert.Equal(0, systems.World.Script.ObjectiveStage);
    }
}
