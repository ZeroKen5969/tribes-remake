using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>One string of the game.</summary>
/// <param name="Id">Index in its table, as the game refers to it.</param>
/// <param name="Text">
/// The string as Unicode: line breaks as <c>\n</c>, accented letters composed, control codes as
/// <c>{N}</c> (0..22 inline icons, 23..30 value rows) and <c>{name}</c> (replaced by the game version).
/// </param>
/// <param name="Raw">
/// The string as stored, one character per byte (the original font's spelling: <c>&amp;</c> line
/// break, <c>=</c> narrow backspace, accent bytes after their letter), with the same <c>{N}</c> and
/// <c>{name}</c> markup.
/// </param>
public sealed record GameString(int Id, string Text, string Raw);

/// <summary>All strings of one language (<c>data/client/strings/&lt;language&gt;.json</c>).</summary>
/// <param name="Format">Format name and version.</param>
/// <param name="Language">Language tag (en, fr, it, de, es, zh-TW).</param>
/// <param name="Name">The language's own name.</param>
/// <param name="Folder">Folder of the original resources (0..5).</param>
/// <param name="S">Table <c>s</c>: menus, buildings, weapons, names (file <c>s</c>).</param>
/// <param name="T">Table <c>t</c> entries 0..70: descriptions and hints (file <c>t</c>).</param>
/// <param name="D">Table <c>t</c> entries from 71: tutorial and story (file <c>d0</c>), keyed by their <c>t</c> index.</param>
public sealed record StringTables(string Format, string Language, string Name, int Folder, IReadOnlyList<GameString> S,
    IReadOnlyList<GameString> T, IReadOnlyList<GameString> D)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-strings/1";
}
