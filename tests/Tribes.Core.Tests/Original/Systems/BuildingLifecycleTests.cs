using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="BuildingPlacement"/> and <see cref="BuildingLifecycle"/> with the original, plus edge cases.</summary>
public sealed class BuildingLifecycleTests
{
    private const int XField = 0, YField = 1, HitPointsField = 2, WorkModeField = 4, KindField = 6, LevelField = 12, AuxField = 14;
    private const int AllKinds = 23, BuildingsPerSample = 3;

    private readonly ITestOutputHelper output;

    public BuildingLifecycleTests(ITestOutputHelper output) => this.output = output;

    /// <summary>The original's result code of a placement.</summary>
    private static int Code(PlacementResult result) => result.Outcome switch
    {
        PlacementOutcome.Placed => result.Slot,
        PlacementOutcome.NoSlot => -1,
        _ => -2,
    };

    /// <summary>A free top-left cell for <paramref name="kind"/> in the original's grid, scanning from a rotating start.</summary>
    private static GridPoint FreeSpot(GameCanvas game, int kind, int start)
    {
        BuildingKind definition = LegacySamples.Content.Rules.Buildings.Kinds[kind];
        int cells = game.s_c * game.s_d;
        for (int i = 0; i < cells; i++)
        {
            int x = (start + i) % game.s_c, y = ((start + i) / game.s_c) % game.s_d;
            bool free = true;
            for (int dy = 0; dy < definition.Height && free; dy++)
            {
                for (int dx = 0; dx < definition.Width && free; dx++)
                {
                    free = x + dx < game.s_c && y + dy < game.s_d && game.bArr2_c[y + dy][x + dx] == 0;
                }
            }
            if (free)
            {
                return new GridPoint(x, y);
            }
        }
        return new GridPoint(0, 0);
    }

