using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StockArming"/> with the original's arming from the stock, plus edge cases.</summary>
public sealed class StockArmingTests
{
    private readonly ITestOutputHelper output;

    public StockArmingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Arm_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            foreach (int unit in SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), 4))
            {
                run.Check(sample, "Arm", g =>
                {
                    (g.world_a.i_v, g.world_a.i_u) = (unit, (unit - 1) / 50);
                    g.world_a.L_();
                }, s => s.StockArming.Arm(s.World.Unit(unit)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void Arm_FailsWithoutStock()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        var tribe = systems.World.Tribes[0];
        for (int counter = 0; counter <= 10; counter++)
        {
            tribe.SetCounter(counter, 0);
        }
        Assert.False(systems.StockArming.Arm(systems.World.Unit(1)));
    }
}
