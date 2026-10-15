using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// The parity runner of the unit work tests: like <see cref="ParityRun"/>, but a sampled state can
/// first be prepared with the original's own data (a unit put into an action, a target placed in
/// front of it) so the rare moments of a unit's work are compared too.
/// </summary>
internal sealed class UnitWorkParity
{
    private const int KeptFailures = 8;

    private readonly List<string> failures = new();
    private readonly Dictionary<string, int> perCase = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> failedPerCase = new(StringComparer.Ordinal);

    /// <summary>Comparisons made.</summary>
    public int Compared { get; private set; }

    /// <summary>Comparisons that differed.</summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Restores <paramref name="sample"/>, applies <paramref name="prepare"/> (null: none) and
    /// compares <paramref name="original"/> with <paramref name="rewritten"/> from that state.
    /// </summary>
    public void Check(LegacySample sample, string what, Action<GameCanvas>? prepare, Action<GameCanvas> original,
        Action<RuleSystems> rewritten, IEnumerable<string>? ignore = null)
    {
        GameCanvas game = sample.Restore();
        prepare?.Invoke(game);
        IReadOnlyList<string> diff = StepComparison.Compare(game, LegacySamples.Content, original,
            world => rewritten(new RuleSystems(world)), ignore);
        Record(sample, what, diff);
    }

    /// <summary>Like <see cref="Check"/> for methods with a result: the results must agree too.</summary>
    public void Check<T>(LegacySample sample, string what, Action<GameCanvas>? prepare, Func<GameCanvas, T> original,
        Func<RuleSystems, T> rewritten, IEnumerable<string>? ignore = null)
    {
        T expected = default!;
        T actual = default!;
        GameCanvas game = sample.Restore();
        prepare?.Invoke(game);
        IReadOnlyList<string> diff = StepComparison.Compare(game, LegacySamples.Content, g => expected = original(g),
            world => actual = rewritten(new RuleSystems(world)), ignore);
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            diff = diff.Prepend($"result: expected {expected}, actual {actual}").ToList();
        }
        Record(sample, what, diff);
    }

    /// <summary>Fails the test when anything differed or fewer than <paramref name="minimum"/> comparisons ran.</summary>
    public void AssertAgrees(int minimum)
    {
        Assert.True(Failed == 0, $"{Failed} of {Compared} comparisons differed:\n" + string.Join("\n", failures));
        Assert.True(Compared >= minimum, $"only {Compared} comparisons (expected at least {minimum})");
    }

    /// <summary>How often each case was compared, for the test output.</summary>
    public override string ToString() =>
        $"{Compared} comparisons: " + string.Join(", ", perCase.Select(p => $"{p.Key} {p.Value}"));

    private void Record(LegacySample sample, string what, IReadOnlyList<string> diff)
    {
        Compared++;
        string key = what.Split(' ')[0];
        perCase[key] = perCase.GetValueOrDefault(key) + 1;
        if (diff.Count > 0)
        {
            Failed++;
            string detail = System.Text.RegularExpressions.Regex.Replace(what, @"(unit|doctor|armoury|tribe) \d+( at \d+)?", "$1");
            failedPerCase[detail] = failedPerCase.GetValueOrDefault(detail) + 1;
            if (failures.Count < KeptFailures)
            {
                failures.Add($"{sample} {what}:\n  " + string.Join("\n  ", diff));
            }
        }
    }
}

/// <summary>Reading and writing the original's unit records and the registers of a unit's turn.</summary>
internal static class LegacyUnits
{
    /// <summary>The original's field numbers the unit work tests use.</summary>
    public const int X = 0, Y = 1, OffsetX = 2, OffsetY = 3, WorkProgress = 4, Facing = 6, PathLength = 8;
    public const int StepTicks = 11, SubState = 13, Age = 14, HitPoints = 19, ActionField = 28, Animation = 29;
    public const int AnimationTick = 30, DestinationX = 31, DestinationY = 32, CarriedItem = 35, CarriedAmount = 36;
    public const int SavedAction = 39, TargetIndex = 44, BuildingSlot = 46, TargetCellCode = 47, Weapon = 65;
    public const int ProjectileStep = 58, ProjectileFlightSteps = 59, OrderMode = 61, AssignedTask = 64;

    /// <summary>Field <paramref name="field"/> of unit <paramref name="id"/>.</summary>
    public static int Get(GameCanvas game, int field, int id) => game.bArr_e[UnitBase + (field * UnitStride) + id];

