using System.Collections.Generic;
using System.Linq;
using Tribes.Core.World;
using Tribes.Legacy;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// Picks the units and frames the parity tests of the unit body and movement rules try, and sets
/// the same unit field or frame on both sides of a comparison.
/// </summary>
internal static class UnitSampling
{
    /// <summary>The original's unit fields the tests set by number.</summary>
    public const int HitPointsField = 19, AgeField = 14, AgeTicksField = 66, StatusField = 79, ActionField = 28;

    /// <summary>
    /// Up to <paramref name="perTribe"/> live units of each tribe of the sampled state, spread over
    /// the slots, children and units inside buildings included when there are any.
    /// </summary>
    public static IEnumerable<int> Units(GameCanvas game, int perTribe)
    {
        var live = LegacySamples.LiveUnits(game).ToList();
        foreach (int tribe in new[] { 0, 1 })
        {
            var own = live.Where(id => (id - 1) / 50 == tribe).ToList();
            var special = own.Where(id => LegacySamples.Field(game, AgeField, id) < 12 || !OnItsCell(game, id)).Take(2);
            foreach (int id in special.Concat(SampleInputs.Spread(own, perTribe)).Distinct().Take(perTribe + 2))
            {
                yield return id;
            }
        }
    }

    /// <summary>Whether unit <paramref name="id"/> stands on its own cell (it is not inside a building).</summary>
    public static bool OnItsCell(GameCanvas game, int id)
    {
        int x = LegacySamples.Field(game, 0, id), y = LegacySamples.Field(game, 1, id);
        return x >= 0 && y >= 0 && x < game.s_e && y < game.s_f && game.bArr2_c[y][x] == id;
    }

    /// <summary>The first frame from <paramref name="from"/> on whose bits under <paramref name="mask"/> equal <paramref name="value"/>.</summary>
    public static int FrameWhere(int from, int mask, int value)
    {
        int frame = (from & ~mask) | (value & mask);
        return frame >= from ? frame : frame + mask + 1;
    }

    /// <summary>Sets unit field <paramref name="field"/> of unit <paramref name="id"/> in the original.</summary>
    public static void Set(GameCanvas game, int field, int id, int value) =>
        game.bArr_e[(field * Parity.EntityFields.UnitStride) + id] = (sbyte)value;

    /// <summary>Sets the same field in the model.</summary>
    public static void Set(GameWorld world, int field, int id, int value) =>
        EntityProperties.Set(world, (EntityProperty)field, id, value);

    /// <summary>The original's action range of unit <paramref name="id"/>'s current action (its register i_B).</summary>
    public static int ActionRange(GameCanvas game, int id)
    {
        Legacy.World w = game.world_a;
        int range = w.bArr_b[w.sArr_a[38] + LegacySamples.Field(game, ActionField, id)];
        return range < 0 ? w.bArr_a[w.sArr_b[-range] + id] : range;
    }

    /// <summary>Sets the original's registers of a unit's turn: current unit and tribe, action range, goal distance.</summary>
    public static Legacy.World Turn(GameCanvas game, int id, int goalDistance)
    {
        Legacy.World w = Current(game, id);
        w.i_B = ActionRange(game, id);
        w.i_z = goalDistance;
        return w;
    }

    /// <summary>The goal distance of unit <paramref name="id"/> as a turn starts with it.</summary>
    public static int GoalDistance(GameCanvas game, int id)
    {
        GameWorld world = Parity.LegacyRules.Import(game, LegacySamples.Content);
        return new Simulation.RuleSystems(world).UnitGoals.Measure(world.Unit(id));
    }

    /// <summary>Sets <paramref name="world"/>'s random state so that its next masked roll satisfies <paramref name="wanted"/>.</summary>
    public static void NextRoll(GameWorld world, int mask, int modulus, System.Func<int, bool> wanted)
    {
        for (long state = 1; ; state++)
        {
            var probe = new GameRandom(0) { State = state };
            if (wanted(probe.NextMasked(mask, modulus)))
            {
                world.Random.State = state;
                return;
            }
        }
    }

    /// <summary>Makes <paramref name="id"/> the original's current unit (and its tribe the current tribe).</summary>
    public static Legacy.World Current(GameCanvas game, int id)
    {
        Legacy.World w = game.world_a;
        w.i_v = id;
        w.i_u = (id - 1) / 50;
        return w;
    }
}

/// <summary>Sets the same unit fields in the original or in the model, for a comparison's two sides.</summary>
internal sealed class UnitFields
{
    private readonly Legacy.GameCanvas? game;
    private readonly GameWorld? world;
    private readonly int unit;

    public UnitFields(Legacy.GameCanvas game, int unit) => (this.game, this.unit) = (game, unit);

    public UnitFields(GameWorld world, int unit) => (this.world, this.unit) = (world, unit);

    /// <summary>Sets field <paramref name="field"/> of the unit; returns this for chaining.</summary>
    public UnitFields Set(int field, int value)
    {
        if (game is not null)
        {
            UnitSampling.Set(game, field, unit, value);
        }
        else
        {
            UnitSampling.Set(world!, field, unit, value);
        }
        return this;
    }
}
