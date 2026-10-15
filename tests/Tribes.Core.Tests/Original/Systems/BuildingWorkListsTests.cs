using System;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="BuildingWorkLists"/> (dust, construction and upgrade lists) with the original, plus edge cases.</summary>
public sealed class BuildingWorkListsTests
{
    private const int BuildingsPerSample = 6;

    private readonly ITestOutputHelper output;

    public BuildingWorkListsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void AddDust_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (int b in SampleInputs.Spread(LegacySamples.UsedBuildings(sample.Restore()), BuildingsPerSample))
            {
                int kind = b % 2 == 0 ? BuildingWorkLists.CompletionDust : BuildingWorkLists.DemolitionDust;
                run.Check(sample, $"AddDust building {b} kind {kind}", g => g.a_II(b, kind), s => s.BuildingWorkLists.AddDust(b, kind));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void AdvanceDust_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            run.Check(sample, "AdvanceDust", g => g.portAdvanceDust(), s => s.BuildingWorkLists.AdvanceDust(s.BuildingLifecycle.Free));
            foreach (int building in SampleInputs.Spread(LegacySamples.UsedBuildings(game), 2))
            {
                foreach (int kind in new[] { BuildingWorkLists.CompletionDust, BuildingWorkLists.DemolitionDust })
                {
                    run.Check(sample, $"AdvanceDust settling over {building} kind {kind}",
                        g =>
                        {
                            int slot = Array.IndexOf(g.bArr2_e[0], (sbyte)0);
                            (g.bArr2_e[0][slot], g.bArr2_e[1][slot], g.bArr2_e[2][slot]) = ((sbyte)building, BuildingWorkLists.DustFrames, (sbyte)kind);
                            g.portAdvanceDust();
                        },
                        s =>
                        {
                            DustCloud cloud = s.World.Dust.First(d => d.Building == 0);
                            (cloud.Building, cloud.Frames, cloud.Kind) = (building, BuildingWorkLists.DustFrames, kind);
                            s.BuildingWorkLists.AdvanceDust(s.BuildingLifecycle.Free);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void ConstructionAndUpgradeLists_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int b in SampleInputs.Spread(LegacySamples.UsedBuildings(game), BuildingsPerSample))
            {
                run.Check(sample, $"AddConstructionSite {b}", g => g.world_a.a_B_Z((sbyte)b), s => s.BuildingWorkLists.AddConstructionSite(b));
                run.Check(sample, $"AddUpgrade {b}", g => g.world_a.b_B_Z((sbyte)b), s => s.BuildingWorkLists.AddUpgrade(b));
            }
            foreach (int b in game.world_a.bArr2_f[0].Where(b => b > 0).Select(b => (int)b).Append(1))
            {
                run.Check(sample, $"RemoveConstructionSite {b}", g => g.world_a.c_I_V(b), s => s.BuildingWorkLists.RemoveConstructionSite(b));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Lists_WhenFull_RefuseMore()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        foreach (ConstructionJob job in world.Construction)
        {
            job.Building = 5;
        }
        foreach (UpgradeJob job in world.Upgrades)
        {
            job.Building = 5;
        }
        foreach (DustCloud cloud in world.Dust)
        {
            cloud.Building = 5;
        }

        Assert.False(systems.BuildingWorkLists.AddConstructionSite(2));
        Assert.False(systems.BuildingWorkLists.AddUpgrade(2));
        systems.BuildingWorkLists.AddDust(2, BuildingWorkLists.CompletionDust);
        Assert.DoesNotContain(world.Dust, d => d.Building == 2);
        systems.BuildingWorkLists.RemoveConstructionSite(5);
        Assert.All(world.Construction, job => Assert.Equal(0, job.Building));
    }

    [Fact]
    public void AddUpgrade_OfAHut_SendsItsCoupleAway()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Building hut = world.Building(2);
        (hut.HitPoints, hut.Kind, hut.Aux, hut.OperatorOrBeds) = (20, 2, 1, 3);
        Couple couple = world.Couples[0];
        (couple.Man, couple.Woman, couple.State, couple.Hut) = (4, 5, 1, 2);
        world.Unit(4).PairLink = 2;
        world.Unit(5).PairLink = 3;

        Assert.True(systems.BuildingWorkLists.AddUpgrade(2));

        Assert.Equal((3, 0, 0), (hut.Aux, hut.OperatorOrBeds, hut.DispatchedWorkers));
        Assert.Equal(0, couple.State);
        Assert.Equal(2, world.Upgrades[0].Building);
    }

    [Fact]
    public void AdvanceDust_FreesADemolishedBuildingWhenTheDustSettles()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Building(4).HitPoints = -1;
        (world.Dust[2].Building, world.Dust[2].Frames, world.Dust[2].Kind) = (4, BuildingWorkLists.DustFrames, BuildingWorkLists.DemolitionDust);
        Building? freed = null;

        systems.BuildingWorkLists.AdvanceDust(b => freed = b);

        Assert.Same(world.Building(4), freed);
        Assert.Equal(BuildingWorkMode.Freed, world.Building(4).WorkMode);
        Assert.Equal(0, world.Dust[2].Building);
        Assert.Equal(BuildingWorkLists.DustFrames + 1, world.Dust[2].Frames);
    }

}
