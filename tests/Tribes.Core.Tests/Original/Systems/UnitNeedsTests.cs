using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitNeeds"/> with the original, plus edge cases.</summary>
public sealed class UnitNeedsTests
{
    private const int UnitsPerTribe = 4;

    private readonly ITestOutputHelper output;

    public UnitNeedsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Tend_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, UnitsPerTribe))
            {
                int action = LegacySamples.Field(game, UnitSampling.ActionField, id);
                int period = LegacySamples.Content.Rules.Actions.Actions[action].SatietyDecayPeriod * ((id - 1) / 50 * 4 + 1);
                int[] frames =
                {
                    UnitSampling.FrameWhere(sample.Frame, 127, id & 15),
                    UnitSampling.FrameWhere(sample.Frame, 63, id & 63),
                    UnitSampling.FrameWhere(sample.Frame, 31, id & 15),
                    sample.Frame + period - (sample.Frame % period) + (id & 3),
                    sample.Frame + (id * 7 % 64),
                };
                foreach (int frame in frames)
                {
                    run.Check(sample, $"Tend unit {id} frame {frame}",
                        g => { g.i_c = frame; return UnitSampling.Current(g, id).r__Z(); },
                        s => { s.World.Frame = frame; return s.UnitNeeds.Tend(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void SurvivesWounds_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 2))
            {
                foreach (int hp in new[] { 0, -3, 4 })
                {
                    run.Check(sample, $"SurvivesWounds unit {id} hp {hp}",
                        g => { UnitSampling.Set(g, UnitSampling.HitPointsField, id, hp); return UnitSampling.Current(g, id).s__Z(); },
                        s => { s.World.Unit(id).HitPoints = hp; return s.UnitNeeds.SurvivesWounds(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Tend_StarvingAndExhaustedSoldiersLoseHitPointsAndWarnThePlayer()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Mission = 1;
        world.Orders.Rally = true;
        Unit unit = Adult(world, 3);
        (unit.Satiety, unit.Energy, unit.Status) = (0, 0, UnitStatus.Mustered);
        int period = world.Content.Rules.Actions.Actions[unit.Action].SatietyDecayPeriod;
        world.Frame = period * world.Content.Rules.Animations.Animations[unit.AnimationNumber].EnergyDecayPeriod!.Value;

        Assert.True(systems.UnitNeeds.Tend(unit));

        Assert.Equal(18, unit.HitPoints);
        Assert.Equal(0, world.Hints.Cooldown(world.Content.Rules.Story.Hints[14].Channel));
    }

    [Fact]
    public void Tend_KitchenWorkersEatAndWitchDoctorsHealThemselves()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Mission = 1;
        Unit cook = Adult(world, 4);
        cook.AssignedTask = 54;
        cook.Satiety = 50;
        world.Frame = 4 + 64 * 5;
        Assert.True(systems.UnitNeeds.Tend(cook));
        Assert.Equal(51, cook.Satiety);

        Unit witch = Adult(world, 5);
        (witch.AssignedTask, witch.WitchcraftSkill, witch.HitPoints) = (57, 4, 10);
        world.Frame = 34 * 10;
        Assert.True(systems.UnitNeeds.Tend(witch));
        Assert.Equal(12, witch.HitPoints);
    }

    [Fact]
    public void Tend_TheSickWasteAwayAndDieOnTheirCell()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit unit = Adult(world, 6);
        (unit.Status, unit.HitPoints) = (UnitStatus.Diseased, 1);
        world.Frame = 32 + 6;

        Assert.False(systems.UnitNeeds.Tend(unit));
        Assert.True(unit.HitPoints < 0);
    }

    [Fact]
    public void SurvivesWounds_PutsOffTheDeathOfAUnitInsideABuilding()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = Adult(systems.World, 7);
        systems.World.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.Empty);
        unit.HitPoints = 0;

        Assert.True(systems.UnitNeeds.SurvivesWounds(unit));

        Assert.Equal((1, 1, OrderModes.Walk), (unit.HitPoints, unit.MaxHitPoints, unit.OrderMode));
        Assert.Equal(UnitNeeds.DelayedDeathBit, unit.PairLink & UnitNeeds.DelayedDeathBit);
    }

    /// <summary>An idle adult of the player standing on its own cell.</summary>
    internal static Unit Adult(GameWorld world, int id)
    {
        Unit unit = world.Unit(id);
        (unit.X, unit.Y, unit.Age, unit.HitPoints, unit.MaxHitPoints) = (5 + (id % 30), 6 + (id / 30), 20, 20, 40);
        (unit.Action, unit.AssignedTask, unit.Animation, unit.Satiety, unit.Energy) = (ActionNumbers.Idle, ActionNumbers.Idle, 8, 60, 60);
        world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.UnitAt(id));
        return unit;
    }
}
