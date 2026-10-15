using System;
using System.Collections.Generic;
using System.Linq;

namespace Tribes.Update;

/// <summary>The platform an asset is for.</summary>
public enum UpdatePlatform
{
    /// <summary>Every platform (content packs).</summary>
    Any,

    /// <summary>Android (APK).</summary>
    Android,

    /// <summary>Windows (zip of the install directory).</summary>
    Windows,
}

/// <summary>What an asset updates.</summary>
public enum AssetKind
{
    /// <summary>The whole app: an APK or a Windows zip.</summary>
    App,

    /// <summary>A Godot resource pack with data and assets only.</summary>
    Content,
}

/// <summary>One file of a release, as the signed manifest describes it.</summary>
/// <param name="Name">The release asset name, also the file name on disk.</param>
/// <param name="Platform">The platform it is for.</param>
/// <param name="Kind">What it updates.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">SHA-256 of the file, 64 lowercase hex digits.</param>
public sealed record ManifestAsset(string Name, UpdatePlatform Platform, AssetKind Kind, long Size, string Sha256);

/// <summary>
/// The signed description of a release (<c>manifest.json</c>): versions, release notes and the
/// size and SHA-256 of every file. The client trusts nothing about a release that is not in here.
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>The schema version this library writes and reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The release asset name of the manifest.</summary>
    public const string FileName = "manifest.json";

    /// <summary>The release asset name of the manifest signature.</summary>
    public const string SignatureFileName = "manifest.json.sig";

    /// <summary>Creates a manifest; throws <see cref="ArgumentException"/> when it is inconsistent.</summary>
    public UpdateManifest(AppVersion appVersion, int contentRevision, AppVersion minAppVersion, string notes, IEnumerable<ManifestAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(appVersion);
        ArgumentNullException.ThrowIfNull(minAppVersion);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentOutOfRangeException.ThrowIfNegative(contentRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contentRevision, ReleaseTag.MaxContentRevision);

        ManifestAsset[] list = assets.ToArray();
        ValidateAssets(list);
        AppVersion = appVersion;
        ContentRevision = contentRevision;
        MinAppVersion = minAppVersion;
        Notes = notes;
        Assets = list;
    }

    /// <summary>The app version of the release.</summary>
    public AppVersion AppVersion { get; }

    /// <summary>The content revision of the release.</summary>
    public int ContentRevision { get; }

    /// <summary>The oldest app version the content pack works with.</summary>
    public AppVersion MinAppVersion { get; }

    /// <summary>Release notes for the player, zh-TW.</summary>
    public string Notes { get; }

    /// <summary>The files of the release.</summary>
    public IReadOnlyList<ManifestAsset> Assets { get; }

    /// <summary>The tag the release must have.</summary>
    public ReleaseTag Tag => new(AppVersion, ContentRevision);

    /// <summary>
    /// The asset of a kind for a platform: the one made for that platform, otherwise one for any
    /// platform; null when there is none.
    /// </summary>
    public ManifestAsset? FindAsset(AssetKind kind, UpdatePlatform platform) =>
        Assets.FirstOrDefault(a => a.Kind == kind && a.Platform == platform)
        ?? Assets.FirstOrDefault(a => a.Kind == kind && a.Platform == UpdatePlatform.Any);

    /// <summary>The standard asset name of an APK.</summary>
    public static string AndroidAppName(AppVersion version) => $"PrehistoricTribes-{version}-android.apk";

    /// <summary>The standard asset name of a Windows zip.</summary>
    public static string WindowsAppName(AppVersion version) => $"PrehistoricTribes-{version}-windows.zip";

    /// <summary>The standard asset name of a content pack.</summary>
    public static string ContentPackName(AppVersion version, int contentRevision) =>
        FormattableString.Invariant($"content-{version}-r{contentRevision}.pck");

    /// <summary>
    /// Whether a name is safe as a file name on every platform: ASCII letters, digits, '.', '-' and
    /// '_', not starting with '.', at most 128 characters.
    /// </summary>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128 || name[0] == '.')
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a text is a SHA-256 in lowercase hex.</summary>
    public static bool IsSha256Hex(string? text)
    {
        if (text is null || text.Length != 64)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateAssets(ManifestAsset[] assets)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new HashSet<(AssetKind, UpdatePlatform)>();
        foreach (ManifestAsset asset in assets)
        {
            ArgumentNullException.ThrowIfNull(asset, nameof(assets));
            if (!IsSafeFileName(asset.Name) || asset.Name is FileName or SignatureFileName)
            {
                throw new ArgumentException($"Asset name '{asset.Name}' is not allowed.", nameof(assets));
            }

            if (asset.Size < 0 || !IsSha256Hex(asset.Sha256))
            {
                throw new ArgumentException($"Asset '{asset.Name}' needs a size and a lowercase SHA-256.", nameof(assets));
            }

            if (!Enum.IsDefined(asset.Kind) || !Enum.IsDefined(asset.Platform)
                || (asset.Kind == AssetKind.App && asset.Platform == UpdatePlatform.Any))
            {
                throw new ArgumentException($"Asset '{asset.Name}' has an invalid kind or platform.", nameof(assets));
            }

            if (!names.Add(asset.Name) || !slots.Add((asset.Kind, asset.Platform)))
            {
                throw new ArgumentException($"Asset '{asset.Name}' is listed twice.", nameof(assets));
            }
        }
    }
}
