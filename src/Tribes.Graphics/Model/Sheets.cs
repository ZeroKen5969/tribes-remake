using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>Which of the original image files a sheet comes from.</summary>
public enum SheetPack
{
    /// <summary>Pack <c>pi0</c>: the sprite sheets 0..26 (25 and 26 are the bitmap fonts).</summary>
    Sprites,

    /// <summary>Pack <c>pi9</c>: recoloured copies of sheets 5, 9, 14 and 15 for tribe and appearance variants.</summary>
    Variants,

    /// <summary>Pack <c>pi8</c>: the story illustrations.</summary>
    Illustrations,

    /// <summary>Files <c>l0</c>..<c>l3</c>: the start-up splash screens.</summary>
    Splash,

    /// <summary>File <c>icon.png</c>: the application icon.</summary>
    Icon,
}

/// <summary>One image of the original game, exported unchanged to <c>client/assets/original</c>.</summary>
/// <param name="Id">Name used to refer to the image; also the file name without extension (<c>pi0_05</c>).</param>
/// <param name="File">Path relative to <c>client/assets</c> (<c>original/pi0_05.png</c>).</param>
/// <param name="Pack">The original file the image comes from.</param>
/// <param name="Index">Position of the image in its pack.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record SheetInfo(string Id, string File, SheetPack Pack, int Index, int Width, int Height);

/// <summary>
/// A set of sheet replacements applied while one part of a villager is drawn: the part keeps its
/// source rectangles but takes the pixels from a recoloured copy of the sheet.
/// </summary>
/// <param name="Id">Variant number (0..5), as chosen by the appearance rules.</param>
/// <param name="Substitutions">The sheets this variant replaces.</param>
public sealed record PaletteVariant(int Id, IReadOnlyList<SheetSubstitution> Substitutions);

/// <summary>One sheet replaced by a recoloured copy.</summary>
/// <param name="Sheet">Number of the replaced sprite sheet (0..26).</param>
/// <param name="Replacement">Id of the sheet drawn instead (a <see cref="SheetPack.Variants"/> sheet).</param>
public sealed record SheetSubstitution(int Sheet, string Replacement);
