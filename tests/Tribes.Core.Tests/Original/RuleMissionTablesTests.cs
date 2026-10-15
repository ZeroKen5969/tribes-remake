using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content.Rules;
using Xunit;
using static Tribes.Core.Tests.RuleActionTablesTests;
using static Tribes.Core.Tests.RuleCheck;

namespace Tribes.Core.Tests;

/// <summary>
/// The exported mission, story and interface tables hold exactly the original's table values,
/// including the blocks the original reads with a computed index (by mission or difficulty).
/// Numbers in calls are the original's block numbers.
/// </summary>
public sealed class RuleMissionTablesTests
{
    private static readonly GameRules Rules = OriginalTables.ExportedRules;
    private static OriginalTables O => OriginalTables.Instance;

    private static IReadOnlyList<MissionRules> Missions => Rules.Missions.Missions;

    [Fact]
    public void MissionScalars_MatchTheOriginal()
    {
        Assert.Equal(12, Missions.Count);
        Same(O.Block(279), Missions.Select(m => m.EndConditions), "block 279");
        Same(Enumerable.Range(0, 12).Select(i => O.Short(4, i)), Missions.Select(m => m.NeedsStartFrame), "short block 4");
        Same(O.Block(101), Missions.Where(m => m.PassiveZone.HasValue).SelectMany(m => Encoded(m.PassiveZone!.Value)), "block 101");
        Assert.All(Missions.Skip(7), m => Assert.Null(m.PassiveZone));
        Same(O.Block(303), Missions.Where(m => m.OpeningDialogue.HasValue).Select(m => m.OpeningDialogue!.Value), "block 303");
        Assert.All(Missions.Skip(O.Length(303)), m => Assert.Null(m.OpeningDialogue));
        Same(O.Block(249), Rules.Missions.MenuMissions, "block 249");
        Same(O.Block(112), Rules.Missions.ScoreWeights.Select(w => w.WeightTenths), "block 112");
        Same(Enumerable.Range(19, 5), Rules.Missions.ScoreWeights.Select(w => (int)w.Counter), "score statistics");
        foreach (MissionRules m in Missions)
        {
            int unlockRow = m.Id < 4 ? 0 : m.Id == 4 ? 1 : 2;
            int defaultRow = m.Id < 2 ? 0 : m.Id == 2 ? 1 : 2;
            Same(O.Range(113, unlockRow * 12, (unlockRow + 1) * 12), m.StartUnlockLevels, $"mission {m.Id} unlock levels");
            Same(O.Range(113, defaultRow * 12, (defaultRow + 1) * 12), m.DefaultBuildingLevels, $"mission {m.Id} default levels");
        }
    }

    [Fact]
    public void MissionAnimals_MatchTheOriginal()
    {
        Same(O.Block(201), Starts(Missions.Select(m => m.AnimalSpawnPoints.Count)), "block 201");
        IEnumerable<AnimalSpawnPoint> points = Missions.SelectMany(m => m.AnimalSpawnPoints);
        Same(O.Block(199), points.Select(p => p.Cell.X), "block 199");
        Same(O.Block(200), points.Select(p => p.Cell.Y), "block 200");
        Same(O.Block(185), points.SelectMany(p => p.KindCumulativePercent), "block 185");
        Same(O.Block(202), Missions.Select(m => m.ActiveAnimalSpawnPoints), "block 202");
        Same(O.Block(203), Missions.Select(m => m.AnimalWanderRadius), "block 203");
        Same(O.Block(204), Missions.Select(m => m.AnimalSpawnInterval), "block 204");
        Same(O.Block(205), Missions.Select(m => m.AnimalsPerSpawnPoint), "block 205");
        Same(O.Block(302), Starts(Missions.Take(5).Select(m => m.TechUnlocks.Count)), "block 302");
        IEnumerable<TechUnlock> unlocks = Missions.SelectMany(m => m.TechUnlocks);
        Same(O.Block(299), unlocks.Select(u => u.TimeUnits), "block 299");
        Same(O.Block(300), unlocks.Select(u => u.BuildingKind), "block 300");
        Same(O.Block(301), unlocks.Select(u => u.Dialogue), "block 301");
    }

