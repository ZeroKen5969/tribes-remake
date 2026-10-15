using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="CellDamage"/> (blows struck on cells) with the original, plus edge cases.</summary>
public sealed class CellDamageTests
{
    private const int UnitCategory = 1, BuildingCategory = 2, AnimalCategory = 5, AnimalSide = 2;
    private static readonly int[] Damages = { 1, 7, 40 };

    private readonly ITestOutputHelper output;

    public CellDamageTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void AnimalBites_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All.Where((_, i) => i % 6 == 0).Concat(RichSamples.All.Where((_, i) => i % 2 == 0)))
        {
            var game = sample.Restore();
            int animal = LiveAnimals(game).FirstOrDefault(-1);
            if (animal < 0)
            {
                continue;
            }
            foreach (int unit in SampleInputs.Spread(LegacySamples.LiveUnits(game), 3))
            {
                GridPoint at = UnitCell(game, unit);
                foreach (int damage in Damages)
                {
                    run.Check(sample, $"Strike animal {animal} unit {unit} damage {damage}",
                        g => { g.world_a.i_v = animal; return g.world_a.a_IIIIIIII(at.X, at.Y, AnimalSide, UnitCategory, unit, 0, damage, 0); },
                        s => s.CellDamage.Strike(Striker.OfAnimal(animal), at, UnitCategory, unit, 0, damage, 0));
                }
            }
            foreach (int building in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 2))
            {
                var at = new GridPoint(LegacySamples.BuildingField(game, 0, building), LegacySamples.BuildingField(game, 1, building));
                run.Check(sample, $"Strike animal {animal} building {building}",
                    g => { g.world_a.i_v = animal; return g.world_a.a_IIIIIIII(at.X, at.Y, AnimalSide, BuildingCategory, building, 0, 9, 0); },
                    s => s.CellDamage.Strike(Striker.OfAnimal(animal), at, BuildingCategory, building, 0, 9, 0));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void UnitBlows_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.All.Where((_, i) => i % 2 == 1).Concat(WildlifeSamples.All.Where((_, i) => i % 9 == 0)))
        {
            var game = sample.Restore();
            var units = LegacySamples.LiveUnits(game).ToList();
            foreach (int striker in SampleInputs.Spread(units, 2))
            {
                int tribe = TribeOf(striker);
                foreach (int victim in SampleInputs.Spread(units.Where(u => u != striker), 2))
                {
                    GridPoint at = UnitCell(game, victim);
                    foreach (int reach in new[] { 0, 1 })
                    {
                        run.Check(sample, $"Strike unit {striker} unit {victim} reach {reach}",
                            g => { g.world_a.i_v = striker; g.world_a.i_u = tribe; return g.world_a.a_IIIIIIII(at.X, at.Y, tribe, UnitCategory, victim, 0, 12, reach); },
                            s => s.CellDamage.Strike(Striker.OfUnit(s.World.Unit(striker)), at, UnitCategory, victim, 0, 12, reach));
                    }
                }
                foreach (int building in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 2))
                {
                    var at = new GridPoint(LegacySamples.BuildingField(game, 0, building) + 1, LegacySamples.BuildingField(game, 1, building));
                    run.Check(sample, $"Strike unit {striker} building {building}",
                        g => { g.world_a.i_v = striker; g.world_a.i_u = tribe; return g.world_a.a_IIIIIIII(at.X, at.Y, tribe, BuildingCategory, building, 0, 30, 0); },
                        s => s.CellDamage.Strike(Striker.OfUnit(s.World.Unit(striker)), at, BuildingCategory, building, 0, 30, 0));
                }
                foreach (int animal in SampleInputs.Spread(LiveAnimals(game), 2))
                {
                    var at = new GridPoint(AnimalField(game, AnimalX, animal), AnimalField(game, AnimalY, animal));
                    run.Check(sample, $"Strike unit {striker} animal {animal}",
                        g => { g.world_a.i_v = striker; g.world_a.i_u = tribe; return g.world_a.a_IIIIIIII(at.X, at.Y, tribe, AnimalCategory, animal, 0, 25, 1); },
                        s => s.CellDamage.Strike(Striker.OfUnit(s.World.Unit(striker)), at, AnimalCategory, animal, 0, 25, 1));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Strike_OnAnEmptyCellDoesNothing()
    {
        GameWorld world = TestWorlds.Empty();
        long random = world.Random.State;
        bool hit = new RuleSystems(world).CellDamage.Strike(Striker.OfAnimal(0), new GridPoint(3, 3), 0, 0, 0, 10, 0);
        Assert.False(hit);
        Assert.Equal(random, world.Random.State);
    }

    [Fact]
    public void Striker_CellCodes()
    {
        Assert.Equal(Maps.CellCode.FirstAnimal + 4, Striker.OfAnimal(4).CellCode);
        Assert.True(Striker.OfAnimal(4).IsAnimal);
    }
}
