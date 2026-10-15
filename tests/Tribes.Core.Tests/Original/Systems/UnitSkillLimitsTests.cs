using System.Collections.Generic;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitSkillLimits"/> with the original (the limits and the panel icons).</summary>
public sealed class UnitSkillLimitsTests
{
    private const int TaskIconField = 22, FirstSkillIconField = 23, ThrowRangeField = 18;

    private readonly ITestOutputHelper output;

    public UnitSkillLimitsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ClampRangesAndIcons_MatchTheOriginal()
    {
        var run = new ParityRun();
        var iconFailures = new List<string>();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6))
            {
                foreach (int range in new[] { 1, 12 })
                {
                    run.Check(sample, $"ClampRanges unit {id} throw {range}",
                        g => { UnitSampling.Set(g, ThrowRangeField, id, range); UnitSampling.Current(g, id).A_(); },
                        s => { s.World.Unit(id).ThrowRange = range; UnitSkillLimits.ClampRanges(s.World.Unit(id)); });
                }
                game = sample.Restore();
                GameWorld world = Parity.LegacyRules.Import(game, LegacySamples.Content);
                SkillIcons icons = UnitSkillLimits.Icons(world, world.Unit(id));
                UnitSampling.Current(game, id).A_();
                int F(int field) => LegacySamples.Field(game, field, id);
                var expected = new SkillIcons(F(TaskIconField), F(FirstSkillIconField), F(FirstSkillIconField + 1),
                    F(FirstSkillIconField + 2), F(FirstSkillIconField + 3));
                if (icons != expected)
                {
                    iconFailures.Add($"{sample} unit {id}: expected {expected}, actual {icons}");
                }
            }
        }
        output.WriteLine(run.ToString());
        Assert.Empty(iconFailures);
        run.AssertAgrees(minimum: 500);
    }
}
