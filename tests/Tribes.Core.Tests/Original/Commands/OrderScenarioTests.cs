using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Tests.Systems;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Commands;

/// <summary>
/// Orders checked by driving the original's handler directly, on states sampled from key-monkey
/// games: the screen state is set up as the original's menus would leave it (the action bar is the
/// one the original builds for the building), the handler runs, and the same order is applied by
/// the new rules to the imported state. Covers the orders the monkey rarely or never gives.
/// </summary>
public sealed class OrderScenarioTests
{
    private const int Player = 0, Units = 50, Buildings = 30;
    private const int UnitHitPoints = 19, UnitAge = 14, UnitX = 0, UnitY = 1, Adult = 12;
    private const int BuildingX = 0, BuildingY = 1, BuildingHitPoints = 2, BuildingKind = 6, BuildingLevel = 12;
    private const int ArmouryKind = 0, StoreKind = 6, SanctuaryKind = 8, FirstTrap = 20;
    private const int BuildingBar = 2, PickedGroup = 4, DoublePress = 2, HintsOption = 3, ArmyPoint = 100;
    private const int UpgradeEntry = 16, DemolishEntry = 17, RepairEntry = 26, StopUpgradeEntry = 27, StopRepairEntry = 28;
    private const int CancelRallyEntry = 14, CancelAllOutEntry = 15, DeliverEntry = 29, FoodHint = 14, RestHint = 15;
    private const int HintChoiceTable = 153, HintChoiceOffset = 145 + 20;
    private const int MainBarPanel = 5, StoreWeapons = 6, LastTrapProduct = 8, UnitCarried = 36, StockOfEach = 20, BuildingOperator = 7;

    private readonly ITestOutputHelper output;

