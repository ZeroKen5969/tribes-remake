using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Senses;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitStealth"/> with the original, plus edge cases.</summary>
public sealed class UnitStealthTests
{
    private const int ScoutingField = 75, RunField = 77, AnimationField = 29, AnimationTickField = 30;
    private static readonly int[] Scouting = { 4, 7, 11 };
    private static readonly int[] StatusBits = { 0, 0x01, 0x02, 0x03 };

    private readonly ITestOutputHelper output;

    public UnitStealthTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 5))
            {
                int frame = UnitSampling.FrameWhere(sample.Frame, 7, id & 7);
                int skill = Scouting[(id + (sample.Frame / 1000)) % Scouting.Length];
                foreach (int bits in StatusBits)
                {
                    int status = (LegacySamples.Field(game, UnitSampling.StatusField, id) & ~3) | bits;
                    int f = frame + (bits == 3 ? 1 : 0);
                    run.Check(sample, $"Update unit {id} scouting {skill} status {status} frame {f}",
                        g =>
                        {
                            g.i_c = f;
                            UnitSampling.Set(g, ScoutingField, id, skill);
                            UnitSampling.Set(g, UnitSampling.StatusField, id, status);
                            return UnitSampling.Current(g, id).h_();
                        },
                        s =>
                        {
                            s.World.Frame = f;
                            Unit unit = s.World.Unit(id);
                            (unit.ScoutingSkill, unit.Status) = (skill, (UnitStatus)(status & 0xFF));
                            return s.UnitStealth.Update(new UnitTurn(unit, 0));
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Update_AnIdleUnseenScoutHidesAndGetsUpWhenSeen()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit scout = UnitNeedsTests.Adult(world, 3);
        scout.ScoutingSkill = 8;
        world.Frame = 8 + 3;
        Assert.True(systems.UnitStealth.Update(new UnitTurn(scout, 0)));
        Assert.Equal(UnitAnimations.Hide, scout.AnimationNumber);

        scout.AnimationTick = 100;
        world.Frame = 12;
        Assert.False(systems.UnitStealth.Update(new UnitTurn(scout, 0)));
        Assert.Equal(UnitStatus.Hidden | UnitStatus.Undetected, scout.Status);

        Unit watcher = world.Unit(60);
        (watcher.X, watcher.Y, watcher.HitPoints, watcher.ScoutingSkill) = (scout.X + 1, scout.Y, 10, 9);
        world.Map.SetCell(new GridPoint(watcher.X, watcher.Y), MapCell.UnitAt(60));
        scout.Animation = 8;
        world.Frame = 8 + 3;
        Assert.True(systems.UnitStealth.Update(new UnitTurn(scout, 0)));
        Assert.Equal((UnitAnimations.Unhide, UnitStatus.None), (scout.AnimationNumber, scout.Status));
    }

    [Fact]
    public void Update_AGoodScoutSpotsEnemyTrapsAndAlarmPlaces()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit scout = UnitNeedsTests.Adult(world, 4);
        (scout.ScoutingSkill, scout.Action) = (10, ActionNumbers.ChildIdle);
        var trap = new GridPoint(scout.X + 2, scout.Y);
        world.Map.SetCell(trap, new MapCell(CellContent.Trap, 21, 1));
        AlarmCall alarm = world.Tribes[1].Alarms[0];
        (alarm.X, alarm.Y, alarm.Age) = (scout.X - 3, scout.Y + 2, 5);
        world.Ai.Flags = 2;
        world.Frame = 4;

        Assert.False(systems.UnitStealth.Update(new UnitTurn(scout, 0)));

        Assert.Equal(new MapCell(CellContent.SpottedTrap, 21, 0), world.Map.CellAt(trap));
        Assert.Equal((1, 1), (world.Ai.Flags, alarm.Spotted));
        Assert.True(world.Tribes[1].Alarms.Skip(1).All(a => a.Spotted == 0));
    }
}
