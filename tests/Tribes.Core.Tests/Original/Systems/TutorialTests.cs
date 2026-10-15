using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Tutorial"/> with the original's tutorial objectives, plus its inputs.</summary>
public sealed class TutorialTests
{
    private readonly ITestOutputHelper output;

    public TutorialTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Advance_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int steps = LegacySamples.Content.Rules.Story.TutorialSteps.Count;
        int i = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1).Where(s => ScenarioSamples.MissionOf(s) < 2))
        {
            foreach (bool roster in new[] { false, true })
            {
                int step = i++ % steps, timer = (i * 37) % 121, stage = i % 3, frame = i * 8;
                run.Check(sample, $"Tutorial {step}:{timer}:{stage}:{roster}", g =>
                {
                    (g.b_p, g.z_F, g.i_c) = (1, roster, frame);
                    (g.world_a.b_g, g.world_a.b_J, g.world_a.b_f) = ((sbyte)step, (sbyte)timer, (sbyte)stage);
                    g.world_a.al_();
                }, s =>
                {
                    ScriptState script = s.World.Script;
                    (script.TutorialFlags, s.World.Frame) = (1, frame);
                    (script.TutorialObjective, script.TutorialTimer, script.ObjectiveStage) = (step, timer, stage);
                    s.Tutorial.Advance(new TutorialInputs(roster));
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void Advance_WaitsWhileAStoryScenePlays()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1).Where(s => ScenarioSamples.MissionOf(s) < 2).Take(10))
        {
            for (int stage = 50; stage <= 53; stage++)
            {
                int s0 = stage;
                run.Check(sample, $"TutorialInScene {stage}", g =>
                {
                    (g.b_p, g.i_c) = (1, 64);
                    (g.world_a.b_g, g.world_a.b_f) = (2, (sbyte)s0);
                    g.world_a.al_();
                }, s =>
                {
                    (s.World.Script.TutorialFlags, s.World.Frame) = (1, 64);
                    (s.World.Script.TutorialObjective, s.World.Script.ObjectiveStage) = (2, s0);
                    s.Tutorial.Advance(default);
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void PlayerOpenedMenu_TriggersAConfirmationStep()
    {
        RuleSystems systems = ScenarioSamples.Started(0);
        ScriptState script = systems.World.Script;
        var steps = LegacySamples.Content.Rules.Story.TutorialSteps;
        int confirm = Enumerable.Range(0, steps.Count).First(i => steps[i].Trigger == 3);
        script.TutorialObjective = confirm;
        systems.OpenedGameMenu();
        Assert.Equal(120 - steps[confirm].Delay, script.TutorialTimer);
        script.TutorialObjective = 0;
        script.TutorialTimer = 0;
        systems.OpenedGameMenu();
        Assert.Equal(0, script.TutorialTimer);
        script.TutorialObjective = steps.Count;
        systems.OpenedGameMenu();
        Assert.Equal(0, script.TutorialTimer);
    }
}
