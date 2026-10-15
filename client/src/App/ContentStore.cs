using System.Collections.Generic;
using Godot;
using Tribes.Core.Content;

namespace Tribes.Client.App;

/// <summary>
/// Loads the game content packed with the game (res://content, possibly replaced by a downloaded
/// content pack). The rules library reads plain files, and packed files are not plain files on
/// every platform, so the content is copied to the user data folder first, into a folder of its
/// own (<see cref="ExtractedContent"/>) so that files an older version left behind are never read.
/// </summary>
public static class ContentStore
{
    private const string PackedRoot = "res://content";
    private const string ExtractedRoot = "user://content";

    /// <summary>Copies the packed content out and loads it.</summary>
    public static GameContent Load()
    {
        var files = new List<(string Name, byte[] Bytes)>();
        ReadTree(PackedRoot, "", files);
        if (files.Count == 0)
        {
            throw new System.InvalidOperationException($"no game content found in {PackedRoot}");
        }
        string folder = ExtractedContent.Prepare(ProjectSettings.GlobalizePath(ExtractedRoot), files);
        return GameContent.Load(folder);
    }

    /// <summary>Adds every file under <paramref name="from"/> to <paramref name="files"/>, named relative to the content root.</summary>
    private static void ReadTree(string from, string prefix, List<(string Name, byte[] Bytes)> files)
    {
        using DirAccess? source = DirAccess.Open(from);
        if (source == null)
        {
            return;
        }
        var directories = new List<string>(source.GetDirectories());
        foreach (string file in source.GetFiles())
        {
            // Exported games list imported files with a ".remap" or ".import" suffix; the content
            // is plain text, so only real names are read.
            if (file.EndsWith(".import", System.StringComparison.Ordinal))
            {
                continue;
            }
            string name = file.EndsWith(".remap", System.StringComparison.Ordinal) ? file[..^6] : file;
            using FileAccess input = FileAccess.Open($"{from}/{name}", FileAccess.ModeFlags.Read);
            if (input == null)
            {
                continue;
            }
            files.Add((prefix + name, input.GetBuffer((long)input.GetLength())));
        }
        foreach (string directory in directories)
        {
            ReadTree($"{from}/{directory}", $"{prefix}{directory}/", files);
        }
    }
}
