namespace Tribes.Client.World.Scene;

/// <summary>
/// java.util.Random: the original's presentation-only generator (the jitter of floating numbers),
/// so the numbers wobble exactly as in the original.
/// </summary>
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    /// <summary>Creates a generator seeded like <c>new Random(seed)</c>.</summary>
    public JavaRandom(long seed)
    {
        State = (seed ^ Multiplier) & Mask;
    }

    /// <summary>The 48-bit internal state.</summary>
    public long State { get; set; }

    /// <summary><c>nextInt()</c>.</summary>
    public int NextInt() => Next(32);

    private int Next(int bits)
    {
        State = ((State * Multiplier) + Addend) & Mask;
        return (int)((ulong)State >> (48 - bits));
    }
}
