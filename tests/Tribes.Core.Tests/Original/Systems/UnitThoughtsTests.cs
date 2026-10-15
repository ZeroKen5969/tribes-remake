using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitThoughts"/> with the original, plus edge cases.</summary>
public sealed class UnitThoughtsTests
{
    private const int ThoughtField = 33, SatietyField = 20;
    private static readonly int[] StartThoughts = { -1, 4, -2, -4 };

    private readonly ITestOutputHelper output;

    public UnitThoughtsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Think_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 5))
            {
                foreach (int thought in StartThoughts)
                {
                    int satiety = thought == -2 ? 0 : LegacySamples.Field(game, SatietyField, id);
                    run.Check(sample, $"Think unit {id} thought {thought} satiety {satiety}",
                        g =>
                        {
                            UnitSampling.Set(g, ThoughtField, id, thought);
                            UnitSampling.Set(g, SatietyField, id, satiety);
                            UnitSampling.Current(g, id).B_();
                        },
                        s =>
                        {
                            (s.World.Unit(id).Thought, s.World.Unit(id).Satiety) = (thought, satiety);
                            s.UnitThoughts.Think(s.World.Unit(id));
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Think_CouplesThinkOfLoveAndCastersKeepTheirIcon()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 2);
        (unit.Action, unit.Thought) = (ActionNumbers.FirstCoupleAction, -1);
        systems.UnitThoughts.Think(unit);
        Assert.Equal(1, unit.Thought);

        (unit.Animation, unit.Thought) = (39, 11);
        systems.UnitThoughts.Think(unit);
        Assert.Equal(11, unit.Thought);

        (unit.Animation, unit.Action, unit.Thought, unit.Energy) = (8, ActionNumbers.Idle, -1, 0);
        systems.UnitThoughts.Think(unit);
        Assert.Equal(2, unit.Thought);
    }
}