    public OrderScenarioTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void BuildingWorkAndDemolition_AsTheBuildingsBarOffersThem()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach (int id in SampleInputs.Spread(OwnBuildings(sample.Restore()), 5))
            {
                foreach (BuildingState state in Enum.GetValues<BuildingState>())
                {
                    GameCanvas probe = sample.Restore();
                    SetUp(probe, id, state);
                    int[] work = OpenBar(probe, id);
                    for (int i = 0; i < work.Length; i++)
                    {
                        BuildingWork? asked = Work(work[i]);
                        int index = i;
                        if (asked != null)
                        {
                            run.CheckPrepared(sample, $"OrderBuildingWork {asked}", g =>
                            {
                                SetUp(g, id, state);
                                SelectEntry(g, id, index);
                            }, g => g.af_(), s => Apply(s, new OrderBuildingWork(Player, id, asked.Value)));
                        }
                    }
                }
                int[] entries = OpenBar(sample.Restore(), id);
                if (entries.Contains(DemolishEntry))
                {
                    run.CheckPrepared(sample, "DemolishBuilding", g => g.i_q = id, g => g.l_I(-2),
                        s => Apply(s, new DemolishBuilding(Player, id)));
                }
            }
        }
        Report(run);
        run.AssertAgrees(100);
    }

    [Fact]
    public void Eviction_OfTheOccupantsTheBarLists()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            int[] adults = OwnAdults(sample.Restore()).ToArray();
            foreach (int id in OwnBuildings(sample.Restore()))
            {
                // As sampled, and with an adult made the operator (the bar then lists it unless the building is a hut).
                int operatorUnit = adults.Length == 0 ? 0 : adults[id % adults.Length];
                foreach (int made in operatorUnit == 0 ? new[] { 0 } : new[] { 0, operatorUnit })
                {
                    GameCanvas probe = sample.Restore();
                    MakeOperator(probe, id, made);
                    int[] entries = OpenBar(probe, id);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        int index = i, unit = -entries[i];
                        if (unit > 0)
                        {
                            run.CheckPrepared(sample, "EvictUnit", g =>
                            {
                                MakeOperator(g, id, made);
                                SelectEntry(g, id, index);
                                (g.b_l, g.z_V, g.z_F) = ((sbyte)unit, true, false);
                            }, g => g.af_(), s => Apply(s, new EvictUnit(Player, id, unit)));
                        }
                    }
                }
            }
        }
        Report(run);
        run.AssertAgrees(100);
    }

    [Fact]
    public void ArmouryCountsAndTraps_AsTheArmourysBarOffersThem()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in SampleInputs.Spread(LegacySamples.All, 24))
        {
            int level = 1 + (sample.Frame / 1000 % 3);
            GameCanvas probe = sample.Restore();
            int armoury = EnsureBuilding(probe, ArmouryKind, level);
            if (armoury == 0)
            {
                continue;
            }
            int[] entries = OpenBar(probe, armoury);
            int[] counts = probe.bArr_s.Take(entries.Length).Select(v => (int)v).ToArray();
            int weapons = probe.b_av < 0 ? entries.Length : probe.b_av;
            for (int i = 0; i < entries.Length; i++)
            {
                int index = i, product = entries[i];
                if (product < 0 || product > LastTrapProduct)
                {
                    continue;
                }
                if (i < weapons)
                {
                    foreach (bool up in new[] { true, false })
                    {
                        run.CheckPrepared(sample, "SetArmouryOrder", g => PrepareCount(g, level, armoury, index, up), g => g.ae_(),
                            s => Apply(s, new SetArmouryOrder(Player, armoury, product, counts[index] + (up ? 1 : -1))));
                    }
                }
                else
                {
                    foreach (GridPoint cell in CellsAround(probe, armoury))
                    {
                        run.CheckPrepared(sample, "PlaceTrap", g =>
                        {
                            EnsureBuilding(g, ArmouryKind, level);
                            OpenBar(g, armoury);
                            Place(g, FirstTrap + product - OrderMenus.FirstTrapProduct, cell);
                        }, g => g.X_(), s => Apply(s, new PlaceTrap(Player, armoury, FirstTrap + product - OrderMenus.FirstTrapProduct, cell)));
                    }
                }
            }
        }
        Report(run);
        run.AssertAgrees(100);
    }

    [Fact]
    public void StoreErrands_ForSelectedUnitsGroupsAndPickedUnits()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in SampleInputs.Spread(LegacySamples.All, 30))
        {
            GameCanvas probe = sample.Restore();
            int store = EnsureBuilding(probe, StoreKind, 1);
            int[] adults = OwnAdults(probe).ToArray();
            if (store == 0 || adults.Length == 0)
            {
                continue;
            }
            var cell = new GridPoint(B(probe, BuildingX, store) + 1, B(probe, BuildingY, store) + 1);
            var origin = new GridPoint(B(probe, BuildingX, store), B(probe, BuildingY, store));
            int[] group = SampleInputs.Pick(adults, sample.Frame / 1000, 4);
            int single = group[0];
            int[] entries = OpenBar(StoreFor(sample.Restore(), store, single, group: null), store);
            for (int i = 0; i < entries.Length; i++)
            {
                int index = i, item = entries[i];
                if (!IsStoreItem(item))
                {
                    continue;
                }
                GameOrder Order(int[] units, GridPoint at) => item == DeliverEntry
                    ? new DeliverToStore(Player, new UnitSet(units), store, at)
                    : new FetchWeapon(Player, new UnitSet(units), store, item, at);
                run.CheckPrepared(sample, item == DeliverEntry ? "DeliverToStore" : "FetchWeapon", g =>
                {
                    OpenBar(StoreFor(g, store, single, group: null), store);
                    PointAt(g, cell);
                    g.b_as = (sbyte)index;
                }, g => g.ag_(), s => Apply(s, Order(new[] { single }, cell)));
                if (item != DeliverEntry)
                {
                    run.CheckPrepared(sample, "FetchWeapon picked", g =>
                    {
                        StoreFor(g, store, 0, group: null);
                        g.i_q = store;
                        (g.i_s, g.i_r, g.z_r, g.b_w) = (-item - 1, single, true, 3);
                    }, g => g.X_(), s => Apply(s, Order(new[] { single }, origin)));
                }
            }
            int[] groupEntries = OpenBar(StoreFor(sample.Restore(), store, 0, group), store);
            for (int i = 0; i < groupEntries.Length; i++)
            {
                int index = i, item = groupEntries[i];
                if (!IsStoreItem(item))
                {
                    continue;
                }
                GameOrder order = item == DeliverEntry
                    ? new DeliverToStore(Player, new UnitSet(group), store, cell)
                    : new FetchWeapon(Player, new UnitSet(group), store, item, cell);
                run.CheckPrepared(sample, order.GetType().Name + " group", g =>
                {
                    OpenBar(StoreFor(g, store, 0, group), store);
                    PointAt(g, cell);
                    g.b_as = (sbyte)index;
                }, g => g.ag_(), s => Apply(s, order));
            }
        }
        Report(run);
        run.AssertAgrees(60);
    }

    [Fact]
    public void Spells_OnUnitsAndPlaces()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in SampleInputs.Spread(LegacySamples.All, 20))
        {
            int level = 1 + (sample.Frame / 1000 % 2);
            GameCanvas probe = sample.Restore();
            int sanctuary = EnsureBuilding(probe, SanctuaryKind, level);
            int[] adults = OwnAdults(probe).ToArray();
            if (sanctuary == 0 || adults.Length == 0)
            {
                continue;
            }
            int spells = OrderMenus.SanctuarySpells(WorldOf(probe), level);
            GridPoint place = CellsAround(probe, sanctuary).First();
            for (int spell = 0; spell < spells; spell++)
            {
                int s0 = spell;
                int target = adults[(sample.Frame / 1000 + spell) % adults.Length];
                GameOrder order = spell == OrderMenus.PlaceSpell
                    ? new CastSpellAt(Player, sanctuary, spell, place)
                    : new CastSpell(Player, sanctuary, spell, target);
                run.CheckPrepared(sample, order.GetType().Name, g =>
                {
                    EnsureBuilding(g, SanctuaryKind, level);
                    PointAt(g, place);
                    (g.i_q, g.i_s, g.i_r, g.z_r, g.b_w) = (sanctuary, s0, target, true, 3);
                }, g => g.X_(), s => Apply(s, order));
            }
        }
        Report(run);
        run.AssertAgrees(40);
    }

    [Fact]
    public void HintAnswers_AndTheRoster()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            GameCanvas probe = sample.Restore();
            int[] units = SampleInputs.Spread(OwnUnits(probe), 4).ToArray();
            foreach (int unit in units)
            {
                foreach (int hint in new[] { FoodHint, RestHint })
                {
                    run.CheckPrepared(sample, "TendNeed", g =>
                    {
                        g.bArr_a[g.sArr_a[HintChoiceTable] + HintChoiceOffset] = 0;
                        (g.b_ap, g.b_am) = ((sbyte)hint, (sbyte)unit);
                    }, g => g.ap_(), s => Apply(s, new TendNeed(Player, unit, hint == FoodHint ? UnitNeed.Food : UnitNeed.Rest)));
                }
                bool adult = U(probe, UnitAge, unit) >= Adult;
                foreach (bool choosingChief in adult ? new[] { false, true } : new[] { false })
                {
                    run.CheckPrepared(sample, choosingChief ? "AppointChief" : "CallOutUnit", g => PickInRoster(g, unit, choosingChief),
                        g => g.af_(), s => Apply(s, choosingChief ? new AppointChief(Player, unit) : new CallOutUnit(Player, unit)));
                }
            }
        }
        Report(run);
        run.AssertAgrees(200);
    }

    [Fact]
    public void UnitGroups_AndTribeOrders()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            GameCanvas probe = sample.Restore();
            int[] units = OwnUnits(probe).ToArray();
            if (units.Length == 0)
            {
                continue;
            }
            int[] group = SampleInputs.Pick(units, sample.Frame / 1000, 6);
            foreach (GridPoint cell in SampleCells(probe, sample.Frame))
            {
                int code = probe.bArr2_c[cell.Y][cell.X];
                foreach (bool hurry in new[] { false, true })
                {
                    run.CheckPrepared(sample, "MoveUnits", g =>
                    {
                        Group(g, group);
                        PointAt(g, cell);
                        g.i_n = hurry ? DoublePress : 0;
                    }, g => g.e_B((sbyte)code), s => Apply(s, new MoveUnits(Player, new UnitSet(group), cell, code, hurry)));
                }
                foreach (int kind in OrderMenus.BuildableKinds(WorldOf(probe)))
                {
                    int site = LegacySamples.Content.Rules.Buildings.Kinds[kind].SiteKind;
                    run.CheckPrepared(sample, "PlaceBuilding", g =>
                    {
                        Place(g, site, cell);
                        (g.b_as, g.bArr_r[0]) = (0, (sbyte)kind);
                    }, g => g.X_(), s => Apply(s, new PlaceBuilding(Player, kind, cell)));
                }
                ArmyOrders(run, sample, probe, cell);
            }
            bool enabled = probe.i_l == 1;
            run.CheckPrepared(sample, "SetHints", g => g.i_Z = HintsOption, g => g.as_(), s => Apply(s, new SetHints(Player, !enabled)));
        }
        Report(run);
        run.AssertAgrees(300);
    }

    private static void ArmyOrders(ParityRun run, LegacySample sample, GameCanvas probe, GridPoint cell)
    {
        if (!probe.z_N && !probe.z_L)
        {
            run.CheckPrepared(sample, "CallArmy", g => ArmyPointAt(g, cell, allOut: false), g => g.X_(),
                s => Apply(s, new CallArmy(Player, ArmyCall.Rally, cell)));
        }
        if (!probe.z_N)
        {
            run.CheckPrepared(sample, "CallArmy", g => ArmyPointAt(g, cell, allOut: true), g => g.X_(),
                s => Apply(s, new CallArmy(Player, ArmyCall.AllOut, cell)));
        }
        run.CheckPrepared(sample, "CancelArmyCall", g =>
        {
            MainBar(g);
            (g.z_N, g.z_L) = (false, true);
            (g.b_as, g.bArr_r[0]) = (0, CancelRallyEntry);
        }, g => g.af_(), s => Apply(s, new CancelArmyCall(Player, ArmyCall.Rally)));
        run.CheckPrepared(sample, "CancelArmyCall", g =>
        {
            MainBar(g);
            g.z_N = true;
            (g.b_as, g.bArr_r[0]) = (0, CancelAllOutEntry);
        }, g => g.af_(), s => Apply(s, new CancelArmyCall(Player, ArmyCall.AllOut)));
    }

    private static void Apply(RuleSystems systems, GameOrder order)
    {
        systems.ApplyOrder(order);
    }

    private void Report(ParityRun run) => output.WriteLine(run.ToString());

    /// <summary>How a building is set up before its bar is opened, so the bar offers every kind of work.</summary>
    private enum BuildingState
    {
        AsSampled,
        DamagedAndUnlocked,
        Upgrading,
        Repairing,
    }

    private static void SetUp(GameCanvas g, int building, BuildingState state)
    {
        if (state == BuildingState.AsSampled)
        {
            return;
        }
        int kind = B(g, BuildingKind, building);
        int hitPoints = Parity.EntityFields.BuildingBase + (BuildingHitPoints * Parity.EntityFields.BuildingStride) + building;
        g.bArr_e[hitPoints] = (sbyte)Math.Max(1, g.bArr_e[hitPoints] - 5);
        if (kind < g.bArr_f.Length)
        {
            g.bArr_f[kind] = 3;
        }
        if (state == BuildingState.Upgrading && B(g, BuildingLevel, building) < 3)
        {
            g.b_IZ(building, true);
        }
        else if (state == BuildingState.Repairing)
        {
            g.b_IZ(building, false);
        }
    }

    private static BuildingWork? Work(int entry) => entry switch
    {
        UpgradeEntry => BuildingWork.Upgrade,
        RepairEntry => BuildingWork.Repair,
        StopUpgradeEntry => BuildingWork.StopUpgrade,
        StopRepairEntry => BuildingWork.StopRepair,
        _ => null,
    };

    /// <summary>The action bar the original builds for <paramref name="building"/>, as it would when the player selects it.</summary>
    private static int[] OpenBar(GameCanvas g, int building)
    {
        (g.i_q, g.b_w, g.z_P) = (building, 2, false);
        Array.Clear(g.zArr_a);
        g.zArr_a[BuildingBar] = true;
        g.Z_();
        return g.bArr_r.Take(g.b_ar).Select(v => (int)v).ToArray();
    }

    /// <summary>The main action bar (build menu, roster, army calls) as the left soft key opens it.</summary>
    private static void MainBar(GameCanvas g)
    {
        Array.Clear(g.zArr_a);
        g.zArr_a[MainBarPanel] = true;
        (g.z_F, g.b_w) = (false, 2);
    }

    private static bool IsStoreItem(int entry) => entry is >= 0 and < StoreWeapons or DeliverEntry;

    private static void MakeOperator(GameCanvas g, int building, int unit)
    {
        if (unit > 0)
        {
            g.bArr_e[Parity.EntityFields.BuildingBase + (BuildingOperator * Parity.EntityFields.BuildingStride) + building] = (sbyte)unit;
        }
    }

    private static void SelectEntry(GameCanvas g, int building, int index)
    {
        OpenBar(g, building);
        g.b_as = (sbyte)index;
    }

    private static void PrepareCount(GameCanvas g, int level, int armoury, int index, bool up)
    {
        EnsureBuilding(g, ArmouryKind, level);
        SelectEntry(g, armoury, index);
        (g.z_S, g.z_e, g.z_f, g.z_g, g.z_h, g.i_n, g.b_j) = (true, up, !up, false, false, 0, 0);
        g.zArr_a[6] = false;
    }

    private static GameCanvas StoreFor(GameCanvas g, int store, int unit, int[]? group)
    {
        EnsureBuilding(g, StoreKind, 1);
        foreach (int counter in new[] { 3, 5, 7, 8, 9, 10 })
        {
            g.sArr2_a[Player][counter] = StockOfEach;
        }
        if (unit > 0)
        {
            g.bArr_e[Parity.EntityFields.UnitBase + (UnitCarried * Parity.EntityFields.UnitStride) + unit] = 2;
        }
        Group(g, group ?? Array.Empty<int>());
        if (group == null)
        {
            g.b_F = -1;
        }
        g.b_k = (sbyte)unit;
        return g;
    }

    private static void Group(GameCanvas g, int[] units)
    {
        Array.Clear(g.bArr2_a[PickedGroup]);
        for (int i = 0; i < units.Length; i++)
        {
            g.bArr2_a[PickedGroup][i] = (sbyte)units[i];
        }
        (g.b_F, g.b_k) = (PickedGroup, 0);
    }

    private static void PickInRoster(GameCanvas g, int unit, bool choosingChief)
    {
        (g.z_F, g.z_K, g.b_k, g.b_l, g.b_as) = (true, choosingChief, (sbyte)unit, (sbyte)unit, 0);
        g.bArr_r[0] = (sbyte)-unit;
    }

    private static void PointAt(GameCanvas g, GridPoint cell) =>
        (g.b_I, g.b_K, g.b_y, g.b_A) = (0, 0, (sbyte)cell.X, (sbyte)cell.Y);

    /// <summary>Placement mode for <paramref name="kind"/> with the footprint's top-left cell at <paramref name="cell"/>.</summary>
    private static void Place(GameCanvas g, int kind, GridPoint cell)
    {
        (g.b_I, g.b_K, g.i_t, g.i_u) = (0, 0, cell.X, cell.Y);
        (g.i_s, g.z_r, g.b_w) = (kind, true, 3);
    }

    private static void ArmyPointAt(GameCanvas g, GridPoint cell, bool allOut)
    {
        PointAt(g, cell);
        (g.i_s, g.z_r, g.b_w, g.z_O, g.z_M) = (ArmyPoint, true, 3, allOut, !allOut);
    }

    /// <summary>
    /// A standing building of <paramref name="kind"/> of the player: the first one there is, or one
    /// placed at once where the footprint is free (with <paramref name="level"/>); 0 when none fits.
    /// </summary>
    private static int EnsureBuilding(GameCanvas g, int kind, int level)
    {
        int found = OwnBuildings(g).FirstOrDefault(id => B(g, BuildingKind, id) == kind);
        if (found != 0)
        {
            g.bArr_e[Parity.EntityFields.BuildingBase + (BuildingLevel * Parity.EntityFields.BuildingStride) + found] = (sbyte)level;
            return found;
        }
        for (int y = 2; y < g.s_d - 6; y += 3)
        {
            for (int x = 2; x < g.s_c - 6; x += 3)
            {
                if (g.a_IIIZ(kind, x, y, false))
                {
                    int slot = g.a_BIIIIZI(0, kind, x, y, level, true, -1);
                    return slot > 0 ? slot : 0;
                }
            }
        }
        return 0;
    }

    private static IEnumerable<GridPoint> CellsAround(GameCanvas g, int building)
    {
        int x = B(g, BuildingX, building), y = B(g, BuildingY, building);
        var offsets = new[] { (-2, 0), (0, -2), (4, 1), (1, 4), (-1, -1) };
        return offsets.Select(o => new GridPoint(Math.Clamp(x + o.Item1, 0, g.s_c - 1), Math.Clamp(y + o.Item2, 0, g.s_d - 1)))
            .Distinct();
    }

    /// <summary>A few cells of the playable map: around the tribe's units and some spread over the map.</summary>
    private static IEnumerable<GridPoint> SampleCells(GameCanvas g, int seed)
    {
        var random = new Random(seed);
        int[] units = OwnUnits(g).ToArray();
        for (int i = 0; i < 3; i++)
        {
            int unit = units[random.Next(units.Length)];
            yield return new GridPoint(Math.Clamp(U(g, UnitX, unit) + random.Next(-3, 4), 0, g.s_c - 1),
                Math.Clamp(U(g, UnitY, unit) + random.Next(-3, 4), 0, g.s_d - 1));
        }
        yield return new GridPoint(random.Next(g.s_c), random.Next(g.s_d));
    }

    private static Tribes.Core.World.GameWorld WorldOf(GameCanvas g) => Parity.LegacyRules.Import(g, LegacySamples.Content);

    private static IEnumerable<int> OwnBuildings(GameCanvas g) =>
        Enumerable.Range(1, Buildings).Where(id => B(g, BuildingHitPoints, id) > 0);

    private static IEnumerable<int> OwnUnits(GameCanvas g) => Enumerable.Range(1, Units).Where(id => U(g, UnitHitPoints, id) > 0);

    private static IEnumerable<int> OwnAdults(GameCanvas g) => OwnUnits(g).Where(id => U(g, UnitAge, id) >= Adult);

    private static int U(GameCanvas g, int field, int id) => LegacySamples.Field(g, field, id);

    private static int B(GameCanvas g, int field, int id) => LegacySamples.BuildingField(g, field, id);
}
