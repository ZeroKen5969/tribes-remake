using Tribes.Core.Simulation;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnlockLevels"/> with the original, plus edge cases.</summary>
public sealed class UnlockLevelsTests
{
    private static readonly int[] Frames = { -1, 3999, 4000, 6000, 9600, 11000, 30000 };

    private readonly ITestOutputHelper output;

    public UnlockLevelsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Recompute_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (int frame in Frames)
            {
                run.Check(sample, $"Recompute at {frame}",
                    g =>
                    {
                        if (frame >= 0)
                        {
                            g.i_c = frame;
                        }
                        g.world_a.j__V();
                    },
                    s =>
                    {
                        if (frame >= 0)
                        {
                            s.World.Frame = frame;
                        }
                        s.UnlockLevels.Recompute();
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Recompute_AddsTheUnlocksDueByNow()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Mission = 4;

        world.Frame = 4999;
        systems.UnlockLevels.Recompute();
        Assert.Equal(1, world.UnlockLevel(8));

        world.Frame = 6500;
        systems.UnlockLevels.Recompute();
        Assert.Equal(2, world.UnlockLevel(8));
        Assert.Equal(2, world.UnlockLevel(7));
        Assert.Equal(1, world.UnlockLevel(9));
    }
}
