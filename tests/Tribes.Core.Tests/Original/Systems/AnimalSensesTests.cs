using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimalSenses"/> (lookout, reacting, roaming) with the original, plus edge cases.</summary>
public sealed class AnimalSensesTests
{
    private const int LookoutMode = 6, LookoutRounds = 6, AnimalsPerSample = 2;
    private static readonly int[] Threats = { AnimalThreats.Hit, AnimalThreats.Sighting };

    private readonly ITestOutputHelper output;

    public AnimalSensesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void LookOut_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All.Where((_, i) => i % 2 == 0).Concat(MissionSamples.All))
        {
            var game = sample.Restore();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), AnimalsPerSample))
            {
                int x = AnimalField(game, AnimalX, animal), y = AnimalField(game, AnimalY, animal);
                run.Check(sample, $"LookOut animal {animal}",
                    g => { g.world_a.i_v = animal; g.world_a.a_IIII_Z(LookoutMode, LookoutRounds, x, y); },
                    s => s.AnimalSenses.LookOut(s.World.Animal(animal)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void ReactAndRoam_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All.Where((_, i) => i % 4 == 1).Concat(MissionSamples.All.Where((_, i) => i % 3 == 0)))
        {
            var game = sample.Restore();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), 1))
            {
                int facing = (animal + sample.Frame) & 7;
                int unit = LegacySamples.LiveUnits(game).FirstOrDefault();
                if (unit > 0)
                {
                    GridPoint at = UnitCell(game, unit);
                    foreach (int threat in Threats)
                    {
                        run.Check(sample, $"React animal {animal} unit {unit} threat {threat}",
                            g => { g.world_a.i_v = animal; g.world_a.b_E = (sbyte)facing; g.world_a.a_IIBI(at.X, at.Y, (sbyte)unit, threat); },
                            s => s.AnimalSenses.React(s.World.Animal(animal), at, unit, threat, facing));
                    }
                }
                foreach (bool flee in new[] { false, true })
                {
                    run.Check(sample, $"Roam animal {animal} flee {flee}",
                        g => { g.world_a.i_v = animal; g.world_a.b_E = (sbyte)facing; g.world_a.e_Z(flee); },
                        s => s.AnimalSenses.Roam(s.World.Animal(animal), flee, facing));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Roam_InThePenStaysPut()
    {
        GameWorld world = TestWorlds.Empty(40, 32);
        world.Mission = AnimalMissions.PenMission;
        Animal animal = world.Animal(0);
        (animal.HitPoints, animal.X, animal.Y, animal.SpawnZone, animal.PathLength) = (20, 22, 3, 0, 4);
        bool moved = new RuleSystems(world).AnimalSenses.Roam(animal, false, 0);
        Assert.False(moved);
        Assert.Equal((22, 3, 0), (animal.DestinationX, animal.DestinationY, animal.PathLength));
    }

    [Fact]
    public void Roam_FleeingIntoAWallMovesToTheNearestFreeCell()
    {
        GameWorld world = TestWorlds.Empty(40, 32);
        Animal animal = world.Animal(0);
        (animal.HitPoints, animal.X, animal.Y, animal.Kind) = (20, 10, 10, 2);
        world.Map.SetCell(new GridPoint(11, 10), Maps.MapCell.Of(Maps.CellContent.Rocks));
        bool moved = new RuleSystems(world).AnimalSenses.Roam(animal, true, 0);
        Assert.True(moved);
        Assert.Equal((12, 10), (animal.DestinationX, animal.DestinationY));
    }
}
