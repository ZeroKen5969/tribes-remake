using System.Linq;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ActionTicks"/> (with the blows, throws and work steps it starts) with the original.</summary>
public sealed class ActionTicksTests
{
    private static readonly int[] MeleeAnimations = { 24, 29, 31, 33 };
    private static readonly int[] MeleeWeapons = { 0, 9, 4, 3 };
    private static readonly int[] ThrownWeapons = { 1, 2, 5 };

    private readonly ITestOutputHelper output;

    public ActionTicksTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Advance_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            int[] live = Live(sample.Restore()).ToArray();
            foreach (int id in Some(sample, 6))
            {
                Check(run, sample, $"AsIs unit {id}", id, null);
                Check(run, sample, $"Settle unit {id}", id, g => { Set(g, Animation, id, -109); Set(g, AnimationTick, id, 0); });
                Check(run, sample, $"GetUp unit {id}", id, g => { Set(g, Animation, id, -108); Set(g, AnimationTick, id, 5); });
                Check(run, sample, $"Unload unit {id}", id, g => Unload(g, id, 19 + (id % 8)));
                int enemy = live.FirstOrDefault(e => TribeOf(e) != TribeOf(id) && Get(sample.Game, Age, e) >= 0);
                if (enemy > 0 && PlaceNextTo(sample.Restore(), id, enemy))
                {
                    int k = id + sample.Frame;
                    Check(run, sample, $"Melee unit {id} at {enemy}", id, g => Melee(g, id, enemy, k));
                    Check(run, sample, $"Throw unit {id} at {enemy}", id, g => Throw(g, id, enemy, k));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }

    private static void Check(UnitWorkParity run, LegacySample sample, string what, int id, System.Action<GameCanvas>? prepare) =>
        run.Check(sample, what, prepare,
            g => { BeginTurn(g, id); g.world_a.i_x = g.i_c; g.world_a.w_(); },
            s => s.ActionTicks.Advance(s.World.Unit(id)));

    private static void Unload(GameCanvas g, int id, int action)
    {
        Set(g, ActionField, id, action);
        Set(g, Animation, id, 22);
        Set(g, AnimationTick, id, 9);
        Set(g, CarriedAmount, id, 3);
    }

    /// <summary>Puts <paramref name="id"/> next to <paramref name="enemy"/>, facing it, one frame before its blow lands.</summary>
    private static void Melee(GameCanvas g, int id, int enemy, int variant)
    {
        if (!PlaceNextTo(g, id, enemy))
        {
            return;
        }
        int animation = MeleeAnimations[variant % MeleeAnimations.Length];
        Set(g, ActionField, id, 0);
        Set(g, TargetIndex, id, enemy);
        Set(g, TargetCellCode, id, enemy);
        Set(g, Weapon, id, MeleeWeapons[variant % MeleeWeapons.Length]);
        Set(g, Animation, id, animation);
        int trigger = LegacySamples.Content.Rules.Animations.Animations[animation].TriggerFrame;
        Set(g, AnimationTick, id, trigger - 1);
    }

    /// <summary>
    /// Makes <paramref name="id"/> throw at <paramref name="enemy"/> on its next frame, if the enemy is
    /// within the distance table (a throw animation cannot start at a target further away).
    /// </summary>
    private static void Throw(GameCanvas g, int id, int enemy, int variant)
    {
        if (System.Math.Abs(Get(g, X, enemy) - Get(g, X, id)) > 12 || System.Math.Abs(Get(g, Y, enemy) - Get(g, Y, id)) > 12)
        {
            return;
        }
        Set(g, ActionField, id, 5 + (variant % 2));
        Set(g, TargetIndex, id, enemy);
        Set(g, TargetCellCode, id, enemy);
        Set(g, DestinationX, id, Get(g, X, enemy));
        Set(g, DestinationY, id, Get(g, Y, enemy));
        Set(g, Weapon, id, ThrownWeapons[variant % ThrownWeapons.Length]);
        Set(g, Animation, id, 35 + (variant % 2));
        int frames = LegacySamples.Content.Rules.Animations.Animations[35 + (variant % 2)].FrameCount;
        Set(g, AnimationTick, id, frames - 2);
        Set(g, ProjectileFlightSteps, id, 0);
    }

    /// <summary>Moves <paramref name="id"/> onto a free cell next to <paramref name="enemy"/> and turns it towards it.</summary>
    internal static bool PlaceNextTo(GameCanvas g, int id, int enemy)
    {
        int ex = Get(g, X, enemy), ey = Get(g, Y, enemy);
        var directions = LegacySamples.Content.Rules.Movement.Directions;
        foreach (DirectionStep step in directions)
        {
            int x = ex - step.Dx, y = ey - step.Dy;
            if (x < 0 || y < 0 || x >= g.s_c || y >= g.s_d || g.bArr2_c[y][x] != 0)
            {
                continue;
            }
            int ox = Get(g, X, id), oy = Get(g, Y, id);
            if (g.bArr2_c[oy][ox] == id)
            {
                g.bArr2_c[oy][ox] = 0;
            }
            g.bArr2_c[y][x] = (sbyte)id;
            Set(g, X, id, x);
            Set(g, Y, id, y);
            Set(g, OffsetX, id, 0);
            Set(g, OffsetY, id, 0);
            Set(g, Facing, id, step.Facing);
            return true;
        }
        return false;
    }
}
