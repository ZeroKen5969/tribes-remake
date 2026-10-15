using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content.Rules;
using Xunit;
using static Tribes.Core.Tests.RuleCheck;

namespace Tribes.Core.Tests;

/// <summary>
/// The exported movement, action and skill tables (data/rules) hold exactly the original's
/// table values. Numbers in comments and calls are the original's block numbers.
/// </summary>
public sealed class RuleActionTablesTests
{
    private static readonly GameRules Rules = OriginalTables.ExportedRules;
    private static OriginalTables O => OriginalTables.Instance;

    [Fact]
    public void Directions_AndFacingLookups_MatchTheOriginal()
    {
        MovementTables m = Rules.Movement;
        Assert.Equal(8, m.Directions.Count);
        Same(O.Block(1), m.Directions.Select(d => d.Dx).Concat(m.Directions.Select(d => d.Dy)), "block 1");
        Same(O.Block(2), m.Directions.Select(d => d.SideDx).Concat(m.Directions.Select(d => d.SideDy)), "block 2");
        Same(O.Block(3), m.FacingByDeltaRowMajor, "block 3");
        Same(O.Block(4), m.FacingByDeltaColumnMajor, "block 4");
        Same(O.Block(0), m.LineSteps.SelectMany(s => new[] { s.Straight.Dx, s.Straight.Dy, s.Diagonal.Dx, s.Diagonal.Dy }), "block 0");
    }

    [Fact]
    public void SpeedClasses_MatchTheOriginal()
    {
        IReadOnlyList<SpeedClass> speeds = Rules.Movement.SpeedClasses;
        Assert.Equal(9, speeds.Count);
        for (int w = 0; w < 2; w++)
        {
            SpeedGait Gait(SpeedClass s) => w == 0 ? s.Straight : s.Diagonal;
            Same(O.Block(15).Skip(w * 9).Take(9), speeds.Select(s => Gait(s).StepFrames), "block 15");
            Same(O.Block(12 + (w * 2)), Starts(speeds.Select(s => Gait(s).OffsetX.Count)), "pixel starts");
            Same(O.Block(5 + w), speeds.SelectMany(s => Gait(s).OffsetX), "x offsets");
            Same(O.Block(7 + w), speeds.SelectMany(s => Gait(s).OffsetY), "y offsets");
            Same(O.Block(9 + w), speeds.SelectMany(s => Gait(s).SwapOffsetX), "swap x offsets");
            Same(O.Block(11 + (w * 2)), speeds.SelectMany(s => Gait(s).SwapOffsetY), "swap y offsets");
            foreach ((int data, int starts, string name) in new[] { (129, 130, "run start"), (133, 134, "run stop") })
            {
                IReadOnlyList<int> Remap(SpeedClass s) => name == "run start" ? Gait(s).RunStartTicks : Gait(s).RunStopTicks;
                Same(O.Block(data + (w * 2)), speeds.SelectMany(Remap), name + " remap");
                Same(O.Block(starts + (w * 2)), Starts(speeds.Skip(5).Take(4).Select(s => Remap(s).Count)), name + " starts");
                Assert.All(speeds.Take(5), s => Assert.Empty(Remap(s)));
            }
        }
    }

    [Fact]
    public void DistanceTables_MatchTheOriginal()
    {
        MovementTables m = Rules.Movement;
        Same(Enumerable.Range(0, O.ShortLength(1)).Select(i => O.Short(1, i)), m.CellDistances.SelectMany(r => r), "short block 1");
        Assert.All(m.CellDistances, r => Assert.Equal(11, r.Count));
        Same(O.Block(53), m.DistanceAndFacing.SelectMany(r => r), "block 53");
        Assert.All(m.DistanceAndFacing, r => Assert.Equal(25, r.Count));
        Same(O.Block(54), m.RevealDiscs.SelectMany(d => d.RowWidths), "block 54");
        Same(O.Block(55).Select(v => v & 0xFF), Starts(m.RevealDiscs.Select(d => d.RowWidths.Count)), "block 55");
        Assert.All(m.RevealDiscs, d => Assert.Equal((2 * d.Radius) + 1, d.RowWidths.Count));
    }

