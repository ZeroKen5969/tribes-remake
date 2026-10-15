using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Trace;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="AnimalSpawning"/> (animals born in their habitats) with the original, plus edge cases.</summary>
public sealed class AnimalSpawningTests
{
    private const int AnimalHitPoints = 7;

    /// <summary>The migration mission has no births before this frame.</summary>
    private const int LateBirths = 5001;

    private readonly ITestOutputHelper output;

    public AnimalSpawningTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// On a birth frame with every animal out of the update (hit points 0, cells left as they are)
    /// the original's wildlife update is the birth and the newborn's first frame.
    /// </summary>
    [Fact]
    public void SpawnIfDue_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All.Where((_, i) => i % 2 == 0).Concat(MissionSamples.All.Where((_, i) => i % 2 == 0)))
        {
            foreach (int offset in new[] { 0, 1 })
            {
                LegacySample birth = OnBirthFrame(sample, offset);
                run.Check(birth, $"SpawnIfDue +{offset}", g => g.world_a.h__V(), s => s.WildlifeUpdate.RunFrame());
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void SpawnIfDue_FullHabitatsHaveNoBirths()
    {
        GameWorld world = TestWorlds.Empty();
        var rules = world.Content.Rules.Missions.Missions[0];
        world.Frame = (rules.AnimalSpawnInterval * AnimalSpawning.IntervalFrames) + AnimalSpawning.BirthPhase;
        world.SetAnimalZoneCount(0, rules.AnimalsPerSpawnPoint);
        long before = world.Random.State;
        new RuleSystems(world).AnimalSpawning.SpawnIfDue();
        Assert.Equal(rules.AnimalsPerSpawnPoint, world.AnimalZoneCount(0));
        Assert.NotEqual(before, world.Random.State);
        Assert.DoesNotContain(Enumerable.Range(0, world.AnimalSlots), i => world.Animal(i).InUse);
    }

    [Fact]
    public void SpawnIfDue_BringsAnAnimalIntoAHabitatWithRoom()
    {
        GameWorld world = TestWorlds.Empty();
        var rules = world.Content.Rules.Missions.Missions[0];
        world.Frame = (rules.AnimalSpawnInterval * AnimalSpawning.IntervalFrames) + AnimalSpawning.BirthPhase;
        new RuleSystems(world).AnimalSpawning.SpawnIfDue();
        Assert.Equal(1, world.AnimalZoneCount(0));
        Animal born = world.Animal(0);
        Assert.True(born.InUse);
        Assert.True(System.Math.Abs(born.X - rules.AnimalSpawnPoints[0].Cell.X) <= 5);
    }

    /// <summary>The sample with its frame moved to a birth frame (+ offset) and its animals' hit points cleared.</summary>
    private static LegacySample OnBirthFrame(LegacySample sample, int offset)
    {
        GameCanvas game = sample.Restore();
        var rules = LegacySamples.Content.Rules.Missions.Missions[game.b_q];
        int period = System.Math.Max(1, rules.AnimalSpawnInterval * AnimalSpawning.IntervalFrames);
        int frame = ((System.Math.Max(game.i_c, LateBirths) / period) + 1) * period;
        game.i_c = frame + AnimalSpawning.BirthPhase + offset;
        for (int i = 0; i < AnimalStride; i++)
        {
            game.bArr_e[AnimalBase + (AnimalHitPoints * AnimalStride) + i] = 0;
        }
        return sample with { Frame = sample.Frame + 1, State = LegacySnapshot.Capture(game) };
    }
}