    [Fact]
    public void Place_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int n = 0;
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            foreach (int kind in SampleInputs.Pick(Enumerable.Range(0, AllKinds).ToArray(), n * 5, 5))
            {
                int tribe = n++ % 2;
                bool immediate = n % 3 != 0 || tribe != 0;
                int level = kind < BuildingKindNumbers.PlannedKinds ? n % 4 : 1 + (n % 3);
                int target = n % 12;
                int owner = LegacySamples.UsedBuildings(game).FirstOrDefault();
                int queue = n % 10;
                GridPoint spot = n % 5 == 0 && owner > 0
                    ? new GridPoint(LegacySamples.BuildingField(game, XField, owner), LegacySamples.BuildingField(game, YField, owner))
                    : FreeSpot(game, kind, n * 37);
                var request = new PlacementRequest(tribe, kind, spot, level, immediate)
                {
                    SiteTarget = target, TrapOwner = owner, TrapQueue = queue,
                };
                run.Check(sample, $"Place {request}",
                    g =>
                    {
                        g.bArr_r[g.b_as] = (sbyte)target;
                        g.world_a.i_v = target;
                        (g.i_q, g.b_au, g.i_s) = (owner, (sbyte)queue, kind);
                        return (int)g.a_BIIIIZI((sbyte)tribe, kind, spot.X, spot.Y, level, immediate, -1);
                    },
                    s => Code(s.BuildingPlacement.Place(request)));
            }
            foreach (int site in LegacySamples.UsedBuildings(game).Where(b => BuildingKindNumbers.IsSite(LegacySamples.BuildingField(game, KindField, b))))
            {
                int target = LegacySamples.BuildingField(game, AuxField, site);
                var spot = new GridPoint(LegacySamples.BuildingField(game, XField, site), LegacySamples.BuildingField(game, YField, site));
                var request = new PlacementRequest((site - 1) / 30, target, spot, 1, true) { Slot = site };
                run.Check(sample, $"Place completion {request}",
                    g => (int)g.a_BIIIIZI((sbyte)((site - 1) / 30), target, spot.X, spot.Y, 1, true, site),
                    s => Code(s.BuildingPlacement.Place(request)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void DestroyAndFree_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            var used = LegacySamples.UsedBuildings(game).ToList();
            var ruins = used.Where(b => LegacySamples.BuildingField(game, HitPointsField, b) < 0);
            foreach (int b in SampleInputs.Spread(used, BuildingsPerSample + 2))
            {
                run.Check(sample, $"Destroy building {b}", g => g.c_IZ(b, true), s => s.BuildingLifecycle.Destroy(s.World.Building(b)));
            }
            foreach (int b in ruins.Concat(SampleInputs.Spread(used, BuildingsPerSample)).Distinct())
            {
                run.Check(sample, $"Free building {b}", g => g.c_IZ(b, false), s => s.BuildingLifecycle.Free(s.World.Building(b)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Upgrades_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            GameCanvas game = sample.Restore();
            var used = LegacySamples.UsedBuildings(game).Where(b => LegacySamples.BuildingField(game, HitPointsField, b) > 0).ToList();
            var normal = used.Where(b => LegacySamples.BuildingField(game, WorkModeField, b) == 0 && LegacySamples.BuildingField(game, LevelField, b) < 3);
            foreach (int b in SampleInputs.Spread(normal, BuildingsPerSample))
            {
                foreach (bool upgrade in new[] { true, false })
                {
                    run.Check(sample, $"StartUpgrade building {b} upgrade {upgrade}",
                        g => g.b_IZ(b, upgrade),
                        s => s.BuildingLifecycle.StartUpgrade(s.World.Building(b), upgrade));
                }
            }
            sbyte[] jobs = game.world_a.bArr2_g[0];
            for (int slot = 0; slot < jobs.Length; slot++)
            {
                int b = jobs[slot];
                if (b <= 0)
                {
                    continue;
                }
                int jobSlot = slot;
                foreach (bool cancelled in new[] { false, true })
                {
                    run.Check(sample, $"FinishUpgrade building {b} cancelled {cancelled}",
                        g => { Cancel(g.bArr_e, b, cancelled); g.world_a.i_v = b; g.world_a.i_x = jobSlot; g.world_a.ad_(); },
                        s => { Cancel(s.World.Building(b), cancelled); s.BuildingLifecycle.FinishUpgrade(s.World.Building(b)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    private static void Cancel(sbyte[] state, int building, bool cancelled)
    {
        if (cancelled)
        {
            state[8383 + (11 * 61) + building] |= BuildingLifecycle.CancelUpgradeBit;
        }
    }

    private static void Cancel(Building building, bool cancelled)
    {
        if (cancelled)
        {
            building.AlertFlags |= BuildingLifecycle.CancelUpgradeBit;
        }
    }

    [Fact]
    public void Place_HutAddsLivingSpaceUpToTheLimitAndFreeTakesItBack()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        TribeState tribe = systems.World.Tribes[0];
        tribe[TribeCounter.LivingSpace] = BuildingPlacement.MaxLivingSpace - 1;
        PlacementResult hut = systems.BuildingPlacement.Place(new PlacementRequest(0, BuildingKindNumbers.Hut, new GridPoint(2, 2), 1, true));

        Assert.Equal(PlacementOutcome.Placed, hut.Outcome);
        Assert.Equal(BuildingPlacement.MaxLivingSpace, tribe[TribeCounter.LivingSpace]);
        Assert.Equal(MapCell.BuildingAt(hut.Slot), systems.World.Map.CellAt(new GridPoint(3, 3)));
        Assert.Equal(PlacementOutcome.Blocked,
            systems.BuildingPlacement.Place(new PlacementRequest(0, BuildingKindNumbers.Hut, new GridPoint(3, 3), 1, true)).Outcome);

        systems.World.Unit(5).Home = hut.Slot;
        systems.BuildingLifecycle.Free(systems.World.Building(hut.Slot));
        Assert.Equal((BuildingPlacement.MaxLivingSpace - 4, 0), (tribe[TribeCounter.LivingSpace], systems.World.Unit(5).Home));
        Assert.True(systems.World.Map.CellAt(new GridPoint(3, 3)).IsEmpty);
        Assert.Contains(systems.TakeEvents(), e => e is Tribes.Core.Systems.Notices.NoticeEvent { Kind: EntityNotices.BuildingLost });
    }

    [Fact]
    public void Place_TrapOrdersAndSetTraps()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        var cell = new GridPoint(4, 5);
        PlacementResult ordered = systems.BuildingPlacement.Place(
            new PlacementRequest(0, BuildingKindNumbers.FirstTrap + 1, cell, 1, false) { TrapOwner = 3, TrapQueue = 2 });

        Assert.Equal(PlacementOutcome.Blocked, ordered.Outcome);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                systems.World.Fog(0).SetExplored(new GridPoint(x, y), true);
                systems.World.Fog(0).SetVisible(new GridPoint(x, y), true);
            }
        }
        ordered = systems.BuildingPlacement.Place(
            new PlacementRequest(0, BuildingKindNumbers.FirstTrap + 1, cell, 1, false) { TrapOwner = 3, TrapQueue = 2 });
        Assert.Equal((PlacementOutcome.Placed, 0), (ordered.Outcome, ordered.Slot));
        Assert.Equal(1, systems.World.Tribes[0].Armouries[2].Pending(BuildingPlacement.FirstTrapProduct + 1));
        Assert.Equal(CellContent.PendingTrap, systems.World.Map.CellAt(cell).Content);

        PlacementResult set = systems.BuildingPlacement.Place(
            new PlacementRequest(1, BuildingKindNumbers.LastTrap, new GridPoint(6, 6), 1, true) { TrapOwner = 40 });
        Assert.Equal(1, set.Slot);
        Assert.Equal(new MapCell(CellContent.Trap, BuildingKindNumbers.LastTrap, 1), systems.World.Map.CellAt(new GridPoint(6, 6)));
    }

    [Fact]
    public void Place_FailsWithoutFreeSlotsOrQueues()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        for (int id = 1; id <= 30; id++)
        {
            systems.World.Building(id).HitPoints = 1;
        }
        Assert.Equal(PlacementOutcome.NoSlot,
            systems.BuildingPlacement.Place(new PlacementRequest(0, 4, new GridPoint(1, 1), 1, true)).Outcome);
        Assert.Contains(systems.TakeEvents(), e => e is Tribes.Core.Systems.Notices.NoticeEvent { Kind: EntityNotices.NoBuildingSlot });

        foreach (ArmouryOrders queue in systems.World.Tribes[1].Armouries)
        {
            queue.Armoury = 60;
        }
        Assert.Equal(PlacementOutcome.NoSlot,
            systems.BuildingPlacement.Place(new PlacementRequest(1, BuildingKindNumbers.Armoury, new GridPoint(1, 1), 1, true)).Outcome);
        foreach (ConstructionJob job in systems.World.Construction)
        {
            job.Building = 59;
        }
        Assert.Equal(PlacementOutcome.NoSlot,
            systems.BuildingPlacement.Place(new PlacementRequest(1, BuildingKindNumbers.FirstSite, new GridPoint(10, 10), 1, true)).Outcome);
        Assert.Equal(0, systems.World.Building(31).HitPoints);
    }

    [Fact]
    public void PlayerCommands_PlaceSitesAndTrapsAndDemolish()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                systems.World.Fog(0).SetExplored(new GridPoint(x, y), true);
                systems.World.Fog(0).SetVisible(new GridPoint(x, y), true);
            }
        }

        int site = systems.OrderConstruction(0, BuildingKindNumbers.Kitchen, new GridPoint(3, 3));
        Building building = systems.World.Building(site);
        BuildingKind kitchen = systems.World.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Kitchen];
        Assert.Equal((kitchen.SiteKind, BuildingKindNumbers.Kitchen, BuildingPlacement.SiteLevel), (building.Kind, building.Aux, building.Level));
        Assert.Equal(0, systems.OrderConstruction(0, BuildingKindNumbers.Kitchen, new GridPoint(3, 3)));
        Assert.True(systems.OrderTrap(0, BuildingKindNumbers.FirstTrap, new GridPoint(20, 20), site, 0));

        systems.StartBuildingWork(site, upgrade: false);
        Assert.Equal(BuildingWorkMode.Repairing, building.WorkMode);
        systems.Demolish(site);
        Assert.Equal(-1, building.HitPoints);
        Assert.Contains(systems.World.Dust, d => d.Building == site);
        Assert.Contains(systems.TakeEvents(), e => e is BuildingDestroyed d && d.FocusCamera == (site == 1));
    }

    [Fact]
    public void Upgrade_RaisesTheLevelUnlessCancelled()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        int slot = systems.BuildingPlacement.Place(new PlacementRequest(0, BuildingKindNumbers.Kitchen, new GridPoint(5, 5), 1, true)).Slot;
        Building kitchen = systems.World.Building(slot);
        kitchen.OperatorOrBeds = 7;

        systems.BuildingLifecycle.StartUpgrade(kitchen, upgrade: true);
        BuildingKind kind = systems.World.Content.Rules.Buildings.Kinds[BuildingKindNumbers.Kitchen];
        Assert.Equal((BuildingWorkMode.Upgrading, kind.MaxHitPoints[1], 7, 0), (kitchen.WorkMode, kitchen.MaxHitPoints, kitchen.Aux, kitchen.OperatorOrBeds));
        systems.BuildingLifecycle.FinishUpgrade(kitchen);
        Assert.Equal((2, BuildingWorkMode.Normal, 7), (kitchen.Level, kitchen.WorkMode, kitchen.OperatorOrBeds));
        Assert.All(systems.World.Upgrades, job => Assert.Equal(0, job.Building));

        systems.BuildingLifecycle.StartUpgrade(kitchen, upgrade: false);
        kitchen.AlertFlags = BuildingLifecycle.CancelUpgradeBit;
        systems.BuildingLifecycle.FinishUpgrade(kitchen);
        Assert.Equal((2, 0), (kitchen.Level, kitchen.AlertFlags));
    }
}
