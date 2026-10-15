using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content.Rules;
using Xunit;
using static Tribes.Core.Tests.RuleActionTablesTests;
using static Tribes.Core.Tests.RuleCheck;

namespace Tribes.Core.Tests;

/// <summary>
/// The exported building, weapon, armoury, animal, animation, target and unit tables hold exactly
/// the original's table values. Numbers in calls are the original's block numbers.
/// </summary>
public sealed class RuleObjectTablesTests
{
    private static readonly GameRules Rules = OriginalTables.ExportedRules;
    private static OriginalTables O => OriginalTables.Instance;

    private static IReadOnlyList<BuildingKind> Kinds => Rules.Buildings.Kinds;

    private static IEnumerable<BuildingOperation> Operations => Kinds.Take(20).Select(k => k.Operation!);

    [Fact]
    public void BuildingTable_HoldsEveryCellOfTheOriginal()
    {
        Assert.Equal(26, Kinds.Count);
        var rows = new List<IEnumerable<int>>
        {
            Kinds.Select(k => k.WorkerSlots[0]), Kinds.Select(k => k.WorkerSlots[1]), Kinds.Select(k => k.WorkerSlots[2]),
            Kinds.Select(k => k.Unknown3), Kinds.Select(k => k.Width), Kinds.Select(k => k.Height),
            Kinds.Select(k => k.ManagerSlot), Kinds.Select(k => k.MaxHitPoints[0]), Kinds.Select(k => k.MaxHitPoints[1]),
            Kinds.Select(k => k.MaxHitPoints[2]), Kinds.Select(k => k.BuildCost[0]), Kinds.Select(k => k.BuildCost[1]),
            Kinds.Select(k => k.BuildCost[2]), Kinds.Select(k => k.UpgradeCost[0]), Kinds.Select(k => k.UpgradeCost[1]),
            Kinds.Select(k => k.UpgradeCost[2]), Kinds.Select(k => k.SiteKind), Kinds.Select(k => k.Unknown17),
            Kinds.Select(k => k.CostTypeCount), Kinds.Select(k => k.WorkerSlotsWhileBusy), Kinds.Select(k => k.HitPointBarOffset),
            Kinds.Select(k => k.InsideHappinessBonus[0]), Kinds.Select(k => k.InsideHappinessBonus[1]),
            Kinds.Select(k => k.InsideHappinessBonus[2]),
        };
        Same(O.Block(138), rows.SelectMany(r => r), "block 138");
        Assert.All(Kinds.Skip(20), k => Assert.Null(k.Operation));
        Assert.All(Kinds.Skip(12), k => Assert.Null(k.Construction));
    }

    [Fact]
    public void BuildingEntrancesAndExits_MatchTheOriginal()
    {
        Same(O.Block(20), Operations.Select(o => o.EntranceCount), "block 20");
        Same(O.Block(21), Operations.SelectMany(o => o.Entrances.Select(e => e.Facing)), "block 21");
        Same(O.Block(22), Operations.SelectMany(o => o.Entrances.Select(e => e.Offset.Dx)), "block 22");
        Same(O.Block(23), Operations.SelectMany(o => o.Entrances.Select(e => e.Offset.Dy)), "block 23");
        Same(O.Block(24), Operations.Select(o => o.ExitOrigin.Dx), "block 24");
        Same(O.Block(25), Operations.Select(o => o.ExitOrigin.Dy), "block 25");
        Same(O.Block(111), Operations.Select(o => o.ExitFacing), "block 111");
    }

    [Fact]
    public void TargetActionTable_IsSplitWithoutLoss()
    {
        TargetTables t = Rules.Targets;
        var categories = new List<IEnumerable<int>>
        {
            new[] { t.TargetActions.EmptyCell }, new[] { t.TargetActions.OwnUnit }, Operations.Select(o => o.UseAction),
            t.GatherActionByNodeKind, t.TargetActions.TerrainResource, new[] { t.TargetActions.Animal },
            new[] { t.TargetActions.Other }, Operations.Select(o => o.UseActionWhenOccupied), Operations.Select(o => o.ActionWhileBusy),
        };
        Same(O.Block(16), categories.SelectMany(c => c), "block 16");
        Same(O.Block(17), Starts(categories.Select(c => c.Count())), "block 17");
        Same(O.Block(19), t.TargetActions.EnemyAttack, "block 19");
        Same(O.Block(121), t.DepositActionByCarriedItem, "block 121");
    }

