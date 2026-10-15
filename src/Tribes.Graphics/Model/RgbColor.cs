using System;
using System.Globalization;

namespace Tribes.Graphics.Model;

/// <summary>An opaque colour; written to JSON as <c>#rrggbb</c>.</summary>
/// <param name="Value">The colour as <c>0xRRGGBB</c>.</param>
public readonly record struct RgbColor(int Value)
{
    private const int ChannelMask = 0xFF;
    private const int RedShift = 16;
    private const int GreenShift = 8;
    private const int HexDigits = 6;

    /// <summary>Red channel, 0..255.</summary>
    public int R => (Value >> RedShift) & ChannelMask;

    /// <summary>Green channel, 0..255.</summary>
    public int G => (Value >> GreenShift) & ChannelMask;

    /// <summary>Blue channel, 0..255.</summary>
    public int B => Value & ChannelMask;

    /// <summary>Builds a colour from its three channels (each masked to 0..255).</summary>
    public static RgbColor FromChannels(int r, int g, int b) =>
        new(((r & ChannelMask) << RedShift) | ((g & ChannelMask) << GreenShift) | (b & ChannelMask));

    /// <summary>Parses <c>#rrggbb</c>.</summary>
    public static RgbColor Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != HexDigits + 1 || text[0] != '#'
            || !int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"not a #rrggbb colour: '{text}'");
        }
        return new RgbColor(value);
    }

    /// <summary>The colour as <c>#rrggbb</c>.</summary>
    public override string ToString() => "#" + Value.ToString("x6", CultureInfo.InvariantCulture);
}
