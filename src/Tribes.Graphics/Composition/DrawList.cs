using System;
using System.Collections.Generic;

namespace Tribes.Graphics.Composition;

/// <summary>The parts one composition draws.</summary>
/// <param name="Parts">The parts in drawing order, relative to the anchor.</param>
/// <param name="PaletteVariantAfter">
/// The palette variant still selected when the composition ends (<see cref="PaletteVariants.None"/>
/// normally). The original keeps the variant selected by the last villager slot it skipped and
/// applies it to whatever it draws next; pass this value on to reproduce that.
/// </param>
/// <param name="Truncated">
/// The composition referred to a missing sprite, piece, effect or pose and stopped there, as the
/// original's drawing stops at the resulting exception.
/// </param>
public sealed record DrawList(IReadOnlyList<DrawPart> Parts, int PaletteVariantAfter, bool Truncated)
{
    /// <summary>Nothing drawn.</summary>
    public static DrawList Empty { get; } = new(Array.Empty<DrawPart>(), PaletteVariants.None, false);
}

/// <summary>Palette variant numbers.</summary>
public static class PaletteVariants
{
    /// <summary>No variant: parts are drawn from their own sheets.</summary>
    public const int None = -1;
}

/// <summary>Collects parts while a composition runs, with the palette variant currently selected.</summary>
internal sealed class PartBuffer
{
    private readonly List<DrawPart> parts = new();

    internal PartBuffer(int paletteVariant)
    {
        PaletteVariant = paletteVariant;
    }

    /// <summary>The variant applied to image pieces drawn now.</summary>
    internal int PaletteVariant { get; set; }

    internal IReadOnlyList<DrawPart> Parts => parts;

    internal void Add(DrawPart part) => parts.Add(part);

    internal DrawList ToDrawList(bool truncated) => new(parts.ToArray(), PaletteVariant, truncated);
}

/// <summary>
/// Thrown inside a composition when it refers to something missing; the public entry points
/// catch it and return what was drawn so far, marked <see cref="DrawList.Truncated"/>.
/// </summary>
internal sealed class DrawingStoppedException : Exception
{
    public DrawingStoppedException()
    {
    }

    public DrawingStoppedException(string message)
        : base(message)
    {
    }

    public DrawingStoppedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
