using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="MeleeBlows"/> with the original.</summary>
public sealed class MeleeBlowsTests
{
    private static readonly int[] Animations = { 24, 29, 31, 33 };
    private static readonly int[] Weapons = { 0, 9, 4, 3, 5 };
    private static readonly int[] Skills = { 0, 4, 9, 12 };

    private readonly ITestOutputHelper output;

    public MeleeBlowsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Strike_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 8))
            {
                int enemy = EnemyOf(sample.Game, id);
                if (enemy == 0 || !ActionTicksTests.PlaceNextTo(sample.Restore(), id, enemy))
                {
                    continue;
                }
                for (int v = 0; v < 3; v++)
                {
                    int k = id + sample.Frame + v;
                    int animation = Animations[k % Animations.Length], weapon = Weapons[k % Weapons.Length];
                    int skill = Skills[k % Skills.Length];
                    Action<GameCanvas> prepare = g =>
                    {
                        if (ActionTicksTests.PlaceNextTo(g, id, enemy))
                        {
                            Set(g, ActionField, id, k % 2 == 0 ? 0 : 1);
                            Set(g, TargetIndex, id, enemy);
                            Set(g, TargetCellCode, id, enemy);
                            Set(g, Animation, id, animation);
                            Set(g, Weapon, id, weapon);
                            Set(g, 17, id, skill);
                            Reveal(g, Get(g, X, enemy), Get(g, Y, enemy));
                        }
                    };
                    run.Check(sample, $"Strike unit {id} at {enemy} weapon {weapon}", prepare,
                        g => { BeginTurn(g, id); g.world_a.i_x = g.i_c; g.world_a.c_III(Ahead(g, id).X, Ahead(g, id).Y, 0); },
                        s => s.MeleeBlows.Strike(s.World.Unit(id), Ahead(s.World.Unit(id)), 0));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void Strike_HeavyArmedBlowMakesTheVictimFlinch()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit victim = world.Unit(1), attacker = world.Unit(51);
        (victim.X, victim.Y, victim.HitPoints, victim.MaxHitPoints, victim.Age, victim.BuildingSlot) = (5, 5, 50, 50, 20, -1);
        (attacker.X, attacker.Y, attacker.HitPoints, attacker.Age) = (4, 5, 50, 20);
        (attacker.Action, attacker.TargetIndex, attacker.Animation, attacker.Weapon, attacker.MeleeSkill) = (0, 1, 31, 0, 12);
        world.Map.SetCell(new GridPoint(5, 5), Tribes.Core.Maps.MapCell.UnitAt(1));
        world.Map.SetCell(new GridPoint(4, 5), Tribes.Core.Maps.MapCell.UnitAt(51));
        systems.MeleeBlows.Strike(attacker, new GridPoint(5, 5), 0);
        Assert.True(victim.HitPoints < 50);
        Assert.Equal(42, victim.Animation);
        Assert.Equal(0, victim.AnimationTick);
        Assert.Equal(-(62 + (world.Frame % 2)), victim.BuildingSlot);
    }

    private static GridPoint Ahead(GameCanvas g, int id)
    {
        DirectionStep step = LegacySamples.Content.Rules.Movement.Directions[Get(g, Facing, id)];
        return new GridPoint(Get(g, X, id) + step.Dx, Get(g, Y, id) + step.Dy);
    }

    private static GridPoint Ahead(Unit unit)
    {
        DirectionStep step = LegacySamples.Content.Rules.Movement.Directions[unit.Facing];
        return new GridPoint(unit.X + step.Dx, unit.Y + step.Dy);
    }
}
