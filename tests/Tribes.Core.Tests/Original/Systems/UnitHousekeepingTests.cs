using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitHousekeeping"/> with the original, plus edge cases.</summary>
public sealed class UnitHousekeepingTests
{
    private readonly ITestOutputHelper output;

    public UnitHousekeepingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var dying = Enumerable.Range(1, 100).Where(id => LegacySamples.Field(game, UnitSampling.HitPointsField, id) < 0);
            foreach (int id in UnitSampling.Units(game, 4).Concat(dying))
            {
                int[] frames =
                {
                    UnitSampling.FrameWhere(sample.Frame, 3, id & 3),
                    UnitSampling.FrameWhere(sample.Frame, 31, id & 15),
                    UnitSampling.FrameWhere(sample.Frame, 31, 0),
                    UnitSampling.FrameWhere(sample.Frame, 7, id & 7) | 1,
                };
                foreach (int frame in frames)
                {
                    run.Check(sample, $"Update unit {id} frame {frame}",
                        g => { g.i_c = frame; return UnitSampling.Current(g, id).a__Z(); },
                        s => { s.World.Frame = frame; return s.UnitHousekeeping.Update(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Update_ADelayedDeathHappensBackOnTheCell()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 13);
        unit.PairLink = 0x80;

        Assert.True(systems.UnitHousekeeping.Update(unit));
        Assert.True(unit.HitPoints < 0);

        unit.AnimationTick = 100;
        Assert.True(systems.UnitHousekeeping.Update(unit));
        Assert.Equal(0, unit.HitPoints);
    }

    [Fact]
    public void Update_CountsSkilledFightersAndClearsOldSpellIcons()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Script.ObjectiveStage = 2;
        Unit unit = UnitNeedsTests.Adult(world, 14);
        (unit.MeleeSkill, unit.Thought, unit.Action) = (6, 12, 0);
        world.FramesSinceCombat = 30;

        Assert.False(systems.UnitHousekeeping.Update(unit));

        Assert.Equal((1, -1, 0), (world.Script.ObjectiveTimer, unit.Thought, world.FramesSinceCombat));
    }
}
