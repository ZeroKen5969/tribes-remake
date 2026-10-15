using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="TargetDistances"/> (goal distances, the holding building) with the original, plus edge cases.</summary>
public sealed class TargetDistancesTests
{
    private const int UnitsPerSample = 6;
    private const int AnimalsPerSample = 4;

    private readonly ITestOutputHelper output;

    public TargetDistancesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void FromUnit_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                GridPoint at = UnitCell(game, id);
                GridPoint[] cells = Around(game, at.X, at.Y, 4, 7).Append(UnitDestination(game, id)).ToArray();
                run.Check(sample, $"FromUnit unit {id}",
                    g => string.Join(";", cells.Select(from =>
                    {
                        g.world_a.i_v = id;
                        bool pending = LegacySamples.Field(g, UnitTargetIndex, id) < -1;
                        int measured = g.world_a.a_III_I(from.X, from.Y, 0);
                        return pending ? $"{measured}" : $"{measured}@{g.world_a.b_q},{g.world_a.b_r}";
                    })),
                    s => string.Join(";", cells.Select(from =>
                    {
                        Unit unit = s.World.Unit(id);
                        bool pending = unit.TargetIndex < -1;
                        GoalDistance goal = s.TargetDistances.FromUnit(unit, from);
                        return pending ? $"{goal.Measured}" : $"{goal.Measured}@{goal.Goal.X},{goal.Goal.Y}";
                    })));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void FromAnimal_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int index in SampleInputs.Spread(LiveAnimals(game), AnimalsPerSample))
            {
                int x = AnimalField(game, AnimalX, index), y = AnimalField(game, AnimalY, index);
                GridPoint[] cells = Around(game, x, y, 3, 5).ToArray();
                run.Check(sample, $"FromAnimal animal {index}",
                    g => string.Join(";", cells.Select(from =>
                    {
                        g.world_a.i_v = index;
                        int measured = g.world_a.a_III_I(from.X, from.Y, 101);
                        return $"{measured}@{g.world_a.b_q},{g.world_a.b_r}";
                    })),
                    s => string.Join(";", cells.Select(from =>
                    {
                        GoalDistance goal = s.TargetDistances.FromAnimal(s.World.Animal(index), from);
                        return $"{goal.Measured}@{goal.Goal.X},{goal.Goal.Y}";
                    })));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 80);
    }

    [Fact]
    public void AimAtHoldingBuilding_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (int id in LegacySamples.LiveUnits(sample.Restore()).Where(id => id % 3 == sample.Frame / 1000 % 3))
            {
                foreach (bool anyKind in new[] { false, true })
                {
                    run.Check(sample, $"AimAtHoldingBuilding unit {id} any {anyKind}",
                        g =>
                        {
                            g.world_a.i_v = id;
                            g.world_a.i_u = TribeOf(id);
                            g.world_a.b_Z(anyKind);
                        },
                        s => s.TargetDistances.AimAtHoldingBuilding(s.World.Unit(id), anyKind));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void FromUnit_WithAPendingTargetSearch_IsAtTheDestination()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(1);
        unit.TargetIndex = -6;
        unit.DestinationX = 7;
        unit.DestinationY = 9;

        GoalDistance goal = systems.TargetDistances.FromUnit(unit, new GridPoint(1, 1));

        Assert.Equal(0, goal.Measured);
        Assert.Equal(new GridPoint(7, 9), goal.Goal);
    }

    [Fact]
    public void FromUnit_BeyondTheTable_IsOutOfReach()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit unit = world.Unit(1);
        unit.Action = 69;
        unit.DestinationX = 30;
        unit.DestinationY = 2;

        GoalDistance goal = systems.TargetDistances.FromUnit(unit, new GridPoint(2, 2));

        Assert.Equal(GridDistances.OutOfReach, goal.Measured);
        Assert.Equal(20, goal.Cells);
    }
}
