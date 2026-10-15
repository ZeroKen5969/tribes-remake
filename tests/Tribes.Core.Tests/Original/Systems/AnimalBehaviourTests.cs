using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimalBehaviour"/> (an animal's decisions each frame) with the original, plus edge cases.</summary>
public sealed class AnimalBehaviourTests
{
    private const int AnimalsPerSample = 3;

    private readonly ITestOutputHelper output;

    public AnimalBehaviourTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in WildlifeSamples.All.Where((_, i) => i % 2 == 1).Concat(MissionSamples.All).Concat(LegacySamples.All.Where((_, i) => i % 2 == 0)))
        {
            var game = sample.Restore();
            foreach (int animal in SampleInputs.Spread(LiveAnimals(game), AnimalsPerSample))
            {
                run.Check(sample, $"Update animal {animal}",
                    g => { g.world_a.i_v = animal; g.world_a.af_(); },
                    s => s.AnimalBehaviour.Update(s.World.Animal(animal)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Update_OffACellCentreKeepsWalking()
    {
        GameWorld world = TestWorlds.Empty();
        Animal animal = world.Animal(0);
        (animal.HitPoints, animal.Kind, animal.X, animal.Y, animal.OffsetX, animal.Action) = (20, 1, 5, 5, 4, 30);
        new RuleSystems(world).AnimalBehaviour.Update(animal);
        Assert.Equal(AnimalAnimations.Walk(1), animal.Action);
    }

    [Fact]
    public void Update_AnIdleAnimalGrowsRestlessAndWanders()
    {
        GameWorld world = TestWorlds.Empty();
        Animal animal = world.Animal(0);
        (animal.HitPoints, animal.Kind, animal.X, animal.Y, animal.DestinationX, animal.DestinationY) = (20, 2, 25, 11, 25, 11);
        animal.ProvokeTimer = AnimalTimers.RestlessAfter + 1;
        new RuleSystems(world).AnimalBehaviour.Update(animal);
        var home = world.Content.Rules.Missions.Missions[0].AnimalSpawnPoints[0].Cell;
        int radius = world.Content.Rules.Missions.Missions[0].AnimalWanderRadius;
        Assert.True(animal.ProvokeTimer <= 1);
        Assert.InRange(animal.DestinationX, home.X - radius, home.X + radius);
        Assert.InRange(animal.DestinationY, home.Y - radius, home.Y + radius);
    }
}