    [Fact]
    public void Difficulties_MatchTheOriginal()
    {
        for (int d = 0; d < 2; d++)
        {
            IEnumerable<MissionDifficulty> rows = Missions.Select(m => m.Difficulties[d]);
            Same(O.Block(206 + d), rows.Select(r => r.AttackWaveBaseSize), $"block {206 + d}");
            Same(O.Block(208 + d), rows.Select(r => r.AttackWaveMaxSize), $"block {208 + d}");
            Same(O.Block(210 + d), rows.Select(r => r.AttackWaveGrowthDivisor), $"block {210 + d}");
            Same(O.Block(212 + d), rows.Select(r => r.AttackWaveUnitSkill), $"block {212 + d}");
            Same(O.Block(273 + d), rows.Select(r => r.AiBirthInterval), $"block {273 + d}");
            Same(O.Block(275 + d), rows.Select(r => r.AiPopulationCap), $"block {275 + d}");
        }
    }

    [Fact]
    public void ComputerPlayer_MatchesTheOriginal()
    {
        IEnumerable<MissionAi> ai = Missions.Select(m => m.Ai);
        Same(O.Block(214), ai.Select(a => a.AttackPeriodBase), "block 214");
        Same(O.Block(215), ai.Select(a => a.AttackPeriodHardReduction), "block 215");
        Same(O.Block(216), ai.Select(a => a.RampUpUnits), "block 216");
        Same(O.Block(217), ai.Select(a => a.ActivateAtStoryStep), "block 217");
        Same(O.Block(220), ai.Select(a => a.Posts.Count), "block 220");
        Same(O.Block(233), ai.Select(a => a.Home.X), "block 233");
        Same(O.Block(234), ai.Select(a => a.Home.Y), "block 234");
        Same(O.Block(241), ai.Select(a => a.BuildPlanCheckInterval), "block 241");
        Same(O.Block(242), ai.Select(a => a.BuilderPostCount), "block 242");
        Same(O.Block(264), ai.Select(a => a.ArmouryOrderInterval), "block 264");
        Same(O.Block(268), ai.Select(a => a.TrapOrderChance), "block 268");
        Same(O.Block(269), ai.Select(a => a.SpellInterval), "block 269");
        Same(O.Block(270), ai.Select(a => a.SpellsPerSanctuary), "block 270");
        Same(O.Block(277), ai.Select(a => a.StoneAxeUnlockTime), "block 277");
        Same(O.Block(278), ai.Select(a => a.MaceAndTomahawkUnlockTime), "block 278");
        Same(O.Block(218), ai.SelectMany(a => Encoded(a.AlarmRect)), "block 218");
        Same(O.Block(219), ai.SelectMany(a => Encoded(a.TerritoryRect)), "block 219");
        Same(O.Block(265), ai.SelectMany(a => new[] { a.ArmouryOrder.PrimaryWeaponQuantity, a.ArmouryOrder.SecondaryWeaponQuantity }
            .Concat(a.ArmouryOrder.TrapQuantities)), "block 265");
        Same(O.Block(266), ai.SelectMany(a => a.TrapAreas.SelectMany(Encoded)), "block 266");
        Same(O.Block(267), Starts(ai.Select(a => a.TrapAreas.Count)), "block 267");
        Same(O.Block(271), ai.SelectMany(a => a.SpellTargetAreas.SelectMany(Encoded)), "block 271");
        Same(O.Block(272), Starts(ai.Select(a => a.SpellTargetAreas.Count)), "block 272");
        foreach (MissionRules m in Missions)
        {
            IReadOnlyList<AiPost> posts = m.Ai.Posts;
            Same(O.Block(221 + m.Id), posts.Select(p => p.Action).Concat(posts.Select(p => p.Cell.X))
                .Concat(posts.Select(p => p.Cell.Y)).Concat(posts.Select(p => p.Weapon)), $"block {221 + m.Id}");
        }
    }

