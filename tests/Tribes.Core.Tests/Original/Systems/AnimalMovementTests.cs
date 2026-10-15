using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimalMovement"/> (walking sub-steps) with the original, plus edge cases.</summary>
public sealed class AnimalMovementTests
{
    private readonly ITestOutputHelper output;

    public AnimalMovementTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Step_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All.Where((_, i) => i % 3 == 2).Concat(MissionSamples.All.Where((_, i) => i % 2 == 1)))
        {
            var game = sample.Restore();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), 3))
            {
                run.Check(sample, $"UpdateThenStep animal {animal}",
                    g => { g.world_a.i_v = animal; g.world_a.af_(); g.world_a.ah_(); },
                    s => { s.AnimalBehaviour.Update(s.World.Animal(animal)); s.AnimalMovement.Step(s.World.Animal(animal)); });
                if (AnimalField(game, 2, animal) != 0 || AnimalField(game, 3, animal) != 0)
                {
                    run.Check(sample, $"Step animal {animal}",
                        g => { g.world_a.i_v = animal; g.world_a.ah_(); },
                        s => s.AnimalMovement.Step(s.World.Animal(animal)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Step_ChangesCellHalfwayThroughTheStep(int facing)
    {
        GameWorld world = TestWorlds.Empty();
        Animal animal = world.Animal(3);
        (animal.HitPoints, animal.Kind, animal.X, animal.Y, animal.Facing) = (20, 0, 10, 10, facing);
        animal.Action = AnimalAnimations.Walk(0);
        world.Map.SetCell(new GridPoint(10, 10), MapCell.AnimalAt(3));
        DirectionStep step = world.Content.Rules.Movement.Directions[facing];
        AnimalMovement movement = new RuleSystems(world).AnimalMovement;
        SpeedClass speed = world.Content.Rules.Movement.SpeedClasses[world.Content.Rules.Animals.Kinds[0].SpeedClass];
        int frames = (facing & 1) == 0 ? speed.Straight.StepFrames : speed.Diagonal.StepFrames;
        for (int i = 0; i < (frames + 1) / 2; i++)
        {
            movement.Step(animal);
        }
        Assert.Equal((10 + step.Dx, 10 + step.Dy), (animal.X, animal.Y));
        Assert.Equal(MapCell.AnimalAt(3), world.Map.CellAt(new GridPoint(animal.X, animal.Y)));
        Assert.True(world.Map.CellAt(new GridPoint(10, 10)).IsEmpty);
    }

    [Fact]
    public void Step_StandingAnimalDoesNotMove()
    {
        GameWorld world = TestWorlds.Empty();
        Animal animal = world.Animal(0);
        (animal.HitPoints, animal.X, animal.Y, animal.Action) = (20, 4, 4, AnimalAnimations.Stand(0));
        new RuleSystems(world).AnimalMovement.Step(animal);
        Assert.Equal((4, 4, 0), (animal.X, animal.Y, animal.StepCounter));
    }
}
