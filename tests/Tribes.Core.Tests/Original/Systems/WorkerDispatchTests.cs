using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Buildings;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkerDispatch"/> (occupant counts, builders sent) with the original, plus edge cases.</summary>
public sealed class WorkerDispatchTests
{
    private readonly ITestOutputHelper output;

    public WorkerDispatchTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void RecountAndSendBuilders_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in BuildingSamples.All.Where((_, i) => i % 3 == 0).Concat(MissionSamples.All.Where((_, i) => i % 6 == 0)))
        {
            GameCanvas game = sample.Restore();
            GameWorld world = LegacyRules.Import(game, LegacySamples.Content);
            foreach (Building building in SampleInputs.Spread(Enumerable.Range(1, world.BuildingSlots).Select(world.Building).Where(b => b.HitPoints > 0), 3)
                .Concat(Enumerable.Range(1, world.BuildingSlots).Select(world.Building).Where(b => b.HitPoints > 0 && (b.Kind >= 12 || b.WorkMode != BuildingWorkMode.Normal))).Distinct())
            {
                int id = building.Id, tribe = building.Tribe;
                int listed = Listed(world, building);
                run.Check(sample, $"RecountOccupants building {id}",
                    g => { (g.world_a.i_v, g.world_a.i_u, g.world_a.i_C) = (id, tribe, listed); g.world_a.ac_(); },
                    s => s.WorkerDispatch.RecountOccupants(s.World.Building(id), listed));
                if (building.Kind >= 12 || building.WorkMode != BuildingWorkMode.Normal)
                {
                    run.Check(sample, $"SendBuilders building {id}",
                        g => { (g.world_a.i_v, g.world_a.i_u) = (id, tribe); g.world_a.ae_(); },
                        s => s.WorkerDispatch.SendBuilders(s.World.Building(id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void SendBuilders_NobodyWhileTheWholeTribeIsOut()
    {
        GameWorld world = TestWorlds.Empty();
        world.Orders.AllOut = true;
        Building site = world.Building(3);
        (site.HitPoints, site.Kind, site.Level) = (5, 18, 3);
        Unit unit = world.Unit(1);
        (unit.HitPoints, unit.Action, unit.AssignedTask) = (10, 69, 56);
        new RuleSystems(world).WorkerDispatch.SendBuilders(site);
        Assert.Equal(0, site.DispatchedWorkers);
        Assert.Equal(69, unit.Action);
    }

    private static int Listed(GameWorld world, Building building)
    {
        int count = 0;
        for (int id = building.FirstOccupant; id > 0; id = world.Unit(id).NextOccupantOrCellObject)
        {
            count++;
        }
        return count;
    }
}
