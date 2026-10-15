using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ComputerPlayer"/> with the original's computer step, plus edge cases.</summary>
public sealed class ComputerPlayerTests
{
    private readonly ITestOutputHelper output;

    public ComputerPlayerTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Step_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1))
        {
            foreach (int frame in AiSamples.Frames(sample))
            {
                run.Check(sample, $"Step {frame}", g =>
                {
                    g.i_c = frame;
                    g.world_a.i_u = AiSamples.Computer;
                    g.world_a.aq_();
                }, s =>
                {
                    s.World.Frame = frame;
                    s.ComputerPlayer.Step(AiSamples.Computer);
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Step_WaitsWhileAStoryScenePlays()
    {
        RuleSystems systems = ScenarioSamples.Started(4);
        GameWorld world = systems.World;
        world.Script.ObjectiveStage = 50;
        int next = world.Ai.NextWaveFrame;
        world.Frame = 100;
        systems.ComputerPlayer.Step(1);
        Assert.Equal(next, world.Ai.NextWaveFrame);
        world.Script.ObjectiveStage = 0;
        systems.ComputerPlayer.Step(1);
        Assert.True(world.Ai.NextWaveFrame > world.Frame);
    }
}