    [Fact]
    public void BuildingTraining_MatchesTheOriginal()
    {
        var reached = new List<int>();
        foreach (BuildingKind kind in Kinds.Take(20))
        {
            BuildingOperation op = kind.Operation!;
            for (int level = 0; level < 3; level++)
            {
                int first = O.B(68, kind.Id) + O.B(69, (kind.Id * 4) + level);
                int end = O.B(68, kind.Id) + O.B(69, (kind.Id * 4) + level + 1);
                reached.AddRange(Enumerable.Range(first, Math.Max(0, end - first)));
                IReadOnlyList<TrainingEffect> effects = op.TrainingEffects[level];
                string at = $"kind {kind.Id} level {level + 1}";
                Same(O.Range(65, first, end), effects.Select(e => e.Stat), at + " stat");
                Same(O.Range(66, first, end), effects.Select(e => e.ChancePercent), at + " chance");
                Same(O.Range(67, first, end), effects.Select(e => e.Cap), at + " cap");
                Same(O.Range(63, first, end), effects.Select(e => e.Step), at + " step");
            }
            Same(O.B(70, kind.Id), (op.TrainsOnceAtEnd ? 2 : 0) + (op.TrainsAllOccupants ? 1 : 0), $"kind {kind.Id} training mode");
        }
        // Every entry is read by some level or is zero padding. The original's sanctuary and tower
        // level 3 ranges run one entry into the next kind's, so those two entries are read twice.
        Assert.Equal(new[] { 37, 50 }, reached.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key));
        foreach (int unreached in Enumerable.Range(0, O.Length(65)).Except(reached))
        {
            Assert.All(new[] { 63, 65, 66, 67 }, block => Same(0, O.B(block, unreached), $"block {block} entry {unreached}"));
        }
    }

    [Fact]
    public void BuildingSlots_MatchTheOriginal()
    {
        IEnumerable<BuildingSlot> slots = Operations.SelectMany(o => o.Slots);
        Same(O.Block(174), Starts(Operations.Select(o => o.Slots.Count)), "block 174");
        Same(O.Block(57), slots.Select(s => s.IdleAction), "block 57");
        Same(O.Block(58), slots.Select(s => s.WorkAction), "block 58");
        Same(O.Block(172).Select(v => v & 0xFF), slots.Select(s => s.EnterAnimation), "block 172");
        Same(O.Block(173).Select(v => v & 0xFF), slots.Select(s => s.ExitAnimation), "block 173");
        IEnumerable<BuildingSlot> busy = Operations.SelectMany(o => o.SlotsWhileBusy);
        Same(O.Block(177), Starts(Operations.Select(o => o.SlotsWhileBusy.Count)), "block 177");
        Same(O.Block(59), busy.Select(s => s.IdleAction), "block 59");
        Same(O.Block(60), busy.Select(s => s.WorkAction), "block 60");
        Same(O.Block(175).Select(v => v & 0xFF), busy.Select(s => s.EnterAnimation), "block 175");
        Same(O.Block(176).Select(v => v & 0xFF), busy.Select(s => s.ExitAnimation), "block 176");
        Same(O.Block(93), Operations.SelectMany(o => o.SlotPlacesWhileBusy), "block 93");
        Same(O.Block(94), Starts(Operations.Take(12).Select(o => o.SlotPlacesWhileBusy.Count)), "block 94");
        Same(O.Block(95), Operations.SelectMany(o => o.PlaceSlotsWhileBusy), "block 95");
        Same(O.Block(96), Starts(Operations.Take(12).Select(o => o.PlaceSlotsWhileBusy.Count)), "block 96");
    }

    [Fact]
    public void ConstructionPlans_MatchTheOriginal()
    {
        IEnumerable<ConstructionPlan> plans = Kinds.Take(12).Select(k => k.Construction!);
        Same(O.Block(80), plans.Select(p => p.DeliveryActions.Count), "block 80");
        Same(O.Block(81), plans.SelectMany(p => p.DeliveryActions), "block 81");
        Same(O.Block(82), Starts(plans.Select(p => p.DeliveryActions.Count)), "block 82");
        Same(O.Block(83), plans.SelectMany(p => p.ProgressHitPoints), "block 83");
        Same(O.Block(84), Starts(plans.Select(p => p.ProgressHitPoints.Count)), "block 84");
    }

    [Fact]
    public void Weapons_MatchTheOriginal()
    {
        WeaponTables w = Rules.Weapons;
        Assert.Equal(10, w.Weapons.Count);
        Same(O.Block(18), w.Weapons.Select(x => x.RangedActionOffset), "block 18");
        Same(O.Block(77), w.Weapons.Select(x => x.ProjectileKind), "block 77");
        Same(O.Block(102), w.Weapons.Select(x => x.DamageBonus), "block 102");
        Same(O.Block(161).Select(v => v & 0xFF), w.Weapons.SelectMany(x => x.AttackAnimations), "block 161");
        Same(O.Block(163), Starts(w.Weapons.Select(x => x.AttackAnimations.Count)), "block 163");
        IEnumerable<WeaponKind> menu = w.Weapons.Take(6);
        Same(O.Block(105), menu.Select(x => x.RefundAmount!.Value), "block 105");
        Same(O.Block(193), menu.Select(x => x.ProjectileSprite!.Value).Concat(menu.Select(x => x.ProjectileSpriteFacingStride!.Value)), "block 193");
        foreach (WeaponKind weapon in menu)
        {
            int column = 12 + weapon.Id;
            Same(new[] { 10, 11, 12 }.Select(r => O.B(138, (r * 26) + column)), weapon.Display!.Cost, $"weapon {weapon.Id} cost");
            Same(O.B(138, (13 * 26) + column), weapon.Display.IsThrown ? 1 : 0, $"weapon {weapon.Id} thrown");
            Same(O.B(138, (14 * 26) + column), weapon.Display.Attack, $"weapon {weapon.Id} attack");
        }
        Assert.All(w.Weapons.Skip(6), x => Assert.True(x.RefundAmount is null && x.Display is null && x.ProjectileSprite is null));
        Same(O.Block(159), w.Projectiles.Select(p => p.Speed), "block 159");
        Same(O.Block(158), w.Projectiles.Select(p => p.RangeTrainingAnimationOffset), "block 158");
        Same(Enumerable.Range(0, O.ShortLength(3)).Select(i => O.Short(3, i)), w.Projectiles.SelectMany(p => p.ArcDivisorByFacing), "short block 3");
        foreach (ProjectileKind p in w.Projectiles)
        {
            Same(O.Block(190 + p.Id), p.FlightFrames, $"block {190 + p.Id}");
        }
        Same(O.Block(124), w.MeleeDamageBySkill, "block 124");
    }

    [Fact]
    public void Armoury_MatchesTheOriginal()
    {
        ArmouryTables a = Rules.Armoury;
        Same(O.Block(86), a.Products.Select(p => p.CraftAction), "block 86");
        Same(O.Block(88), a.Products.Select(p => (int)p.StockCounter), "block 88");
        Same(O.Block(71), a.Recipes.SelectMany(r => r.Costs.Select(c => (int)c.Counter)), "block 71");
        Same(O.Block(72), a.Recipes.SelectMany(r => r.Costs.Select(c => c.Amount)), "block 72");
        Same(O.Block(73), Starts(a.Recipes.Select(r => r.Costs.Count)), "block 73");
        Same(O.Block(74), a.Recipes.Take(16).Select(r => r.CargoItem!.Value), "block 74");
        Same(O.Block(75), a.Recipes.Take(16).Select(r => r.CargoAmount!.Value), "block 75");
        Assert.All(a.Recipes.Skip(16), r => Assert.True(r.CargoItem is null && r.CargoAmount is null));
        Same(O.Block(76), a.StockCargo.Select(c => c.CarriedItem), "block 76");
        Same(Enumerable.Range(0, 11), a.StockCargo.Select(c => (int)c.Counter), "stock cargo counters");
        Same(O.Block(103), a.AutoEquipOrder.Select(c => c.Weapon), "block 103");
        Same(O.Block(104), a.AutoEquipOrder.Select(c => c.StockCost), "block 104");
        Same(O.Block(109), a.AiWeaponOrderPriority, "block 109");
        Same(O.Block(87), a.SpellActions, "block 87");
        Same(O.Block(106), a.Traps.Select(t => t.Damage), "block 106");
    }

    [Fact]
    public void AnimalsAndAnimations_MatchTheOriginal()
    {
        IReadOnlyList<AnimalKind> animals = Rules.Animals.Kinds;
        Same(O.Block(179), animals.Select(k => k.SpeedClass), "block 179");
        Same(O.Block(180), animals.Select(k => k.MaxHitPoints), "block 180");
        Same(O.Block(181), animals.Select(k => k.FleeThreshold), "block 181");
        Same(O.Block(182), animals.Select(k => k.AttackDamage), "block 182");
        Same(O.Block(183), animals.Select(k => k.CarcassAmount), "block 183");
        Same(O.Block(184), animals.Select(k => k.IdleAnimations[0]).Concat(animals.Select(k => k.IdleAnimations[1])), "block 184");

        IReadOnlyList<AnimationDefinition> animations = Rules.Animations.Animations;
        Assert.Equal(O.PdByteLength(4), animations.Count);
        foreach (AnimationDefinition a in animations)
        {
            string at = $"animation {a.Id}";
            Same(O.PdByte(4, a.Id), a.FrameCount, at + " frames");
            Same(O.PdByte(5, a.Id), a.TriggerFrame, at + " trigger frame");
            Same(O.PdByte(6, a.Id), a.TriggerParameter, at + " trigger parameter");
            Same((O.PdShort(3, a.Id) >> 14) & 3, a.DirectionGroup, at + " direction group");
        }
        Same(O.Block(171), animations.Select(a => a.LoopMode), "block 171");
        Same(O.Block(170), animations.Where(a => a.EnergyDecayPeriod.HasValue).Select(a => a.EnergyDecayPeriod!.Value), "block 170");
        Assert.All(animations.Skip(O.Length(170)), a => Assert.Null(a.EnergyDecayPeriod));
        Same(O.Block(99), Rules.Animations.SpriteDirectionGroups.SelectMany(r => r), "block 99");
    }

    [Fact]
    public void CellCategoriesAndThreats_MatchTheOriginal()
    {
        TargetTables t = Rules.Targets;
        IReadOnlyList<CellCategory> c = t.CellCategories;
        Same(O.Block(89), c.Select(x => x.CodeMin).Concat(c.Select(x => x.CodeMax)), "block 89");
        Same(O.Block(90), c.Select(x => x.IndexMultiplier).Concat(c.Select(x => x.IndexOffset)), "block 90");
        Same(O.Block(91), c.Select(x => (int)x.SubKindProperty), "block 91");
        Same(O.Block(92), c.Select(x => x.SideDivisor), "block 92");
        Same(O.Block(78), c.Select(x => Number(x.HitPointsProperty, 127)).Concat(c.Select(x => Number(x.UnknownProperty, 127)))
            .Concat(c.Select(x => Number(x.PositionProperty, 127))), "block 78");
        Same(O.Block(61), t.ThreatResponses.Select(x => x.Action), "block 61");
        Same(O.Block(62), t.ThreatResponses.Select(x => x.CellCodeSign), "block 62");
        Same(O.Block(115), t.ObjectGroups.Select(g => g.FirstIndex), "block 115");
        Same(O.Block(116), t.ObjectGroups.Select(g => g.LastIndex), "block 116");
        Same(O.Block(117), t.ObjectGroups.Select(g => (int)g.HitPointsProperty), "block 117");
        Same(O.Block(118), t.ObjectGroups.Select(g => (int)g.PositionProperty), "block 118");
        Same(O.Block(119), t.ObjectGroups.Select(g => g.ThreatPriority), "block 119");
        Same(O.Block(120), t.ThreatSearchOrder.SelectMany(r => r), "block 120");
        Same(O.Block(85), t.DepletionStageTenths, "block 85");
    }

    [Fact]
    public void UnitTables_MatchTheOriginal()
    {
        UnitTables u = Rules.Units;
        IReadOnlyList<ThoughtRule> t = u.Thoughts;
        Same(O.Block(56), t.Select(x => (int)x.Property).Concat(t.Select(x => x.Min)).Concat(t.Select(x => x.Max))
            .Concat(t.Select(x => x.ChancePercent)), "block 56");
        Same(O.Block(97), u.AgeSteps.Select(a => a.MaxHitPointsDelta), "block 97");
        Same(O.Block(98), u.AgeSteps.Select(a => a.MaxHitPointsCap), "block 98");
        Same(O.Block(189), u.Portraits.SelectMany(p => new[] { p.First, p.Count }), "block 189");
        Same(u.Portraits.Select((p, i) => i), u.Portraits.Select(p => (p.Gender * 5) + p.AgeClass), "portrait order");
        Same(O.Block(186).Skip(1), u.MeetingOverrides.Select(m => (int)m.Property), "block 186");
        Same(O.Block(187).Skip(1), u.MeetingOverrides.Select(m => m.Value), "block 187");
        Same(O.Block(188).Select(v => v & 0xFF), u.MeetingSeatAnimations, "block 188");
    }
}
