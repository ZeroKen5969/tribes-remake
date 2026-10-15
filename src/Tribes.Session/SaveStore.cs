using System;
using System.Globalization;

namespace Tribes.Session;

/// <summary>Where a save goes: the match kind and the slot ("auto" or "1".."5").</summary>
/// <param name="Header">What the save says about itself.</param>
/// <param name="Slot">The slot.</param>
public sealed record SaveSlot(SaveHeader Header, string Slot)
{
    /// <summary>The autosave slot.</summary>
    public const string Auto = "auto";

    /// <summary>Manual slots are "1" to this.</summary>
    public const int ManualSlots = 5;

    /// <summary>The mode name used in file names: "campaign", "custom", "versus" or "endless".</summary>
    public string ModeName => Header.Match.ToString().ToLowerInvariant();

    /// <summary>The file name without extension: "&lt;mode&gt;-&lt;slot&gt;".</summary>
    public string Stem => ModeName + "-" + Slot;

    /// <summary>Whether <paramref name="slot"/> is "auto" or a manual slot number.</summary>
    public static bool IsValidSlot(string? slot) =>
        slot == Auto
        || (int.TryParse(slot, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= ManualSlots
            && string.Equals(slot, n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
}

/// <summary>
/// Where the host keeps its saves (the client writes them to user://saves). Writes may come from
/// a background thread, but never two at once.
/// </summary>
public interface ISaveStore
{
    /// <summary>
    /// Writes <paramref name="data"/> (a <see cref="MatchSaveFile"/>) to <paramref name="slot"/>,
    /// replacing what was there, and removes the slot's old picture (it shows another save).
    /// </summary>
    void Write(SaveSlot slot, byte[] data);

    /// <summary>Writes the picture (a PNG) of the save now in <paramref name="slot"/>.</summary>
    void WritePicture(SaveSlot slot, byte[] png);
}

/// <summary>Settings shared by both runners.</summary>
public sealed record RunnerOptions
{
    /// <summary>This build's game version (written into saves).</summary>
    public required string GameVersion { get; init; }

    /// <summary>This build's content version (written into saves).</summary>
    public required string ContentVersion { get; init; }

    /// <summary>Where saves go; null: saving is not possible.</summary>
    public ISaveStore? Saves { get; init; }

    /// <summary>Ticks between autosaves on the host (1500 = 3 minutes at normal speed); 0 turns them off.</summary>
    public int AutosaveTicks { get; init; } = 1500;

    /// <summary>A name to keep in saves (an endless world's), or empty.</summary>
    public string Title { get; init; } = "";

    /// <summary>Where log lines go (desyncs, saves, stops); null: nowhere.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>
    /// Whether saves are written on a background thread (the state is still taken at once, at the
    /// tick boundary); false writes them before <see cref="IMatchRunner.Save"/> returns.
    /// </summary>
    public bool WriteInBackground { get; init; } = true;

    /// <summary>The clock saves are dated with.</summary>
    public Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
}
