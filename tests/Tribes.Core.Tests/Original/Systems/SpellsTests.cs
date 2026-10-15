using System;
using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Core.Tests.Systems.WorkScenarios;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Spells"/> with the original.</summary>
public sealed class SpellsTests
{
    private const int FirstSpell = 94, Spells = 4, Happiness = 5, PairLink = 82;

    private readonly ITestOutputHelper output;

    public SpellsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CastAndMarkCasting_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            int[] live = Live(sample.Restore()).ToArray();
            foreach (int id in Some(sample, 6))
            {
                if (FindBuilding(sample.Game, TribeOf(id), Sanctuary) == 0)
                {
                    continue;
                }
                int target = live.FirstOrDefault(t => t != id && TribeOf(t) == TribeOf(id));
                if (target == 0)
                {
                    continue;
                }
                for (int spell = 0; spell < Spells; spell++)
                {
                    int s0 = spell;
                    Action<GameCanvas> prepare = g => Caster(g, id, s0, target);
                    run.Check(sample, $"Cast unit {id} spell {spell}", prepare,
                        g => { BeginTurn(g, id); g.world_a.i_R = Get(g, SubState, id); g.world_a.K_(); return g.world_a.z_f; },
                        s => s.Spells.Cast(s.World.Unit(id)));
                }
                for (int casting = 0; casting <= 1; casting++)
                {
                    int c = casting;
                    run.Check(sample, $"MarkCasting unit {id} casting {c}", g => Caster(g, id, 0, target),
                        g => g.a_IB_B(id, (sbyte)c),
                        s => s.Spells.MarkCasting(s.World.Unit(id), c));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Cast_MarksTheEnemyAreaAndShiftsOlderAlarms()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit caster = systems.World.Unit(51);
        (caster.Action, caster.DestinationX, caster.DestinationY, caster.WitchcraftSkill) = (97, 7, 8, 1);
        Tribes.Core.World.AlarmCall[] alarms = systems.World.Tribes[1].Alarms;
        (alarms[0].X, alarms[0].Y, alarms[0].Age) = (1, 2, 30);
        Assert.True(systems.Spells.Cast(caster));
        Assert.Equal((7, 8, 0, 0), (alarms[0].X, alarms[0].Y, alarms[0].Age, alarms[0].Spotted));
        Assert.Equal((1, 2, 30), (alarms[1].X, alarms[1].Y, alarms[1].Age));
    }

    [Fact]
    public void Cast_WithoutATargetIsDone()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit caster = systems.World.Unit(1);
        (caster.Action, caster.TargetIndex, caster.WorkProgress) = (94, 0, 50);
        Assert.True(systems.Spells.Cast(caster));
        Assert.Equal(0, caster.WorkProgress);
        systems.Spells.MarkCasting(caster, 1);
    }

    /// <summary>
    /// <paramref name="id"/> is the witch doctor of its tribe's sanctuary casting <paramref name="spell"/> at
    /// <paramref name="target"/> (who is hurt and unhappy, to see the spell's effect).
    /// </summary>
    private static void Caster(GameCanvas g, int id, int spell, int target)
    {
        WitchDoctor(g, id, spell, target);
        Set(g, ActionField, id, FirstSpell + spell);
        Set(g, TargetIndex, id, target);
        Set(g, DestinationX, id, Get(g, X, target));
        Set(g, DestinationY, id, Get(g, Y, target));
        Set(g, HitPoints, target, Math.Max(1, Get(g, HitPoints, target) - 15));
        Set(g, Happiness, target, 40);
        Set(g, PairLink, target, 0);
    }
}
