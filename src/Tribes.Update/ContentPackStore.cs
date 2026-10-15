using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tribes.Update;

/// <summary>The content pack recorded as installed.</summary>
/// <param name="ContentRevision">Its content revision.</param>
/// <param name="FileName">Its file name inside the store directory.</param>
/// <param name="AppVersion">The app version of the release it came from.</param>
/// <param name="MinAppVersion">The oldest app version it works with.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">Its SHA-256, lowercase hex.</param>
public sealed record InstalledContentPack(int ContentRevision, string FileName, AppVersion AppVersion, AppVersion MinAppVersion, long Size, string Sha256);

/// <summary>
/// The downloaded content packs and which one is in use, kept in a directory under the app's user
/// data (the Godot client passes the global path of <c>user://updates/content</c>). At start the
/// client asks <see cref="GetPackToLoad"/> for the pack to load with
/// <c>ProjectSettings.LoadResourcePack</c>, before any game scene is loaded.
/// </summary>
public sealed class ContentPackStore
{
    /// <summary>The state file inside the directory.</summary>
    public const string StateFileName = "content-state.json";

    /// <summary>Creates a store over a directory (created when needed).</summary>
    public ContentPackStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory = Path.GetFullPath(directory);
    }

    /// <summary>The store directory.</summary>
    public string Directory { get; }

    private string StatePath => Path.Combine(Directory, StateFileName);

    /// <summary>Where a pack is downloaded to.</summary>
    public string PathFor(ManifestAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Kind != AssetKind.Content || !UpdateManifest.IsSafeFileName(asset.Name))
        {
            throw new ArgumentException("Not a content pack asset.", nameof(asset));
        }

        return Path.Combine(Directory, asset.Name);
    }

    /// <summary>The recorded pack, or null when there is none or the record is unreadable.</summary>
    public InstalledContentPack? ReadState()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(StatePath));
            JsonElement root = document.RootElement;
            string file = Json.RequiredString(root, "file");
            string sha = Json.RequiredString(root, "sha256");
            if (!UpdateManifest.IsSafeFileName(file) || !UpdateManifest.IsSha256Hex(sha)
                || !AppVersion.TryParse(Json.RequiredString(root, "appVersion"), out AppVersion? app)
                || !AppVersion.TryParse(Json.RequiredString(root, "minAppVersion"), out AppVersion? min))
            {
                return null;
            }

            return new InstalledContentPack(Json.RequiredInt(root, "contentRevision"), file, app, min, Json.RequiredLong(root, "size"), sha);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            return null; // A damaged record means "no pack": the built-in content is used.
        }
    }

    /// <summary>
    /// Records a verified pack, already downloaded to <see cref="PathFor"/>, as the one to load from
    /// the next start on. The record is replaced atomically.
    /// </summary>
    public InstalledContentPack Install(UpdateManifest manifest, ManifestAsset asset)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string path = PathFor(asset);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != asset.Size)
        {
            throw new UpdateException(UpdateErrorKind.IntegrityMismatch, $"{asset.Name} is not where it was downloaded to.");
        }

        var pack = new InstalledContentPack(manifest.ContentRevision, asset.Name, manifest.AppVersion, manifest.MinAppVersion, asset.Size, asset.Sha256);
        WriteState(pack);
        return pack;
    }

    /// <summary>
    /// The full path of the pack to load, or null for the built-in content: the recorded pack is
    /// used when it is newer than the built-in content, the app is at least its minimum version,
    /// and the file is there with its size (and SHA-256 with <paramref name="verifyHash"/>).
    /// </summary>
    public string? GetPackToLoad(AppVersion currentAppVersion, int builtInContentRevision, bool verifyHash = false) =>
        LoadablePack(currentAppVersion, builtInContentRevision, verifyHash) is InstalledContentPack pack
            ? Path.Combine(Directory, pack.FileName)
            : null;

    /// <summary>The content revision in use: the loadable pack's, or the built-in one.</summary>
    public int GetCurrentRevision(AppVersion currentAppVersion, int builtInContentRevision) =>
        LoadablePack(currentAppVersion, builtInContentRevision, verifyHash: false)?.ContentRevision ?? builtInContentRevision;

    private InstalledContentPack? LoadablePack(AppVersion currentAppVersion, int builtInContentRevision, bool verifyHash)
    {
        ArgumentNullException.ThrowIfNull(currentAppVersion);
        InstalledContentPack? pack = ReadState();
        if (pack is null || pack.ContentRevision <= builtInContentRevision || currentAppVersion < pack.MinAppVersion)
        {
            return null;
        }

        string path = Path.Combine(Directory, pack.FileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != pack.Size)
            {
                return null;
            }

            if (verifyHash)
            {
                using FileStream stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return pack;
    }

    /// <summary>Forgets the recorded pack (e.g. after it failed to load), so the next start uses the built-in content.</summary>
    public void Uninstall()
    {
        try
        {
            File.Delete(StatePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "removing the content pack record");
        }
    }

    /// <summary>
    /// Deletes packs and partial downloads other than the recorded pack. Call at start before
    /// loading a pack (a loaded pack stays open and cannot be deleted on Windows). Returns the
    /// files that could not be deleted; they are tried again next time.
    /// </summary>
    public IReadOnlyList<string> RemoveStaleFiles()
    {
        var failed = new List<string>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return failed;
        }

        string? keep = ReadState()?.FileName;
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failed.Add(Directory);
            return failed;
        }

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            bool stale = (name.EndsWith(".pck", StringComparison.OrdinalIgnoreCase) && name != keep)
                || name.EndsWith(UpdateDownloader.PartSuffix, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            if (!stale)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed.Add(path);
            }
        }

        return failed;
    }

    private void WriteState(InstalledContentPack pack)
    {
        string temp = StatePath + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            using (FileStream stream = File.Create(temp))
            using (var writer = new Utf8JsonWriter(stream, ManifestJson.WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("contentRevision", pack.ContentRevision);
                writer.WriteString("file", pack.FileName);
                writer.WriteString("appVersion", pack.AppVersion.ToString());
                writer.WriteString("minAppVersion", pack.MinAppVersion.ToString());
                writer.WriteNumber("size", pack.Size);
                writer.WriteString("sha256", pack.Sha256);
                writer.WriteEndObject();
            }

            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "recording the content pack");
        }
    }
}
