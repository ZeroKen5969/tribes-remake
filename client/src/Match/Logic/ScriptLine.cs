using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// One command of a scripted interface run ("--match-script"): a command word and its arguments,
/// separated by spaces or commas. Empty lines and lines starting with # are skipped.
/// </summary>
/// <param name="Command">The command word, lower case.</param>
/// <param name="Args">The arguments as written.</param>
public sealed record ScriptLine(string Command, IReadOnlyList<string> Args)
{
    /// <summary>The commands of <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<ScriptLine> ParseAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<ScriptLine>();
        foreach (string raw in text.Split('\n'))
        {
            if (Parse(raw) is { } line)
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    /// <summary>The command on one line, or null for an empty line or a comment.</summary>
    public static ScriptLine? Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string text = raw.Trim();
        int comment = text.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0)
        {
            text = text[..comment].TrimEnd();
        }
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }
        string[] parts = text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return new ScriptLine(parts[0].ToLowerInvariant(), parts[1..]);
    }

    /// <summary>Argument <paramref name="i"/> as a whole number.</summary>
    /// <exception cref="FormatException">It is missing or not a number.</exception>
    public int Int(int i) =>
        i < Args.Count && int.TryParse(Args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new FormatException($"{Command}: argument {i + 1} must be a whole number");

    /// <summary>Arguments <paramref name="i"/> and <paramref name="i"/> + 1 as a point.</summary>
    /// <exception cref="FormatException">They are missing or not numbers.</exception>
    public Vector2 Point(int i) =>
        i + 1 < Args.Count
        && float.TryParse(Args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
        && float.TryParse(Args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            ? new Vector2(x, y) : throw new FormatException($"{Command}: arguments {i + 1} and {i + 2} must be a point");

    /// <inheritdoc/>
    public override string ToString() => Args.Count == 0 ? Command : Command + " " + string.Join(' ', Args);
}