    /// <summary>Sets field <paramref name="field"/> of unit <paramref name="id"/>.</summary>
    public static void Set(GameCanvas game, int field, int id, int value) =>
        game.bArr_e[UnitBase + (field * UnitStride) + id] = unchecked((sbyte)value);

    /// <summary>The tribe of unit <paramref name="id"/>.</summary>
    public static int TribeOf(int id) => (id - 1) / 50;

    /// <summary>
    /// Sets the registers the original's unit loop has set when unit <paramref name="id"/>'s turn
    /// gets going: the current unit and tribe, and its action's range.
    /// </summary>
    public static void BeginTurn(GameCanvas game, int id)
    {
        Tribes.Legacy.World w = game.world_a;
        w.i_v = id;
        w.i_u = TribeOf(id);
        w.i_B = Range(game, id);
    }

    /// <summary>The range of unit <paramref name="id"/>'s action, as the original's unit loop works it out.</summary>
    public static int Range(GameCanvas game, int id)
    {
        Tribes.Legacy.World w = game.world_a;
        int range = w.bArr_b[w.sArr_a[38] + Get(game, ActionField, id)];
        return range < 0 ? w.bArr_a[w.sArr_b[-range] + id] : range;
    }

    /// <summary>
    /// What the original's goal measurement (a_III_I for a unit) gives for unit <paramref name="id"/>,
    /// worked out without its side effect (an attacker of a building takes the nearest footprint cell
    /// as its destination): the value a unit's turn starts with.
    /// </summary>
    public static int GoalDistance(GameCanvas game, int id)
    {
        Tribes.Legacy.World w = game.world_a;
        int target = Get(game, TargetIndex, id);
        if (target < -1)
        {
            return 0;
        }
        int field = w.bArr_b[w.sArr_a[28] + Get(game, ActionField, id)];
        int x = Get(game, DestinationX, id), y = Get(game, DestinationY, id);
        if (target >= 1 && field != 43)
        {
            x = game.bArr_e[w.sArr_b[field] + target];
            y = game.bArr_e[w.sArr_b[field + 1] + target];
            if (field == 83)
            {
                int kind = game.bArr_e[8383 + (6 * 61) + target];
                x = Clamp(Get(game, X, id), x, x + w.bArr_b[w.sArr_a[138] + 104 + kind] - 1);
                y = Clamp(Get(game, Y, id), y, y + w.bArr_b[w.sArr_a[138] + 130 + kind] - 1);
            }
        }
        int dx = unchecked((sbyte)(x - Get(game, X, id))), dy = unchecked((sbyte)(y - Get(game, Y, id)));
        return dx >= -12 && dy >= -12 && dx <= 12 && dy <= 12 ? w.bArr_b[w.sArr_a[53] + ((12 + dy) * 25) + 12 + dx] : 200;
    }

    /// <summary>
    /// Sets the registers of unit <paramref name="id"/>'s turn (<see cref="BeginTurn"/>) and the goal
    /// distance it starts with.
    /// </summary>
    public static void BeginMeasuredTurn(GameCanvas game, int id)
    {
        BeginTurn(game, id);
        game.world_a.i_z = GoalDistance(game, id);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value < max ? value : max;

    /// <summary>At most <paramref name="count"/> live units of the sample, spread over the slots (both tribes).</summary>
    public static int[] Some(LegacySample sample, int count) =>
        SampleInputs.Spread(LegacySamples.LiveUnits(sample.Restore()), count).ToArray();

    /// <summary>The first live unit of the other tribe than <paramref name="id"/>'s, or 0.</summary>
    public static int EnemyOf(GameCanvas game, int id) =>
        LegacySamples.LiveUnits(game).FirstOrDefault(e => TribeOf(e) != TribeOf(id) && game.bArr2_c[Get(game, Y, e)][Get(game, X, e)] == e,
            LegacySamples.LiveUnits(game).FirstOrDefault(e => TribeOf(e) != TribeOf(id)));

    /// <summary>Lets the player see cell (<paramref name="x"/>, <paramref name="y"/>) (explored and in sight).</summary>
    public static void Reveal(GameCanvas game, int x, int y)
    {
        foreach (int[][] layer in game.iArr3_a)
        {
            layer[x / 32][y] |= 1 << (x % 32);
        }
    }

    /// <summary>Live units of the sampled state.</summary>
    public static IEnumerable<int> Live(GameCanvas game) => LegacySamples.LiveUnits(game);
}
