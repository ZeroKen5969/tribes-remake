using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Passability"/> (what blocks units and animals, the player's fog) with the original, plus edge cases.</summary>
public sealed class PassabilityTests
{
    private const int UnitsPerSample = 6;
    private const int AnimalsPerSample = 4;
    private const int PathBlockField = 48;

    private readonly ITestOutputHelper output;

    public PassabilityTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void BlocksUnit_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                GridPoint at = UnitCell(game, id);
                GridPoint[] cells = Around(game, at.X, at.Y, 6, 2).ToArray();
                foreach (bool blockedBefore in new[] { false, true })
                {
                    run.Check(sample, $"BlocksUnit unit {id} blocked {blockedBefore}",
                        g =>
                        {
                            if (blockedBefore)
                            {
                                g.bArr_e[(PathBlockField * 101) + id] = 1;
                            }
                            g.world_a.i_v = id;
                            g.world_a.i_u = TribeOf(id);
                            return string.Concat(cells.Select(c => CodeAt(g, c.X, c.Y) == 0 ? 0 : g.world_a.a_III(c.X, c.Y, 0)));
                        },
                        s =>
                        {
                            Unit unit = s.World.Unit(id);
                            if (blockedBefore)
                            {
                                unit.PathBlock = 1;
                            }
                            return string.Concat(cells.Select(c => s.Passability.BlocksUnit(unit, c) ? 1 : 0));
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void BlocksAnimal_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int index in SampleInputs.Spread(LiveAnimals(game), AnimalsPerSample))
            {
                GridPoint[] cells = Around(game, AnimalField(game, AnimalX, index), AnimalField(game, AnimalY, index), 4).ToArray();
                run.Check(sample, $"BlocksAnimal animal {index}",
                    g =>
                    {
                        g.world_a.i_v = index;
                        return string.Concat(cells.Select(c => CodeAt(g, c.X, c.Y) == 0 ? 0 : g.world_a.a_III(c.X, c.Y, 101)));
                    },
                    s => string.Concat(cells.Select(c => s.Passability.BlocksAnimal(s.World.Animal(index), c) ? 1 : 0)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 80);
    }

    [Fact]
    public void FogAt_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            GridPoint[] cells = Enumerable.Range(0, game.s_d).SelectMany(y => Enumerable.Range(0, game.s_c).Select(x => new GridPoint(x, y))).ToArray();
            run.Check(sample, "FogAt",
                g => string.Concat(cells.Select(c => g.a_II_B(c.X, c.Y))),
                s => string.Concat(cells.Select(c => (int)s.Passability.FogAt(c))));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void BlocksUnit_FarWalkersAndReservedCellsDoNotBlock()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(51);
        (unit.X, unit.Y, unit.Age, unit.Action) = (5, 5, 20, 69);
        world.Unit(52).Animation = 0;
        world.Map.SetCell(new GridPoint(10, 5), MapCell.UnitAt(52));
        world.Map.SetCell(new GridPoint(11, 5), MapCell.Of(CellContent.Reserved));
        world.Map.SetCell(new GridPoint(6, 5), MapCell.Of(CellContent.Reserved));
        world.Map.SetCell(new GridPoint(5, 6), MapCell.Of(CellContent.Shore));

        Assert.False(systems.Passability.BlocksUnit(unit, new GridPoint(10, 5)));
        Assert.False(systems.Passability.BlocksUnit(unit, new GridPoint(11, 5)));
        Assert.True(systems.Passability.BlocksUnit(unit, new GridPoint(6, 5)));
        Assert.True(systems.Passability.BlocksUnit(unit, new GridPoint(5, 6)));
        unit.Action = 13;
        Assert.False(systems.Passability.BlocksUnit(unit, new GridPoint(5, 6)));
    }

    [Fact]
    public void BlocksUnit_AdultsWalkIntoTheOtherTribesTraps()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(60);
        (unit.X, unit.Y, unit.Age) = (5, 5, 20);
        var trap = new GridPoint(6, 5);
        world.Map.SetCell(trap, new MapCell(CellContent.Trap, CellCode.FirstTrapKind, 0));

        Assert.False(systems.Passability.BlocksUnit(unit, trap));
        unit.Age = 5;
        Assert.True(systems.Passability.BlocksUnit(unit, trap));
    }

    [Fact]
    public void FogAt_TellsVisibleExploredAndUnexplored()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Fog(0).SetExplored(new GridPoint(1, 1), true);
        world.Fog(0).SetExplored(new GridPoint(2, 2), true);
        world.Fog(0).SetVisible(new GridPoint(2, 2), true);

        Assert.Equal(FogState.Unexplored, systems.Passability.FogAt(new GridPoint(0, 0)));
        Assert.Equal(FogState.Explored, systems.Passability.FogAt(new GridPoint(1, 1)));
        Assert.Equal(FogState.Visible, systems.Passability.FogAt(new GridPoint(2, 2)));
    }
}
