using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="PlaceSwap"/> with the original, plus edge cases.</summary>
public sealed class PlaceSwapTests
{
    private const int MoveDelayField = 10, RunField = 77;

    private readonly ITestOutputHelper output;

    public PlaceSwapTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Swap_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var walkers = UnitSampling.Units(game, 8).Where(id => UnitSampling.OnItsCell(game, id) && Walks(game, id)).ToList();
            foreach (int id in walkers)
            {
                foreach (int mate in walkers.Where(m => m != id && (m - 1) / 50 == (id - 1) / 50).Take(3))
                {
                    run.Check(sample, $"Swap unit {id} with {mate}",
                        g => { UnitSampling.Turn(g, id, 0).a_B((sbyte)mate); return g.world_a.i_E; },
                        s => (int)s.PlaceSwap.Swap(s.World.Unit(id), s.World.Unit(mate)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Swap_TheMateKeepsItsTaskSiteWhereItGoes()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit unit = UnitNeedsTests.Adult(world, 22);
        Unit mate = UnitNeedsTests.Adult(world, 23);
        (unit.MoveDelay, mate.MoveDelay, unit.Facing) = (6, 6, 0);
        (mate.TaskSiteX, mate.TaskSiteY, mate.PathLength) = (mate.X, mate.Y, 4);
        int unitX = unit.X, mateX = mate.X;

        Assert.Equal(SwapResult.Swapped, systems.PlaceSwap.Swap(unit, mate));

        Assert.Equal((mateX, unitX, unitX, 0), (unit.X, mate.X, mate.TaskSiteX, mate.PathLength));
        Assert.Equal((StepMotion.Swapping, 4), (unit.PathBlock, mate.Facing));
        Assert.Equal(MapCell.UnitAt(23), world.Map.CellAt(new GridPoint(unitX, unit.Y)));
    }

    /// <summary>Whether unit <paramref name="id"/> can take a step (its speed class has frames).</summary>
    internal static bool Walks(Legacy.GameCanvas game, int id)
    {
        int speed = LegacySamples.Field(game, MoveDelayField, id) - LegacySamples.Field(game, RunField, id);
        return speed >= 2 && speed <= 8;
    }
}
