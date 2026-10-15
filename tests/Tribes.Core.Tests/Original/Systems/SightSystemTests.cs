using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Vision;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="SightSystem"/> (the player's fog) with the original, plus edge cases.</summary>
public sealed class SightSystemTests
{
    private const int VisibilityField = 13;
    private const int MaxRadius = 12;

    private readonly ITestOutputHelper output;

    public SightSystemTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void UpdateSight_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            run.Check(sample, "UpdateSight", g => g.world_a.d_(), s => s.Sight.UpdateSight());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void RevealDiscAndRect_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            int r = sample.Frame / 1000 % (MaxRadius + 1);
            var places = new[] { (-2, 3), (game.s_c - 1, game.s_d - 2), (Grid - 3, 10), (20, -1), (7, game.s_d) };
            foreach (int layer in new[] { 0, 1 })
            {
                FogLayer fogLayer = layer == 0 ? FogLayer.Explored : FogLayer.Visible;
                run.Check(sample, $"RevealDisc layer {layer} radius {r}",
                    g =>
                    {
                        g.world_a.b_S = (sbyte)layer;
                        foreach ((int x, int y) in places)
                        {
                            g.world_a.a_III_V(x, y, r);
                        }
                        g.world_a.b_S = 1;
                    },
                    s =>
                    {
                        foreach ((int x, int y) in places)
                        {
                            s.Sight.RevealDisc(fogLayer, x, y, r);
                        }
                    });
                run.Check(sample, $"RevealRect layer {layer}",
                    g =>
                    {
                        g.world_a.b_S = (sbyte)layer;
                        foreach ((int x, int y) in places)
                        {
                            g.world_a.a_IIII_V(x, y, r % 6 + 1, r % 4 + 1);
                        }
                        g.world_a.b_S = 1;
                    },
                    s =>
                    {
                        foreach ((int x, int y) in places)
                        {
                            s.Sight.RevealRect(fogLayer, x, y, r % 6 + 1, r % 4 + 1);
                        }
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 120);
    }

    [Fact]
    public void RevealSeenBuildings_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (bool forgotten in new[] { false, true })
            {
                run.Check(sample, $"RevealSeenBuildings forgotten {forgotten}",
                    g =>
                    {
                        if (forgotten)
                        {
                            for (int b = 1; b <= 60; b++)
                            {
                                g.bArr_e[Parity.EntityFields.BuildingBase + (VisibilityField * Parity.EntityFields.BuildingStride) + b] = 0;
                            }
                        }
                        g.portRevealBuildings();
                    },
                    s =>
                    {
                        if (forgotten)
                        {
                            for (int b = 1; b <= s.World.BuildingSlots; b++)
                            {
                                s.World.Building(b).Visibility = 0;
                            }
                        }
                        s.Sight.RevealSeenBuildings();
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 60);
    }

    [Fact]
    public void RevealSeenBuildings_RevealsAFootprintOnceACellIsExplored()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Building hut = world.Building(31);
        (hut.HitPoints, hut.Kind, hut.X, hut.Y) = (10, 2, 4, 4);

        systems.Sight.RevealSeenBuildings();
        Assert.False(world.Fog(0).IsExplored(new GridPoint(5, 5)));

        world.Fog(0).SetExplored(new GridPoint(4, 4), true);
        systems.Sight.RevealSeenBuildings();

        Assert.True(world.Fog(0).IsExplored(new GridPoint(5, 5)));
        Assert.Equal(SightSystem.RevealedVisibility, hut.Visibility);
    }
}
