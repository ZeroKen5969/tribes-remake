using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="FrameStart"/> with the original's start of frame, plus edge cases.</summary>
public sealed class FrameStartTests
{
    private readonly ITestOutputHelper output;

    public FrameStartTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Run_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            int mission = ScenarioSamples.MissionOf(sample);
            var ai = LegacySamples.Content.Rules.Missions.Missions[mission].Ai;
            int unit = 1000 / (1 + (3 * sample.Restore().b_b));
            foreach ((int frame, bool rally, bool allOut, int stage) in new[]
            {
                (sample.Frame | 3, false, false, 0), (128, false, false, 2), (130, true, false, 0), (194, true, true, 1),
                (ai.StoneAxeUnlockTime * unit, false, false, 0), (ai.MaceAndTomahawkUnlockTime * unit, false, false, 0),
            })
            {
                run.Check(sample, $"FrameStart {frame}:{rally}:{allOut}:{stage}", g =>
                {
                    (g.i_c, g.z_L, g.z_N, g.world_a.b_f) = (frame, rally, allOut, (sbyte)stage);
                    g.world_a.s_();
                }, s =>
                {
                    (s.World.Frame, s.World.Orders.Rally, s.World.Orders.AllOut, s.World.Script.ObjectiveStage) = (frame, rally, allOut, stage);
                    s.FrameStart.Run();
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void MusterArmy_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 3))
        {
            foreach ((bool rally, bool allOut) in new[] { (true, false), (false, true), (false, false) })
            {
                run.Check(sample, $"Muster {rally}:{allOut}", g =>
                {
                    (g.z_L, g.z_N) = (rally, allOut);
                    g.world_a.b_();
                }, s =>
                {
                    (s.World.Orders.Rally, s.World.Orders.AllOut) = (rally, allOut);
                    s.FrameStart.MusterArmy();
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void Run_ResetsTheCountedObjective()
    {
        RuleSystems systems = ScenarioSamples.Started(0);
        systems.World.Script.ObjectiveStage = 2;
        systems.World.Script.ObjectiveTimer = 9;
        systems.World.Frame = 1;
        systems.FrameStart.Run();
        Assert.Equal(0, systems.World.Script.ObjectiveTimer);
    }
}
