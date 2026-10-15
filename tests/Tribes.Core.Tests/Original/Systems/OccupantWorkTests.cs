using System.Linq;
using Tribes.Core.Systems.Buildings;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="OccupantWork"/> (the work of the units inside buildings) with the original.</summary>
public sealed class OccupantWorkTests
{
    private const int Hut = 2, Kitchen = 4, FirstSite = 12;

    private readonly ITestOutputHelper output;

    public OccupantWorkTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ScanOccupants_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in BuildingSamples.All.Where((_, i) => i % 3 == 0).Concat(RichSamples.All.Where((_, i) => i % 4 == 0)))
        {
            GameCanvas game = sample.Restore();
            GameWorld world = LegacyRules.Import(game, LegacySamples.Content);
            foreach (Building building in Enumerable.Range(1, world.BuildingSlots).Select(world.Building).Where(Worked))
            {
                int id = building.Id, kind = building.Kind;
                int job = JobIndex(world, building);
                if (job < 0)
                {
                    continue;
                }
                bool hut = kind == Hut && building.WorkMode == BuildingWorkMode.Normal;
                run.Check(sample, $"ScanOccupants building {id} kind {kind}",
                    g =>
                    {
                        Tribes.Legacy.World w = g.world_a;
                        (w.i_v, w.i_u, w.i_z, w.i_x, w.i_A, w.i_C) = (id, building.Tribe, kind, job, 0, 0);
                        w.aa_();
                        return (w.i_C, hut ? w.i_x : 0, hut ? w.i_A : 0);
                    },
                    s =>
                    {
                        Building b = s.World.Building(id);
                        var turn = new BuildingTurn(b, kind, Construction(s.World, b), Upgrade(s.World, b));
                        OccupantScan scan = s.OccupantWork.ScanOccupants(turn);
                        return (scan.Occupants, scan.Sleeper, scan.CouplePartners);
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    /// <summary>The buildings whose occupants the buildings' update sets to work.</summary>
    private static bool Worked(Building building) =>
        building.InUse && (building.Kind >= FirstSite || building.Kind == Kitchen || building.Kind == Hut
            || building.WorkMode != BuildingWorkMode.Normal);

    /// <summary>The list entry the original's register holds for the building, or -1 when it has none.</summary>
    private static int JobIndex(GameWorld world, Building building)
    {
        if (building.WorkMode != BuildingWorkMode.Normal)
        {
            return IndexOf(world.Upgrades.Select(j => j.Building), building.Id);
        }
        return building.Kind >= FirstSite ? IndexOf(world.Construction.Select(j => j.Building), building.Id) : 0;
    }

    private static int IndexOf(System.Collections.Generic.IEnumerable<int> slots, int building)
    {
        int i = 0;
        foreach (int slot in slots)
        {
            if (slot == building)
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    private static ConstructionJob? Construction(GameWorld world, Building building) =>
        building.Kind >= FirstSite ? world.Construction.FirstOrDefault(j => j.Building == building.Id) : null;

    private static UpgradeJob? Upgrade(GameWorld world, Building building) =>
        building.WorkMode != BuildingWorkMode.Normal ? world.Upgrades.FirstOrDefault(j => j.Building == building.Id) : null;
}