    [Fact]
    public void Actions_MatchTheOriginal()
    {
        IReadOnlyList<ActionDefinition> actions = Rules.Actions.Actions;
        Assert.Equal(112, actions.Count);
        foreach (ActionDefinition a in actions)
        {
            int i = a.Id;
            string at = $"action {i}";
            Same(O.B(26, i), (int)a.TargetAliveProperty, at + " target alive");
            Same(O.U(27, i), (int)a.Traits, at + " traits");
            Same(O.B(28, i), Number(a.TargetPositionProperty, 43), at + " target position");
            Same(O.B(29, i), a.StandingTask, at + " standing task");
            Same(O.B(30, i), a.EffectGoal, at + " effect goal");
            Same(O.B(31, i), (int)a.EffectProperty, at + " effect property");
            Same(O.B(32, i), Number(a.EffectObjectProperty, 0), at + " effect object");
            Same(O.B(33, i), Encoded(a.RateTarget), at + " rate target");
            Same(O.B(34, i), Encoded(a.EffectDelta), at + " effect delta");
            Same(O.B(35, i), (int)a.RateProperty, at + " rate property");
            Same(O.B(36, i), a.RateBonus, at + " rate bonus");
            Same(O.B(37, i), a.ActionTicks, at + " action ticks");
            Same(O.B(38, i), Encoded(a.Range), at + " range");
            Same(O.B(39, i), (int)a.BuildingInteraction, at + " building interaction");
            Same(O.B(40, i), a.CarriedItem, at + " carried item");
            Same(O.B(41, i), a.Category, at + " category");
            Same(O.B(43, i), a.MovePriority, at + " move priority");
            Same(O.B(45, i), a.TrainedStat, at + " trained stat");
            Same(O.B(46, i), a.TrainingChancePercent, at + " training chance");
            Same(O.B(47, i), a.TrainingCap, at + " training cap");
            Same(O.B(48, i), a.TrainsEveryStep ? 2 : 0, at + " training mode");
            Same(O.B(49, i), a.WorkCyclePoints, at + " work cycle");
            Same(O.B(50, i), a.SatietyDecayPeriod, at + " satiety decay");
            Same(O.B(51, i), a.HappinessDelta, at + " happiness delta");
            Same(O.B(52, i), a.EnergyDecayPeriod, at + " energy decay");
            Same(O.B(160, i), a.NextActionOnComplete, at + " next action");
            Same(O.B(169, i), a.PauseVariant, at + " pause variant");
            Same(i < 10 ? O.B(79, i) : -1, a.AttackTargetCategory ?? -1, at + " attack target category");
            Same(i < 10 ? O.B(122, i) : -1, a.ThreatThreshold ?? -1, at + " threat threshold");
            Same(i is >= 5 and < 10 ? O.B(123, i - 5) : -1, a.ProjectileImpactDrop ?? -1, at + " impact drop");
        }
        Same(O.Block(166).Select(v => v & 0xFF), Starts(actions.Select(a => a.AnimationChoices.Count)), "block 166");
        Same(O.Block(164).Select(v => v & 0xFF), actions.SelectMany(a => a.AnimationChoices.Select(c => c.Animation)), "block 164");
        Same(O.Block(165), actions.SelectMany(a => a.AnimationChoices.Select(c => c.CumulativePercent)), "block 165");
    }

    [Fact]
    public void ActionCategoriesAndPauses_MatchTheOriginal()
    {
        ActionTables a = Rules.Actions;
        Same(O.Block(42), a.Categories.Select(c => c.ServiceGroup), "block 42");
        Same(O.Block(100), a.Categories.Select(c => c.CallToArmsWeight), "block 100");
        Same(O.Block(168), a.PauseVariants.Select(p => p.MinSubState).Concat(a.PauseVariants.Select(p => p.MaxSubState)), "block 168");
        Same(O.Block(167), a.PauseAnimationOffsetBySubState, "block 167");
    }

    [Fact]
    public void Skills_MatchTheOriginal()
    {
        SkillTables s = Rules.Skills;
        Same(O.Block(44), s.TrainableStats.Select(t => (int)t.Property), "block 44");
        Same(O.Block(64), s.TrainableStats.Select(t => t.ActionTrainingStep), "block 64");
        Same(O.Block(107), s.Skills.Select(k => (int)k.Property), "block 107");
        Same(O.Block(108), s.Skills.Where(k => k.TaskIcon.HasValue).Select(k => k.TaskIcon!.Value), "block 108");
        Assert.All(s.Skills.Skip(O.Length(108)), k => Assert.Null(k.TaskIcon));
        Same(O.Block(114), s.TrainingTargets.Select(t => t.BuildingKind).Concat(s.TrainingTargets.Select(t => t.Action)), "block 114");
        Same(Enumerable.Range(0, 3), s.TrainingTargets.Select(t => t.Skill), "training target skills");
    }

    /// <summary>The start offsets of consecutive rows of the given lengths, with the end offset last.</summary>
    internal static IEnumerable<int> Starts(IEnumerable<int> lengths)
    {
        int at = 0;
        yield return at;
        foreach (int length in lengths)
        {
            at += length;
            yield return at;
        }
    }
}
