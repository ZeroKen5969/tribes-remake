using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Tribes.Core.Commands;

/// <summary>
/// The units an order is for, by unit slot, in the order the player picked them (the rules go
/// through them in this order). Immutable, without repeats, and equal to another set with the
/// same slots in the same order.
/// </summary>
public sealed class UnitSet : IReadOnlyList<int>, IEquatable<UnitSet>
{
    /// <summary>Most units one order may name.</summary>
    public const int MaxCount = 255;

    private readonly int[] units;

    /// <summary>A set of <paramref name="units"/>, in their order.</summary>
    /// <exception cref="ArgumentException">A slot is named twice, or more than <see cref="MaxCount"/> are named.</exception>
    public UnitSet(IEnumerable<int> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        this.units = units.ToArray();
        if (this.units.Length > MaxCount)
        {
            throw new ArgumentException($"an order names at most {MaxCount} units", nameof(units));
        }
        if (this.units.Distinct().Count() != this.units.Length)
        {
            throw new ArgumentException("a unit is named twice", nameof(units));
        }
    }

    /// <summary>A set of the given units.</summary>
    public static UnitSet Of(params int[] units) => new(units);

    /// <summary>Whether <paramref name="units"/> could form a set: at most <see cref="MaxCount"/> slots, none twice.</summary>
    public static bool IsValid(IReadOnlyCollection<int> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        return units.Count <= MaxCount && units.Distinct().Count() == units.Count;
    }

    /// <inheritdoc />
    public int Count => units.Length;

    /// <inheritdoc />
    public int this[int index] => units[index];

    /// <inheritdoc />
    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)units).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(UnitSet? other) => other is not null && units.AsSpan().SequenceEqual(other.units);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as UnitSet);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int unit in units)
        {
            hash.Add(unit);
        }
        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => "[" + string.Join(", ", units) + "]";
}
