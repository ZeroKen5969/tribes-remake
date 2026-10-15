using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// Collects the comparisons of one parity test: each runs an original method and its rewrite from
/// the same sampled state and compares the whole rule state (and the results, when there are any).
/// </summary>
internal sealed class ParityRun
{
    private const int KeptFailures = 8;

    private readonly List<string> failures = new();
    private readonly Dictionary<string, int> perCase = new(StringComparer.Ordinal);

    /// <summary>Comparisons made.</summary>
    public int Compared { get; private set; }

    /// <summary>Comparisons that differed.</summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Compares <paramref name="original"/> run on the sample's game with <paramref name="rewritten"/>
    /// run on a world imported from it (with its rule systems).
    /// </summary>
    public void Check(LegacySample sample, string what, Action<GameCanvas> original, Action<RuleSystems> rewritten,
        IEnumerable<string>? ignore = null)
    {
        GameCanvas game = sample.Restore();
        IReadOnlyList<string> diff = StepComparison.Compare(game, LegacySamples.Content, original,
            world => rewritten(new RuleSystems(world)), ignore);
        Count(what);
        if (diff.Count > 0)
        {
            Fail(sample, what, diff);
        }
    }

    /// <summary>
    /// Like <see cref="Check(LegacySample, string, Action{GameCanvas}, Action{RuleSystems}, IEnumerable{string})"/>
    /// from the sampled state changed by <paramref name="prepare"/> (run on the original before both sides start).
    /// </summary>
    public void CheckPrepared(LegacySample sample, string what, Action<GameCanvas> prepare, Action<GameCanvas> original,
        Action<RuleSystems> rewritten)
    {
        GameCanvas game = sample.Restore();
        prepare(game);
        IReadOnlyList<string> diff = StepComparison.Compare(game, LegacySamples.Content, original,
            world => rewritten(new RuleSystems(world)));
        Count(what);
        if (diff.Count > 0)
        {
            Fail(sample, what, diff);
        }
    }

    /// <summary>
    /// Like <see cref="Check"/> for methods with a result: the results must agree too.
    /// </summary>
    public void Check<T>(LegacySample sample, string what, Func<GameCanvas, T> original, Func<RuleSystems, T> rewritten,
        IEnumerable<string>? ignore = null)
    {
        T expected = default!;
        T actual = default!;
        GameCanvas game = sample.Restore();
        IReadOnlyList<string> diff = StepComparison.Compare(game, LegacySamples.Content, g => expected = original(g),
            world => actual = rewritten(new RuleSystems(world)), ignore);
        Count(what);
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            diff = diff.Prepend($"result: expected {expected}, actual {actual}").ToList();
        }
        if (diff.Count > 0)
        {
            Fail(sample, what, diff);
        }
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

    private void Count(string what)
    {
        Compared++;
        string key = what.Split(' ')[0];
        perCase[key] = perCase.GetValueOrDefault(key) + 1;
    }

    private void Fail(LegacySample sample, string what, IReadOnlyList<string> diff)
    {
        Failed++;
        if (failures.Count < KeptFailures)
        {
            failures.Add($"{sample} {what}:\n  " + string.Join("\n  ", diff));
        }
    }
}

/// <summary>Helpers for picking test inputs from a sampled state.</summary>
internal static class SampleInputs
{
    /// <summary>At most <paramref name="count"/> items spread evenly over <paramref name="items"/>.</summary>
    public static IEnumerable<T> Spread<T>(IEnumerable<T> items, int count)
    {
        var list = items.ToList();
        if (list.Count <= count)
        {
            return list;
        }
        return Enumerable.Range(0, count).Select(i => list[i * list.Count / count]);
    }

    /// <summary>
    /// <paramref name="count"/> items of <paramref name="items"/> from a position that rotates with
    /// <paramref name="seed"/>, so different samples try different items.
    /// </summary>
    public static int[] Pick(int[] items, int seed, int count) =>
        items.Length == 0
            ? items
            : Enumerable.Range(0, Math.Min(count, items.Length)).Select(i => items[(seed + i) % items.Length]).ToArray();

    /// <summary>The model unit of the same slot in an imported world.</summary>
    public static Unit Unit(RuleSystems systems, int id) => systems.World.Unit(id);
}
