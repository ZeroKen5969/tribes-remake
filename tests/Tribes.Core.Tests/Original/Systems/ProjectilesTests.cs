using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Projectiles"/> with the original, plus edge cases.</summary>
public sealed class ProjectilesTests
{
    private static readonly int[] Weapons = { 1, 2, 5, 0, 9 };
    private static readonly int[] ThrowActions = { 5, 7, 8, 92, 93, 106 };

    private readonly ITestOutputHelper output;

    public ProjectilesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ThrowAndFly_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 8))
            {
                int enemy = NearestEnemy(sample.Game, id);
                if (enemy == 0)
                {
                    continue;
                }
                int k = id + sample.Frame;
                int action = ThrowActions[k % ThrowActions.Length], weapon = Weapons[k % Weapons.Length];
                Action<GameCanvas> aim = g => Aim(g, id, enemy, action, weapon, moving: k % 3 == 0);
                run.Check(sample, $"Throw unit {id} action {action} weapon {weapon}", aim,
                    g => { BeginTurn(g, id); g.world_a.a_BBII((sbyte)Get(g, X, id), (sbyte)Get(g, Y, id), 0, 3); },
                    s => s.Projectiles.Throw(s.World.Unit(id), new GridPoint(s.World.Unit(id).X, s.World.Unit(id).Y), 0, 3));
                foreach (int step in new[] { 0, 3, 20 })
                {
                    Action<GameCanvas> flying = g =>
                    {
                        aim(g);
                        BeginTurn(g, id);
                        g.world_a.a_BBII((sbyte)Get(g, X, id), (sbyte)Get(g, Y, id), 0, 3);
                        Set(g, ProjectileStep, id, Math.Min(step, Get(g, ProjectileFlightSteps, id)));
                    };
                    run.Check(sample, $"Fly unit {id} step {step}", flying,
                        g => { BeginTurn(g, id); if (Get(g, ProjectileFlightSteps, id) > 0) { g.world_a.R_(); } },
                        s => { if (s.World.Unit(id).ProjectileFlightSteps > 0) { s.Projectiles.Fly(s.World.Unit(id)); } });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void Throw_KeepsAProjectileAlreadyInFlight()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Unit unit = systems.World.Unit(1);
        unit.ProjectileFlightSteps = 4;
        unit.DestinationX = 3;
        systems.Projectiles.Throw(unit, new GridPoint(0, 0), 0, 0);
        Assert.Equal(4, unit.ProjectileFlightSteps);
        Assert.Empty(systems.PendingEvents);
    }

    [Fact]
    public void Throw_OutOfReachDoesNothing()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Unit unit = systems.World.Unit(1);
        unit.Action = 5;
        unit.ThrowRange = 3;
        unit.Weapon = 1;
        (unit.X, unit.Y, unit.DestinationX, unit.DestinationY) = (2, 2, 20, 2);
        systems.Projectiles.Throw(unit, new GridPoint(2, 2), 0, 0);
        Assert.Equal(0, unit.ProjectileFlightSteps);
    }

    [Fact]
    public void Throw_FarTargetIsTimedAtTheTableEdgeAndRaisesTheArcEvent()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Unit unit = systems.World.Unit(1);
        unit.Action = 93;
        unit.ThrowRange = 12;
        unit.Weapon = 9;
        (unit.X, unit.Y, unit.DestinationX, unit.DestinationY) = (2, 2, 13, 13);
        systems.Projectiles.Throw(unit, new GridPoint(2, 2), 24, 37);
        Assert.True(unit.ProjectileFlightSteps > 0);
        Assert.Contains(systems.PendingEvents, e => e is ProjectileThrown { Unit: 1, StartHeight: 24, ImpactDrop: 37 });
    }

    /// <summary>The nearest enemy of <paramref name="id"/> standing on its cell, or 0.</summary>
    private static int NearestEnemy(GameCanvas g, int id) =>
        Live(g).Where(e => TribeOf(e) != TribeOf(id) && g.bArr2_c[Get(g, Y, e)][Get(g, X, e)] == e)
            .OrderBy(e => Math.Max(Math.Abs(Get(g, X, e) - Get(g, X, id)), Math.Abs(Get(g, Y, e) - Get(g, Y, id))))
            .FirstOrDefault();

    /// <summary>Makes <paramref name="id"/> aim <paramref name="action"/> at <paramref name="enemy"/> with <paramref name="weapon"/>.</summary>
    private static void Aim(GameCanvas g, int id, int enemy, int action, int weapon, bool moving)
    {
        Set(g, ActionField, id, action);
        Set(g, Weapon, id, weapon);
        Set(g, TargetIndex, id, enemy);
        Set(g, TargetCellCode, id, enemy);
        Set(g, DestinationX, id, Get(g, X, enemy));
        Set(g, DestinationY, id, Get(g, Y, enemy));
        Set(g, ProjectileFlightSteps, id, 0);
        Set(g, 18, id, 9);
        if (moving)
        {
            Set(g, Animation, enemy, 0);
        }
    }
}