    [Fact]
    public void AttackRoutesAndBuildPlans_MatchTheOriginal()
    {
        IEnumerable<MissionAi> ai = Missions.Select(m => m.Ai);
        Same(O.Block(235), ai.Select(a => a.AttackRoutes.Count), "block 235");
        Same(O.Block(238), Starts(ai.Select(a => a.AttackRoutes.Count)), "block 238");
        Same(O.Block(236), ai.SelectMany(a => a.AttackRoutes.Select(r => r.Count)), "block 236");
        Same(O.Block(237), ai.SelectMany(a => Starts(a.AttackRoutes.Select(r => r.Count * 2)).Take(a.AttackRoutes.Count)), "block 237");
        Same(O.Block(240).Select(v => v & 0xFF), Starts(ai.Select(a => a.AttackRoutes.Sum(r => r.Count * 2))), "block 240");
        Same(O.Block(239), ai.SelectMany(a => a.AttackRoutes.SelectMany(r => r.SelectMany(p => new[] { p.X, p.Y }))), "block 239");
        IEnumerable<AiBuildPlanEntry> plan = ai.SelectMany(a => a.BuildPlan);
        Same(O.Block(247).Select(v => v & 0xFF), Starts(ai.Select(a => a.BuildPlan.Count)), "block 247");
        Same(O.Block(243), plan.Select(e => e.BuildingKind), "block 243");
        Same(O.Block(244), plan.Select(e => e.TargetLevel), "block 244");
        Same(O.Block(245), plan.Select(e => e.Cell.X), "block 245");
        Same(O.Block(246), plan.Select(e => e.Cell.Y), "block 246");
    }

    [Fact]
    public void MissionSetup_MatchesTheOriginal()
    {
        Same(O.Block(262), Missions.Select(m => m.InitialObjects.Count), "block 262");
        foreach (MissionRules m in Missions)
        {
            IReadOnlyList<InitialObject> o = m.InitialObjects;
            Same(O.Block(250 + m.Id), o.Select(Code).Concat(o.Select(Type)).Concat(o.Select(x => x.Cell.X))
                .Concat(o.Select(x => x.Cell.Y)).Concat(o.Select(Extra)), $"block {250 + m.Id}");
            Assert.Equal(2, m.StartStock.Count);
            for (int tribe = 0; tribe < 2; tribe++)
            {
                IReadOnlyList<CounterAmount> stock = m.StartStock[tribe];
                Same(Enumerable.Range(0, 11), stock.Select(s => (int)s.Counter), $"mission {m.Id} stock counters");
                Same(O.Range(263, ((m.Id * 2) + tribe) * 11, (((m.Id * 2) + tribe) + 1) * 11).Select(v => v * (tribe == 1 ? 20 : 1)),
                    stock.Select(s => s.Amount), $"mission {m.Id} tribe {tribe} stock");
            }
        }
        Assert.Equal(O.Length(263), Missions.Count * 2 * 11);
    }

    [Fact]
    public void StorySteps_MatchTheOriginal()
    {
        Same(O.Block(291), Missions.Select(m => m.StorySteps.Count), "block 291");
        Same(O.Block(288), Starts(Missions.Take(7).Select(m => m.StorySteps.Count)), "block 288");
        IEnumerable<StoryStep> steps = Missions.SelectMany(m => m.StorySteps);
        Same(O.Block(286), steps.Select(s => s.Dialogue), "block 286");
        Same(O.Block(287), steps.Select(s => s.Trigger), "block 287");
        Same(O.Block(289), steps.SelectMany(s => Encoded(s.Area)), "block 289");
        Same(O.Block(290), Starts(Missions.Take(7).Select(m => m.StorySteps.Count * 4)), "block 290");
    }

