using System.Collections;
using System.Collections.Generic;

namespace Tribes.Core.World;

/// <summary>
/// A read-only view of one of the world's lists. Walking it with <c>foreach</c> makes no garbage
/// (the rules walk the tribes and the shared tables many times a frame).
/// </summary>
/// <typeparam name="T">The items.</typeparam>
public sealed class ListView<T> : IReadOnlyList<T>
{
    private readonly List<T> items;

    internal ListView(List<T> items) => this.items = items;

    /// <inheritdoc />
    public int Count => items.Count;

    /// <inheritdoc />
    public T this[int index] => items[index];

    /// <summary>An enumerator over the items (a struct: <c>foreach</c> allocates nothing).</summary>
    public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
}
