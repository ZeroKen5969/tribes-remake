namespace Tribes.Core.World;

/// <summary>
/// Value ranges of the original game's storage. The original keeps most quantities in signed
/// bytes or shorts; a value stored outside the range wraps around, and some rules depend on that
/// (for example a 4-bit counter that rolls over), so the model stores such quantities the same way.
/// </summary>
public static class Wrap
{
    /// <summary><paramref name="value"/> as a signed byte would hold it (-128..127).</summary>
    public static int Signed8(int value) => unchecked((sbyte)value);

    /// <summary><paramref name="value"/> as a signed short would hold it (-32768..32767).</summary>
    public static int Signed16(int value) => unchecked((short)value);
}
