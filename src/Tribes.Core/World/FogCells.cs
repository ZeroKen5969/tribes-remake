using System;
using System.Runtime.InteropServices;

namespace Tribes.Core.World;

/// <summary>Bulk operations on fog flags kept one per cell.</summary>
internal static class FogCells
{
    /// <summary>
    /// Sets every flag of <paramref name="target"/> that is set in <paramref name="source"/> (same
    /// length); true when any flag of <paramref name="target"/> changed.
    /// </summary>
    public static bool Or(Span<bool> target, ReadOnlySpan<bool> source)
    {
        if (source.Length != target.Length)
        {
            throw new ArgumentException("the flags differ in length", nameof(source));
        }
        // A bool is one byte holding 0 or 1, so eight flags are or-ed at once as one word.
        Span<ulong> targetWords = MemoryMarshal.Cast<bool, ulong>(target);
        ReadOnlySpan<ulong> sourceWords = MemoryMarshal.Cast<bool, ulong>(source);
        ulong added = 0;
        for (int i = 0; i < targetWords.Length; i++)
        {
            added |= sourceWords[i] & ~targetWords[i];
            targetWords[i] |= sourceWords[i];
        }
        for (int i = targetWords.Length * sizeof(ulong); i < target.Length; i++)
        {
            added |= source[i] && !target[i] ? 1UL : 0UL;
            target[i] |= source[i];
        }
        return added != 0;
    }
}
