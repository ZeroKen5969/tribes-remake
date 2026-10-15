using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Tribes.Client.Platform;

/// <summary>
/// The version of this build, from <c>res://version.json</c> (client/version.json), the single
/// place where the version is set; the build scripts copy it into the export presets and the
/// project settings. Shape: <c>{"version": "1.2.0", "versionCode": 3, "contentRevision": 0}</c>.
/// </summary>
/// <param name="Version">App version (<c>X.Y.Z</c> or <c>X.Y.Z-label</c>), as in the release tags.</param>
/// <param name="VersionCode">Android version code; every released APK needs a larger one.</param>
/// <param name="ContentRevision">The content revision built into this app (see docs/UPDATES.md).</param>
public sealed record BuildInfo(string Version, int VersionCode, int ContentRevision)
{
    /// <summary>Where the version file is inside the game.</summary>
    public const string FilePath = "res://version.json";

    private static BuildInfo? current;

    /// <summary>The running build, read on first use.</summary>
    public static BuildInfo Current => current ??= Load();

    /// <summary>Reads <see cref="FilePath"/>; falls back to the project version when the file is missing or broken.</summary>
    public static BuildInfo Load()
    {
        string text = Godot.FileAccess.GetFileAsString(FilePath);
        try
        {
            if (text.Length > 0)
            {
                return Parse(text);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            GD.PushWarning($"{FilePath} is invalid ({e.Message}); using the project version.");
        }

        string version = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
        return new BuildInfo(version, 0, 0);
    }

    /// <summary>Reads the version file's text.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="KeyNotFoundException">A field is missing.</exception>
    /// <exception cref="InvalidOperationException">A field has the wrong type.</exception>
    public static BuildInfo Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string version = root.GetProperty("version").GetString() ?? throw new InvalidOperationException("version is null");
        return new BuildInfo(version, root.GetProperty("versionCode").GetInt32(), root.GetProperty("contentRevision").GetInt32());
    }
}
