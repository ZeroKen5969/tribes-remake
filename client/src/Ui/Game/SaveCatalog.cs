using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;
using Tribes.Client.App;

namespace Tribes.Client.Ui.Game;

/// <summary>What a save slot says about its save, kept next to it as JSON.</summary>
public sealed record SaveInfo
{
    /// <summary>Format name of the description file.</summary>
    public string Format { get; init; } = SaveCatalog.Format;

    /// <summary>The match kind: "campaign", "custom", "versus" or "endless".</summary>
    public required string Mode { get; init; }

    /// <summary>Slot: "auto" or "1".."5".</summary>
    public required string Slot { get; init; }

    /// <summary>Mission number, or -1 (endless worlds).</summary>
    public int Mission { get; init; } = -1;

    /// <summary>Difficulty 0 or 1.</summary>
    public int Difficulty { get; init; }

    /// <summary>When it was saved (UTC, ISO 8601).</summary>
    public DateTime SavedAt { get; init; }

    /// <summary>Time played in the match, in seconds.</summary>
    public long PlaySeconds { get; init; }

    /// <summary>A world name or other text to show instead of the mission name, or empty.</summary>
    public string Title { get; init; } = "";

    /// <summary>The settings the match or endless world was started with; null for the campaign and older saves.</summary>
    public Tribes.Core.World.WorldSettings? Settings { get; init; }

    /// <summary>Absolute path of the save data (filled when read).</summary>
    public string DataPath { get; init; } = "";
}

/// <summary>
/// The saved games in user://saves/: for each slot a data file <c>&lt;mode&gt;-&lt;slot&gt;.save</c>
/// written by the match layer (its own format) and a description <c>&lt;mode&gt;-&lt;slot&gt;.json</c>
/// (<see cref="SaveInfo"/>, camelCase JSON). Slots per mode: "auto" (autosave) and "1".."5".
/// </summary>
public static class SaveCatalog
{
    /// <summary>Format name of the description files.</summary>
    public const string Format = "tribes-save-info/1";

    /// <summary>Where saves live.</summary>
    public const string Folder = "user://saves";

    /// <summary>The manual slots of each mode.</summary>
    public const int ManualSlots = 5;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The mode name used in file names for <paramref name="mode"/>.</summary>
    public static string ModeName(MatchMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>All saves, newest first.</summary>
    public static IReadOnlyList<SaveInfo> List() => ListIn(Folder);

    /// <summary>All saves kept in <paramref name="path"/> (a res://, user:// or absolute folder), newest first.</summary>
    public static IReadOnlyList<SaveInfo> ListIn(string path)
    {
        var saves = new List<SaveInfo>();
        using DirAccess? folder = DirAccess.Open(path);
        if (folder is null)
        {
            return saves;
        }
        foreach (string file in folder.GetFiles())
        {
            if (!file.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }
            string stem = file[..^5];
            string dataPath = $"{path}/{stem}.save";
            if (!FileAccess.FileExists(dataPath))
            {
                continue;
            }
            try
            {
                SaveInfo? info = JsonSerializer.Deserialize<SaveInfo>(FileAccess.GetFileAsString($"{path}/{file}"), Json);
                if (info is not null)
                {
                    saves.Add(info with { DataPath = ProjectSettings.GlobalizePath(dataPath) });
                }
            }
            catch (JsonException e)
            {
                GD.PushWarning($"save description {file} ignored: {e.Message}");
            }
        }
        saves.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return saves;
    }

    /// <summary>The saves of <paramref name="mode"/>, newest first.</summary>
    public static IReadOnlyList<SaveInfo> List(MatchMode mode)
    {
        var result = new List<SaveInfo>();
        foreach (SaveInfo save in List())
        {
            if (save.Mode == ModeName(mode))
            {
                result.Add(save);
            }
        }
        return result;
    }

    /// <summary>Writes a save: <paramref name="data"/> and its description (for the match layer).</summary>
    public static void Write(SaveInfo info, byte[] data)
    {
        DirAccess.MakeDirRecursiveAbsolute(Folder);
        string stem = $"{Folder}/{info.Mode}-{info.Slot}";
        using (FileAccess file = FileAccess.Open(stem + ".save", FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                throw new InvalidOperationException($"cannot write {stem}.save: {FileAccess.GetOpenError()}");
            }
            file.StoreBuffer(data);
        }
        using FileAccess description = FileAccess.Open(stem + ".json", FileAccess.ModeFlags.Write);
        description?.StoreString(JsonSerializer.Serialize(info with { DataPath = "" }, Json));
    }

    /// <summary>Deletes a save and its description.</summary>
    public static void Delete(SaveInfo info)
    {
        string stem = $"{Folder}/{info.Mode}-{info.Slot}";
        // The match screen keeps a picture of each save next to it (<stem>.png).
        foreach (string path in new[] { stem + ".save", stem + ".json", stem + ".png" })
        {
            if (FileAccess.FileExists(path))
            {
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
            }
        }
    }

    /// <summary>
    /// Where the picture of <paramref name="save"/> is: next to its data, as a PNG with the same
    /// name (the match screen writes it).
    /// </summary>
    public static string ThumbnailPath(SaveInfo save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.DataPath.EndsWith(".save", StringComparison.Ordinal)
            ? save.DataPath[..^5] + ".png"
            : $"{Folder}/{save.Mode}-{save.Slot}.png";
    }

    /// <summary>"12:05" style play time.</summary>
    public static string PlayTime(long seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", time.Minutes, time.Seconds);
    }
}
