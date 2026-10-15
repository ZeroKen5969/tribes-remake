using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="HutBirths"/> with the original's births in huts, plus edge cases.</summary>
public sealed class HutBirthsTests
{
    private const int Kind = 8749, Aux = 9237, Hut = 2, Cooldown = 18;

    private readonly ITestOutputHelper output;

    public HutBirthsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1))
        {
            foreach (int tribe in new[] { 0, 1 })
            {
                foreach (int aux in new[] { 3, 8, 9 })
                {
                    run.Check(sample, $"Births {tribe}:{aux}", g =>
                    {
                        Prepare(g, aux);
                        g.world_a.i_u = tribe;
                        g.world_a.Z_();
                    }, s =>
                    {
                        Prepare(s.World, aux);
                        s.HutBirths.Update(tribe);
                    });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Update_GivesNoBirthsInTheChieflessMission()
    {
        RuleSystems systems = ScenarioSamples.Started(3);
        int population = systems.World.Tribes[0][TribeCounter.Population];
        systems.World.Tribes[1][TribeCounter.BirthCooldown] = -1;
        systems.HutBirths.Update(0);
        systems.HutBirths.Update(1);
        Assert.Equal(population, systems.World.Tribes[0][TribeCounter.Population]);
    }

    /// <summary>Every hut's counter is set to <paramref name="aux"/>; the computer may have a child.</summary>
    private static void Prepare(Tribes.Legacy.GameCanvas game, int aux)
    {
        for (int id = 1; id <= 60; id++)
        {
            if (game.bArr_e[Kind + id] == Hut)
            {
                game.bArr_e[Aux + id] = (sbyte)aux;
            }
        }
        game.sArr2_a[1][Cooldown] = -1;
    }

    private static void Prepare(GameWorld world, int aux)
    {
        for (int id = 1; id <= 60; id++)
        {
            if (world.Building(id).Kind == Hut)
            {
                world.Building(id).Aux = aux;
            }
        }
        world.Tribes[1][TribeCounter.BirthCooldown] = -1;
    }
}
