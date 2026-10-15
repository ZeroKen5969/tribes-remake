using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Buildings;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StoreKeeping"/> (goods taken from the stock) with the original, plus edge cases.</summary>
public sealed class StoreKeepingTests
{
    private const int FirstFetch = 28, LastFetch = 49, UpgradeMaterial = 99;

    private readonly ITestOutputHelper output;

    public StoreKeepingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TakeFromStock_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int[] actions = Enumerable.Range(FirstFetch, LastFetch - FirstFetch + 1).Append(UpgradeMaterial).ToArray();
        foreach (LegacySample sample in BuildingSamples.All.Where((_, i) => i % 6 == 0).Concat(MissionSamples.All.Where((_, i) => i % 10 == 0)))
        {
            var game = sample.Restore();
            int worker = LegacySamples.LiveUnits(game).FirstOrDefault();
            if (worker == 0)
            {
                continue;
            }
            foreach (int building in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 3))
            {
                int tribe = (building - 1) / 30;
                foreach (int action in SampleInputs.Pick(actions, building + sample.Frame, 4).Append(UpgradeMaterial))
                {
                    run.Check(sample, $"TakeFromStock {action} building {building}",
                        g => { g.world_a.i_v = building; g.world_a.i_u = tribe; return g.world_a.a_IZI(action, false, worker); },
                        s => s.StoreKeeping.TakeFromStock(action, s.World.Unit(worker), s.World.Building(building), tribe));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void TakeFromStock_ShortStockTakesNothingAndShowsWhatIsMissing()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit worker = world.Unit(1);
        Building store = world.Building(2);
        world.Tribes[0][TribeCounter.Wood] = 1;
        bool taken = systems.StoreKeeping.TakeFromStock(FirstFetch, worker, store, 0);
        Assert.False(taken);
        Assert.Equal(1, world.Tribes[0][TribeCounter.Wood]);
        Assert.NotEqual(0, worker.CarriedItem);
    }

    [Fact]
    public void TakeFromStock_AnActionWithoutARecipeTakesNothing()
    {
        GameWorld world = TestWorlds.Empty();
        world.Tribes[0][TribeCounter.Wood] = 50;
        Assert.False(new RuleSystems(world).StoreKeeping.TakeFromStock(19, world.Unit(1), world.Building(2), 0));
        Assert.Equal(50, world.Tribes[0][TribeCounter.Wood]);
    }
}
