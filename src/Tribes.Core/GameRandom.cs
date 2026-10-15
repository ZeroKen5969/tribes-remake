namespace Tribes.Core;

/// <summary>
/// The random number generator of the game rules. It produces the same sequence as
/// <c>java.util.Random</c>, which the original game used, so the rules can be checked against it.
/// Only rules code draws from it; anything visual uses its own generator, otherwise what is on
/// screen would change the course of a game (and break network play, where every player runs
/// the same rules).
/// </summary>
public sealed class GameRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Increment = 0xBL;
    private const long StateMask = (1L << 48) - 1;

    private long state;

    /// <summary>Starts the sequence that <c>new java.util.Random(seed)</c> starts.</summary>
    public GameRandom(long seed)
    {
        state = (seed ^ Multiplier) & StateMask;
    }

    /// <summary>
    /// The 48-bit internal state, for saving a game and for taking over the state of the
    /// original game's generator.
    /// </summary>
    public long State
    {
        get => state;
        set => state = value & StateMask;
    }

    /// <summary>The next value over the whole int range.</summary>
    public int NextInt() => Next(32);

    /// <summary>
    /// <c>(NextInt() &amp; mask) % modulus</c>, the original game's way of picking a value: the
    /// mask keeps the result non-negative and the modulus bounds it.
    /// </summary>
    public int NextMasked(int mask, int modulus) => (NextInt() & mask) % modulus;

    private int Next(int bits)
    {
        state = unchecked((state * Multiplier) + Increment) & StateMask;
        return (int)(state >>> (48 - bits));
    }
}
