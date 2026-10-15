using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AttackWaves"/> with the original's attack waves, plus edge cases.</summary>
public sealed class AttackWavesTests
{
    /// <summary>The mission without attack waves (its wave tables are empty).</summary>
    private const int NoWavesMission = 3;

    private readonly ITestOutputHelper output;

    public AttackWavesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void LaunchAndAdvance_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1).Where(s => ScenarioSamples.MissionOf(s) != NoWavesMission))
        {
            int frame = sample.Restore().i_c;
            foreach (int advances in new[] { 0, 1, 3 })
            {
                run.Check(sample, $"Wave {advances}", g =>
                {
                    AiSamples.Registers(g, sample);
                    g.world_a.i_aR = frame;
                    g.world_a.av_();
                    for (int i = 0; i < advances && g.world_a.b_O >= 0; i++)
                    {
                        g.i_c += 16 * 40;
                        g.world_a.aw_();
                    }
                }, s =>
                {
                    s.World.Ai.WaveStartFrame = frame;
                    AttackWaves waves = s.AttackWaves;
                    waves.Launch(AiSamples.Computer, AiSamples.Ai(sample).Posts.Count, AiSamples.RampUp(sample));
                    for (int i = 0; i < advances && s.World.Ai.WaveTarget >= 0; i++)
                    {
                        s.World.Frame += 16 * 40;
                        waves.Advance(AiSamples.Computer);
                    }
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void Launch_SendsAWaveOnARoute()
    {
        RuleSystems systems = ScenarioSamples.Started(4, difficulty: 1);
        GameWorld world = systems.World;
        world.Frame = 40000;
        systems.AttackWaves.Launch(1, LegacySamples.Content.Rules.Missions.Missions[4].Ai.Posts.Count, 30000);
        Assert.Equal(0, world.Ai.WaveTarget);
        Assert.InRange(world.Ai.WaveStage, 0, LegacySamples.Content.Rules.Missions.Missions[4].Ai.AttackRoutes.Count - 1);
        world.Frame = 60000;
        for (int i = 0; i < 20 && world.Ai.WaveTarget >= 0; i++)
        {
            systems.AttackWaves.Advance(1);
        }
        Assert.Equal(AttackWaves.NoWave, world.Ai.WaveTarget);
    }
}
