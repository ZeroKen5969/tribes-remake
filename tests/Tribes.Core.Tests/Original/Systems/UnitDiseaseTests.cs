using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitDisease"/> with the original, plus edge cases.</summary>
public sealed class UnitDiseaseTests
{
    private const int ExposureField = 80, TargetField = 44, BuildingFlags = 8871, SickOccupant = 0x02;

    private readonly ITestOutputHelper output;

    public UnitDiseaseTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void UpdateExposure_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 4))
            {
                int target = LegacySamples.Field(game, TargetField, id);
                bool inBuilding = !UnitSampling.OnItsCell(game, id) && target >= 1 && target <= 60;
                foreach (int exposure in new[] { 0, 29, 12 })
                {
                    run.Check(sample, $"UpdateExposure unit {id} exposure {exposure} sick building {inBuilding}",
                        g =>
                        {
                            UnitSampling.Set(g, ExposureField, id, exposure);
                            if (inBuilding)
                            {
                                g.bArr_e[BuildingFlags + target] = (sbyte)(g.bArr_e[BuildingFlags + target] | SickOccupant);
                            }
                            UnitSampling.Current(g, id).X_();
                        },
                        s =>
                        {
                            s.World.Unit(id).DiseaseExposure = exposure;
                            if (inBuilding)
                            {
                                s.World.Building(target).Flags |= SickOccupant;
                            }
                            s.UnitDisease.UpdateExposure(s.World.Unit(id));
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void SpreadFrom_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6))
            {
                run.Check(sample, $"SpreadFrom unit {id}",
                    g => UnitSampling.Current(g, id).Y_(),
                    s => s.UnitDisease.SpreadFrom(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void SpreadFrom_ExposesHealthyAdultNeighboursOnly()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit sick = UnitNeedsTests.Adult(world, 1);
        Unit healthy = Place(world, 2, sick.X + 1, sick.Y, age: 30);
        Unit child = Place(world, 3, sick.X, sick.Y + 1, age: 5);
        Unit alsoSick = Place(world, 4, sick.X - 1, sick.Y - 1, age: 30);
        alsoSick.Status = UnitStatus.Diseased;

        systems.UnitDisease.SpreadFrom(sick);

        Assert.Equal((8, 0, 0), (healthy.DiseaseExposure, child.DiseaseExposure, alsoSick.DiseaseExposure));
    }

    [Fact]
    public void UpdateExposure_FullExposureMakesAUnitSick()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 5);
        unit.DiseaseExposure = 31;

        systems.UnitDisease.UpdateExposure(unit);

        Assert.Equal((UnitStatus.Diseased, 0), (unit.Status, unit.DiseaseExposure));
    }

    private static Unit Place(GameWorld world, int id, int x, int y, int age)
    {
        Unit unit = world.Unit(id);
        (unit.X, unit.Y, unit.Age, unit.HitPoints) = (x, y, age, 10);
        world.Map.SetCell(new GridPoint(x, y), MapCell.UnitAt(id));
        return unit;
    }
}