    [Fact]
    public void StoryTables_MatchTheOriginal()
    {
        StoryTables s = Rules.Story;
        IReadOnlyList<StoryScene> scenes = s.Scenes;
        Same(O.Block(155), scenes.Select(x => x.Mode).Concat(scenes.Select(x => x.PoseOrPicture))
            .Concat(scenes.Select(x => x.Transition)).Concat(scenes.Select(x => x.DurationFrames)), "block 155");
        var poses = new List<int>();
        for (int actor = 0; actor < 7; actor++)
        {
            poses.AddRange(s.Poses.SelectMany(p => new[] { (sbyte)p.Actors[actor].Animation, p.Actors[actor].FacingCode, p.Actors[actor].SubState }));
        }
        Same(O.Block(196), poses, "block 196");
        Same(O.Block(156), s.CastLists.SelectMany(c => c), "block 156");
        Same(O.Block(157), Starts(s.CastLists.Select(c => c.Count)), "block 157");
        Same(O.Block(154), s.ChapterIllustrations, "block 154");
        Same(O.Block(310).Take(71).Select(v => v & 0xFF), Starts(s.Dialogues.Select(d => d.PageCount)), "block 310 pages");
        Same(O.Block(310).Skip(71), s.Dialogues.Select(d => d.Cast).Append(s.UnusedCast), "block 310 casts");
        Same(s.Dialogues.Select(d => d.FirstPage), Starts(s.Dialogues.Select(d => d.PageCount)).Take(s.Dialogues.Count), "first pages");
        int pages = s.Pages.Count;
        Assert.Equal(3 * pages, O.Length(311));
        Same(O.Range(311, 0, 3 * pages), s.Pages.Select(p => p.Speaker).Concat(s.Pages.Select(p => p.Scene))
            .Concat(s.Pages.Select(p => p.Camera)), "block 311");
        Same(O.Block(294), s.TutorialStepLimitByStoryStep, "block 294");
        Same(O.Block(295), s.TutorialSteps.Select(t => t.Dialogue), "block 295");
        Same(O.Block(296), s.TutorialSteps.Select(t => t.Trigger), "block 296");
        Same(O.Block(297), s.TutorialSteps.Select(t => t.Parameter), "block 297");
        Same(O.Block(298), s.TutorialSteps.Select(t => t.Delay), "block 298");
        Same(O.Block(292), s.Hints.Select(h => h.Channel), "block 292");
        Same(O.Block(293), s.Hints.Select(h => h.MinMission), "block 293");
        Same(Enumerable.Range(0, O.ShortLength(2)).Select(i => O.Short(2, i)), s.HintChannelCooldowns, "short block 2");
    }

    [Fact]
    public void InterfaceTables_MatchTheOriginal()
    {
        InterfaceTables u = Rules.Interface;
        Same(O.Block(125), u.SquadListsToPurge, "block 125");
        Same(O.Block(126), u.InfoPanelLines.Select(l => Number(l.Property, -1)), "block 126");
        Same(O.Block(127), u.InfoPanelLines.Select(l => l.Add), "block 127");
        Same(O.Block(128), u.HitPointBarWidths, "block 128");
        Same(O.Block(145), u.WeaponSpriteLayers, "block 145");
        Same(O.Block(146), u.HudCounters.Select(c => (int)c), "block 146");
        Same(O.Block(147), u.UnitPanelProperties.Select(p => (int)p), "block 147");
        Same(O.Block(148), u.CursorFrame, "block 148");
        Same(O.Block(194), u.MinimapColors, "block 194");
        IReadOnlyList<InterfacePanel> p = u.Panels;
        Same(O.Block(195), p.Select(x => x.Picture).Concat(p.Select(x => x.FrameStyle)).Concat(p.Select(x => x.Layout))
            .Concat(p.Select(x => x.FirstLabel)).Concat(p.Select(x => x.AlwaysRedraw)), "block 195");
        Same(O.Block(304), u.CommandBarSecondLabels, "block 304");
        Same(O.Block(307), u.MusicListLengths, "block 307");
        Same(O.Block(178), u.HitEffectAnchors, "block 178");
    }

    private static int Code(InitialObject o) => o.Kind switch
    {
        InitialObjectKind.Building => o.Tribe!.Value,
        InitialObjectKind.Unit => (o.Skill!.Value * 2) + o.Tribe!.Value,
        _ => o.SpawnPoint!.Value,
    };

    private static int Type(InitialObject o) => o.Kind switch
    {
        InitialObjectKind.Building => o.BuildingKind!.Value,
        InitialObjectKind.Unit => -1,
        _ => -2,
    };

    // Animals have no extra value; every animal entry of the original holds 1 there, which nothing reads.
    private static int Extra(InitialObject o) => o.Kind switch
    {
        InitialObjectKind.Building => o.Level!.Value,
        InitialObjectKind.Unit => o.Age!.Value,
        _ => 1,
    };
}
