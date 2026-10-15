using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>Comparisons between exported rule values and the original's table values.</summary>
internal static class RuleCheck
{
    /// <summary>Asserts <paramref name="actual"/> equals the original's <paramref name="expected"/>.</summary>
    public static void Same(int expected, int actual, string where) =>
        Assert.True(expected == actual, $"{where}: original {expected}, exported {actual}");

    /// <summary>Asserts two sequences are equal.</summary>
    public static void Same(IEnumerable<int> expected, IEnumerable<int> actual, string where)
    {
        int[] e = expected.ToArray();
        int[] a = actual.ToArray();
        Assert.True(e.SequenceEqual(a), $"{where}: original [{string.Join(",", e)}], exported [{string.Join(",", a)}]");
    }

    /// <summary>A property as the original's field number, or <paramref name="none"/> for null.</summary>
    public static int Number(EntityProperty? property, int none) => property is EntityProperty p ? (int)p : none;

    /// <summary>The original's encoding of a value source: a constant, or -field.</summary>
    public static int Encoded(ValueSource source)
    {
        Assert.True(source.Constant.HasValue != source.Property.HasValue, "a value source holds exactly one of constant and property");
        return source.Constant ?? -(int)source.Property!.Value;
    }

    /// <summary>The original's encoding of a rate target: a unit field, or -counter.</summary>
    public static int Encoded(RateTarget target)
    {
        Assert.True(target.Property.HasValue != target.Counter.HasValue, "a rate target holds exactly one of property and counter");
        return target.Property is EntityProperty p ? (int)p : -(int)target.Counter!.Value;
    }

    /// <summary>A rectangle as the original stores it.</summary>
    public static int[] Encoded(CellRect r) => new[] { r.Left, r.Top, r.Right, r.Bottom };
}
