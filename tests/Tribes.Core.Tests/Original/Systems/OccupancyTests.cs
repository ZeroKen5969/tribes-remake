using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Occupancy"/> with the original, plus edge cases.</summary>
public sealed class OccupancyTests
{
    private const int HomeField = 34, TargetField = 44, NextOccupantField = 45, AgeField = 14;
    private const int OperatorField = 7, FirstOccupantField = 10, KindField = 6;
    private const int UnitsPerSample = 4;

    private readonly ITestOutputHelper output;

    public OccupancyTests(ITestOutputHelper output) => this.output = output;

    /// <summary>The occupants of <paramref name="building"/> in the original's list order.</summary>
    internal static List<int> Occupants(GameCanvas game, int building)
    {
        var list = new List<int>();
        for (int id = LegacySamples.BuildingField(game, FirstOccupantField, building); id > 0 && list.Count < 100;
            id = LegacySamples.Field(game, NextOccupantField, id))
        {
            list.Add(id);
        }
        return list;
    }

    [Fact]
    public void HomesAndRanges_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var live = LegacySamples.LiveUnits(game).ToList();
            var housed = live.Where(id => LegacySamples.Field(game, HomeField, id) > 0);
            var homeless = live.Where(id => LegacySamples.Field(game, HomeField, id) < 1 && LegacySamples.Field(game, AgeField, id) >= 12);
            foreach (int id in SampleInputs.Spread(housed, UnitsPerSample))
            {
                run.Check(sample, $"FreeHome unit {id}", g => g.world_a.b_I_V(id), s => s.Occupancy.FreeHome(s.World.Unit(id)));
            }
            foreach (int id in SampleInputs.Spread(homeless, UnitsPerSample).Append(game.sArr2_a[0][17]).Where(i => i > 0))
            {
                run.Check(sample, $"AssignHome unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.i_u = (id - 1) / 50; g.world_a.V_(); },
                    s => s.Occupancy.AssignHome(s.World.Unit(id)));
            }
            foreach (int b in LegacySamples.UsedBuildings(game))
            {
                run.Check(sample, $"UpdateRange building {b}",
                    g => { g.world_a.i_u = (b - 1) / 30; g.world_a.b_B_m((sbyte)b); },
                    s => s.Occupancy.UpdateRange(s.World.Building(b)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }

    [Fact]
    public void PickWorkPosition_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var buildings = LegacySamples.UsedBuildings(game).ToHashSet();
            var targeting = LegacySamples.LiveUnits(game).Where(id => buildings.Contains(LegacySamples.Field(game, TargetField, id)));
            var picked = SampleInputs.Spread(targeting, UnitsPerSample).ToList();
            foreach (int b in SampleInputs.Spread(buildings, UnitsPerSample))
            {
                int someone = LegacySamples.LiveUnits(game).FirstOrDefault(id => (id - 1) / 50 == (b - 1) / 30 && !picked.Contains(id));
                if (someone > 0)
                {
                    run.Check(sample, $"PickWorkPosition unit {someone} into {b}",
                        g => { g.bArr_e[4444 + someone] = (sbyte)b; g.world_a.i_v = someone; g.world_a.G_(); },
                        s => { s.World.Unit(someone).TargetIndex = b; s.Occupancy.PickWorkPosition(s.World.Unit(someone)); });
                }
            }
            foreach (int id in picked)
            {
                run.Check(sample, $"PickWorkPosition unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.G_(); },
                    s => s.Occupancy.PickWorkPosition(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 220);
    }

    [Fact]
    public void LeavingBuildings_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int b in LegacySamples.UsedBuildings(game))
            {
                List<int> inside = Occupants(game, b);
                int op = LegacySamples.BuildingField(game, OperatorField, b);
                if (op > 0 && op <= 100 && !inside.Contains(op))
                {
                    inside.Add(op);
                }
                if (inside.Count == 0)
                {
                    continue;
                }
                foreach (bool all in new[] { false, true })
                {
                    run.Check(sample, $"Evict building {b} all {all}",
                        g => g.a_IZ(b, all),
                        s => s.Occupancy.Evict(s.World.Building(b), all));
                }
                foreach (int id in SampleInputs.Spread(inside, 2))
                {
                    foreach ((bool forced, bool others) in new[] { (false, false), (true, false), (true, true), (false, true) })
                    {
                        run.Check(sample, $"LeaveBuilding unit {id} building {b} forced {forced} others {others}",
                            g => g.a_IIZZ(id, b, forced, others),
                            s => s.Occupancy.LeaveBuilding(s.World.Unit(id), s.World.Building(b), forced, others));
                    }
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void ReleaseWorkplaces_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var operators = LegacySamples.UsedBuildings(game)
                .Select(b => LegacySamples.BuildingField(game, OperatorField, b))
                .Where(id => id > 0 && id <= 100 && LegacySamples.Field(game, LegacySamples.UnitHitPointsField, id) != 0);
            var insiders = LegacySamples.UsedBuildings(game).SelectMany(b => Occupants(game, b));
            var units = SampleInputs.Spread(operators, UnitsPerSample)
                .Concat(SampleInputs.Spread(insiders, UnitsPerSample))
                .Concat(SampleInputs.Spread(LegacySamples.LiveUnits(game), 2)).Distinct();
            foreach (int id in units)
            {
                run.Check(sample, $"ReleaseWorkplaces unit {id}",
                    g => g.c_I(id),
                    s => s.Occupancy.ReleaseWorkplaces(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 180);
    }

    [Fact]
    public void PickWorkPosition_OverfullOrOddOccupants_MatchesTheRulesModeOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.All)
        {
            GameCanvas game = sample.Restore();
            foreach (int b in LegacySamples.UsedBuildings(game).Where(b => Occupants(game, b).Count > 0).Take(2))
            {
                int occupant = Occupants(game, b)[0];
                int tribe = (b - 1) / 30;
                int newcomer = LegacySamples.LiveUnits(game).FirstOrDefault(id => (id - 1) / 50 == tribe && !Occupants(game, b).Contains(id));
                if (newcomer == 0)
                {
                    continue;
                }
                run.CheckPrepared(sample, $"PickWorkPosition odd occupant {occupant} building {b} unit {newcomer}",
                    g => { g.bArr_e[4646 + occupant] = -90; g.bArr_e[4444 + newcomer] = (sbyte)b; },
                    g => { g.world_a.i_v = newcomer; g.world_a.G_(); },
                    s => s.Occupancy.PickWorkPosition(s.World.Unit(newcomer)));
                run.CheckPrepared(sample, $"PickWorkPosition overfull building {b} unit {newcomer}",
                    g => { g.bArr_e[8383 + (9 * 61) + b] = 20; g.bArr_e[4444 + newcomer] = (sbyte)b; },
                    g => { g.world_a.i_v = newcomer; g.world_a.G_(); },
                    s => s.Occupancy.PickWorkPosition(s.World.Unit(newcomer)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void FreeHome_OfABusyHutGivesBackTheSavedBed()
    {
        var world = TestWorlds.Empty();
        var occupancy = new Occupancy(world, new BuildingSlotTables(world.Content.Rules.Buildings.Kinds));
        Building hut = world.Building(4);
        (hut.Kind, hut.WorkMode, hut.Aux, hut.OperatorOrBeds) = (BuildingKindNumbers.Hut, BuildingWorkMode.Upgrading, 2, 0);
        world.Unit(9).Home = 4;

        occupancy.FreeHome(world.Unit(9));
        Assert.Equal((1, 0, 0), (hut.Aux, hut.OperatorOrBeds, world.Unit(9).Home));
        occupancy.FreeHome(world.Unit(9));
        Assert.Equal(1, hut.Aux);
    }

    [Fact]
    public void AssignHome_TakesTheFirstHutWithAFreeBed()
    {
        var world = TestWorlds.Empty();
        var occupancy = new Occupancy(world, new BuildingSlotTables(world.Content.Rules.Buildings.Kinds));
        int beds = world.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Hut].WorkerSlots[0];
        for (int id = 32; id <= 33; id++)
        {
            Building hut = world.Building(id);
            (hut.Kind, hut.Level, hut.HitPoints) = (BuildingKindNumbers.Hut, 1, 10);
        }
        world.Building(32).OperatorOrBeds = beds;

        occupancy.AssignHome(world.Unit(60));
        Assert.Equal((33, 1), (world.Unit(60).Home, world.Building(33).OperatorOrBeds));
        world.Tribes[0][TribeCounter.Chief] = 5;
        occupancy.AssignHome(world.Unit(5));
        Assert.Equal(1, world.Unit(5).Home);
    }

    [Fact]
    public void UpdateRange_UsesThePlayersTowerGuards()
    {
        var world = TestWorlds.Empty();
        var occupancy = new Occupancy(world, new BuildingSlotTables(world.Content.Rules.Buildings.Kinds));
        Building tower = world.Building(2);
        (tower.Kind, tower.FirstOccupant) = (BuildingKindNumbers.Tower, 7);
        (world.Unit(7).SightRadius, world.Unit(7).ScoutingSkill, world.Unit(7).NextOccupantOrCellObject) = (6, 4, 8);
        (world.Unit(8).SightRadius, world.Unit(8).ScoutingSkill) = (3, 9);

        occupancy.UpdateRange(tower);
        Assert.Equal(9, tower.RangeInfo & 0x0F);
        Assert.Equal(9 << 4, tower.RangeInfo & 0xF0);
        Building enemyTower = world.Building(40);
        (enemyTower.Kind, enemyTower.FirstOccupant) = (BuildingKindNumbers.Tower, 7);
        occupancy.UpdateRange(enemyTower);
        Assert.Equal(Occupancy.DefaultRange, enemyTower.RangeInfo);
    }
}
