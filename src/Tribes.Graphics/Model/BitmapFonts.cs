using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>One row of glyphs on a font sheet.</summary>
/// <param name="Row">Row number (0..2).</param>
/// <param name="Top">Source y of the row.</param>
/// <param name="Height">Height of the row's glyphs.</param>
public sealed record GlyphRow(int Row, int Top, int Height);

/// <summary>One glyph of a bitmap font.</summary>
/// <param name="Code">Glyph code: <c>row * 30 + column</c>.</param>
/// <param name="X">Source x on the sheet.</param>
/// <param name="Width">Width; the pen then moves by <c>Width + Spacing</c>.</param>
/// <param name="IsAccent">
/// An accent (row 0, column 14 and up): drawn over the previous glyph, centred on it and raised by
/// <paramref name="Raise"/>, without moving the pen.
/// </param>
/// <param name="Raise">For accents, how far it is drawn above the line (negative: below).</param>
/// <param name="FitsSheet">Whether the glyph lies on the sheet; glyphs that do not are not drawn.</param>
public sealed record Glyph(int Code, int X, int Width, bool IsAccent, int Raise, bool FitsSheet);

/// <summary>One of the original bitmap fonts.</summary>
/// <param name="Id">0 large letters, 1 small digits.</param>
/// <param name="Sheet">Sheet id.</param>
/// <param name="Spacing">Extra pixels after each glyph (equal to the font number).</param>
/// <param name="ColorVariants">
/// Number of colour versions stacked on the sheet, each <c>Rows[0].Height</c> pixels below the
/// previous (the small digits: yellow, red, cyan); 1 for the large font.
/// </param>
/// <param name="Rows">The glyph rows.</param>
/// <param name="Glyphs">The glyphs by code.</param>
public sealed record BitmapFont(int Id, string Sheet, int Spacing, int ColorVariants, IReadOnlyList<GlyphRow> Rows,
    IReadOnlyList<Glyph> Glyphs);

/// <summary>A character of the string files and how it is written.</summary>
/// <param name="Text">The Unicode text it stands for.</param>
/// <param name="Bytes">The bytes in the string files (a letter followed by an accent byte, for accented letters).</param>
public sealed record CharacterSpelling(string Text, string Bytes);

/// <summary>
/// The original text rendering (<c>data/client/fonts.json</c>): every byte of a string file is
/// turned into a glyph code with <see cref="GlyphByByte"/>, then drawn with one of the fonts.
/// </summary>
/// <param name="Format">Format name and version.</param>
/// <param name="GlyphByByte">Glyph code of each string-file byte 0..127.</param>
/// <param name="NewlineCode">Glyph code of a line break.</param>
/// <param name="NarrowBackspaceCode">Glyph code that moves the pen back by <c>Spacing + 2</c> pixels.</param>
/// <param name="FirstIconCode">Glyph codes from here are inline icons: sprite <c>FirstIconSprite + code - FirstIconCode</c>.</param>
/// <param name="FirstIconSprite">Sprite of the first inline icon.</param>
/// <param name="IconWidth">Width an inline icon takes in a line.</param>
/// <param name="FirstValueRowCode">Glyph codes from here are value rows (two numbers at the edges of the box).</param>
/// <param name="LineHeight">Line spacing of paragraphs.</param>
/// <param name="Fonts">The fonts.</param>
/// <param name="Spellings">How the string files write the characters that are not plain ASCII.</param>
public sealed record BitmapFontCatalog(string Format, IReadOnlyList<int> GlyphByByte, int NewlineCode, int NarrowBackspaceCode,
    int FirstIconCode, int FirstIconSprite, int IconWidth, int FirstValueRowCode, int LineHeight,
    IReadOnlyList<BitmapFont> Fonts, IReadOnlyList<CharacterSpelling> Spellings)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-bitmap-fonts/1";
}
