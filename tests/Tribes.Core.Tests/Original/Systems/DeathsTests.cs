using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Deaths"/> (animals, units and buildings dying) with the original, plus edge cases.</summary>
public sealed class DeathsTests
{
    private const int AnimalHitPoints = 106, UnitHitPoints = 19, BuildingHitPoints = 85;

    private readonly ITestOutputHelper output;

    public DeathsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Die_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.All.Where((_, i) => i % 2 == 0).Concat(WildlifeSamples.All.Where((_, i) => i % 7 == 0)))
        {
            var game = sample.Restore();
            var units = LegacySamples.LiveUnits(game).ToList();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), 2))
            {
                var at = new GridPoint(AnimalField(game, AnimalX, animal), AnimalField(game, AnimalY, animal));
                run.Check(sample, $"Die animal {animal}",
                    g => { g.world_a.i_v = units.FirstOrDefault(); g.world_a.a_IIIB(AnimalHitPoints, at.X, at.Y, (sbyte)animal); },
                    s => s.Deaths.Die(EntityProperty.AnimalHitPoints, at, animal, updatedLaterThisFrame: false));
            }
            foreach (int unit in SampleInputs.Spread(units, 3))
            {
                GridPoint at = UnitCell(game, unit);
                foreach (int current in new[] { 1, 100 })
                {
                    run.Check(sample, $"Die unit {unit} while {current} acts",
                        g => { g.world_a.i_v = current; g.world_a.a_IIIB(UnitHitPoints, at.X, at.Y, (sbyte)unit); },
                        s => s.Deaths.Die(EntityProperty.UnitHitPoints, at, unit, updatedLaterThisFrame: unit > current));
                }
            }
            foreach (int building in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 2))
            {
                var at = new GridPoint(LegacySamples.BuildingField(game, 0, building), LegacySamples.BuildingField(game, 1, building));
                run.Check(sample, $"Die building {building}",
                    g => g.world_a.a_IIIB(BuildingHitPoints, at.X, at.Y, (sbyte)building),
                    s => s.Deaths.Die(EntityProperty.BuildingHitPoints, at, building, updatedLaterThisFrame: false));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Die_AnimalLeavesACarcassOnItsCell()
    {
        GameWorld world = TestWorlds.Empty();
        Animal animal = world.Animal(2);
        (animal.HitPoints, animal.Kind, animal.X, animal.Y, animal.Action) = (5, 1, 6, 6, 11);
        world.Map.SetCell(new GridPoint(6, 6), MapCell.AnimalAt(2));
        new RuleSystems(world).Deaths.Die(EntityProperty.AnimalHitPoints, new GridPoint(6, 6), 2, false);
        Assert.False(animal.InUse);
        MapCell cell = world.Map.CellAt(new GridPoint(6, 6));
        Assert.Equal(CellContent.ResourceNode, cell.Content);
        ResourceNode carcass = world.Map.ResourceNodes[cell.Index];
        Assert.Equal((Tribes.Core.Systems.Map.NodeKinds.Carcass, world.Content.Rules.Animals.Kinds[1].CarcassAmount), (carcass.Kind, carcass.Amount));
    }
}
