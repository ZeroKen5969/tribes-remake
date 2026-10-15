using System.Numerics;

namespace Tribes.Core.Maps.Generation;

/// <summary>A set of terrain tiles (0..255) as 256 bits.</summary>
internal struct TileSet
{
    /// <summary>Tiles a set can hold.</summary>
    public const int Capacity = 256;

    private ulong w0, w1, w2, w3;

    /// <summary>The set of every tile.</summary>
    public static TileSet All => new() { w0 = ulong.MaxValue, w1 = ulong.MaxValue, w2 = ulong.MaxValue, w3 = ulong.MaxValue };

    /// <summary>The set of only <paramref name="tile"/>.</summary>
    public static TileSet Of(int tile)
    {
        TileSet set = default;
        set.Add(tile);
        return set;
    }

    /// <summary>Whether the set is empty.</summary>
    public readonly bool IsEmpty => (w0 | w1 | w2 | w3) == 0;

    /// <summary>Number of tiles in the set.</summary>
    public readonly int Count => BitOperations.PopCount(w0) + BitOperations.PopCount(w1) + BitOperations.PopCount(w2) + BitOperations.PopCount(w3);

    /// <summary>Whether <paramref name="tile"/> is in the set.</summary>
    public readonly bool Contains(int tile) => (Word(tile >> 6) & (1UL << (tile & 63))) != 0;

    /// <summary>Adds <paramref name="tile"/>.</summary>
    public void Add(int tile)
    {
        ulong bit = 1UL << (tile & 63);
        switch (tile >> 6)
        {
            case 0: w0 |= bit; break;
            case 1: w1 |= bit; break;
            case 2: w2 |= bit; break;
            default: w3 |= bit; break;
        }
    }

    /// <summary>The tiles in both sets.</summary>
    public static TileSet operator &(TileSet a, TileSet b) => new() { w0 = a.w0 & b.w0, w1 = a.w1 & b.w1, w2 = a.w2 & b.w2, w3 = a.w3 & b.w3 };

    /// <summary>The tiles in either set.</summary>
    public static TileSet operator |(TileSet a, TileSet b) => new() { w0 = a.w0 | b.w0, w1 = a.w1 | b.w1, w2 = a.w2 | b.w2, w3 = a.w3 | b.w3 };

    /// <summary>Bits <c>8 * index</c> .. <c>8 * index + 7</c> of the set (tiles of that number as a byte).</summary>
    public readonly int ByteAt(int index) => (int)((Word(index >> 3) >> ((index & 7) << 3)) & 0xFF);

    /// <summary>Whether two sets hold the same tiles.</summary>
    public readonly bool SameAs(TileSet other) => w0 == other.w0 && w1 == other.w1 && w2 == other.w2 && w3 == other.w3;

    /// <summary>The smallest tile of the set, or -1 when it is empty.</summary>
    public readonly int First()
    {
        for (int word = 0; word < 4; word++)
        {
            ulong bits = Word(word);
            if (bits != 0)
            {
                return (word << 6) + BitOperations.TrailingZeroCount(bits);
            }
        }
        return -1;
    }

    /// <summary>The smallest tile of the set above <paramref name="tile"/>, or -1.</summary>
    public readonly int Next(int tile)
    {
        int from = tile + 1;
        for (int word = from >> 6; word < 4; word++)
        {
            ulong bits = Word(word);
            if (word == from >> 6)
            {
                bits &= ulong.MaxValue << (from & 63);
            }
            if (bits != 0)
            {
                return (word << 6) + BitOperations.TrailingZeroCount(bits);
            }
        }
        return -1;
    }

    private readonly ulong Word(int index) => index switch { 0 => w0, 1 => w1, 2 => w2, _ => w3 };
}
