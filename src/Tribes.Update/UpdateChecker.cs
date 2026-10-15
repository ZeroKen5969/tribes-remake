using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Update;

/// <summary>What the installed game is, for deciding whether a release is an update.</summary>
public sealed class UpdateCheckOptions
{
    /// <summary>The running app version.</summary>
    public required AppVersion CurrentAppVersion { get; init; }

    /// <summary>The content revision in use: the built-in one or the installed pack's, whichever is newer.</summary>
    public required int InstalledContentRevision { get; init; }

    /// <summary>The platform the game runs on (<see cref="UpdatePlatform.Android"/> or <see cref="UpdatePlatform.Windows"/>).</summary>
    public required UpdatePlatform Platform { get; init; }

    /// <summary>Whether releases marked as pre-release count (testers).</summary>
    public bool IncludePrereleases { get; init; }
}

/// <summary>The kind of update found.</summary>
public enum UpdateKind
{
    /// <summary>The game is up to date.</summary>
    None,

    /// <summary>A content pack: downloaded, then loaded at the next start.</summary>
    Content,

    /// <summary>A new app: APK on Android, zip on Windows.</summary>
    App,
}

/// <summary>A release whose manifest was downloaded and its signature checked.</summary>
/// <param name="Release">The GitHub release.</param>
/// <param name="Manifest">The verified manifest.</param>
public sealed record VerifiedRelease(GitHubRelease Release, UpdateManifest Manifest);

/// <summary>The answer to "is there an update?".</summary>
public sealed class UpdateCheckResult
{
    /// <summary>The kind of update; <see cref="UpdateKind.None"/> when up to date.</summary>
    public required UpdateKind Kind { get; init; }

    /// <summary>The release the update comes from (null when none).</summary>
    public VerifiedRelease? Release { get; init; }

    /// <summary>The file to download, as the signed manifest describes it.</summary>
    public ManifestAsset? Asset { get; init; }

    /// <summary>The same file as the release lists it (for its download id).</summary>
    public GitHubAsset? RemoteAsset { get; init; }

    /// <summary>
    /// True when newer content exists but needs a newer app that is not offered for this platform
    /// yet; the client can tell the player to update the game by hand.
    /// </summary>
    public bool NewerContentNeedsNewerApp { get; init; }

    /// <summary>The release notes (zh-TW), empty when there is no update.</summary>
    public string Notes => Release?.Manifest.Notes ?? "";

    /// <summary>The version the update brings, or null.</summary>
    public ReleaseTag? Tag => Release?.Manifest.Tag;

    /// <summary>An up-to-date answer.</summary>
    public static UpdateCheckResult UpToDate(bool newerContentNeedsNewerApp = false) =>
        new() { Kind = UpdateKind.None, NewerContentNeedsNewerApp = newerContentNeedsNewerApp };
}

/// <summary>
/// Finds the newest applicable update among the repository's releases. Only signed manifests are
/// believed: a release counts when its manifest verifies against the trusted keys and matches its tag.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>How many candidate releases (newest first) are examined at most.</summary>
    public const int MaxCandidates = 3;

    private readonly GitHubReleaseClient client;
    private readonly TrustedKeySet keys;

    /// <summary>Creates a checker.</summary>
    public UpdateChecker(GitHubReleaseClient client, TrustedKeySet keys)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(keys);
        this.client = client;
        this.keys = keys;
    }

    /// <summary>
    /// Looks for an update. Releases are taken newest first by tag; drafts, pre-releases (unless
    /// asked for), releases without a manifest and releases that cannot be newer are skipped.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<GitHubRelease> releases = await client.ListReleasesAsync(cancellationToken).ConfigureAwait(false);
        var installed = new ReleaseTag(options.CurrentAppVersion, Math.Max(0, options.InstalledContentRevision));
        IEnumerable<(GitHubRelease Release, ReleaseTag Tag)> candidates = releases
            .Where(r => !r.Draft && (options.IncludePrereleases || !r.Prerelease))
            .Where(r => r.FindAsset(UpdateManifest.FileName) is not null && r.FindAsset(UpdateManifest.SignatureFileName) is not null)
            .Select(r => (Release: r, Tag: ReleaseTag.TryParse(r.TagName)))
            .Where(c => c.Tag is not null && (c.Tag.Version > installed.Version || c.Tag.ContentRevision > installed.ContentRevision))
            .Select(c => (c.Release, c.Tag!))
            .OrderByDescending(c => c.Item2)
            .Take(MaxCandidates);

        bool contentNeedsApp = false;
        UpdateException? firstFailure = null;
        foreach ((GitHubRelease release, ReleaseTag _) in candidates)
        {
            VerifiedRelease verified;
            try
            {
                verified = await FetchVerifiedAsync(release, cancellationToken).ConfigureAwait(false);
            }
            catch (UpdateException e) when (e.Kind is UpdateErrorKind.BadSignature or UpdateErrorKind.InvalidManifest)
            {
                // A broken release must not hide the valid ones after it; it is reported if nothing else applies.
                firstFailure ??= e;
                continue;
            }

            UpdateCheckResult result = Decide(options, verified);
            if (result.Kind != UpdateKind.None)
            {
                return result;
            }

            contentNeedsApp |= result.NewerContentNeedsNewerApp;
        }

        return firstFailure is null ? UpdateCheckResult.UpToDate(contentNeedsApp) : throw firstFailure;
    }

    /// <summary>
    /// Downloads and verifies a release's manifest: signed by a trusted key, and describing the
    /// release it is attached to (same tag, every listed file present with the listed size).
    /// </summary>
    public async Task<VerifiedRelease> FetchVerifiedAsync(GitHubRelease release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        GitHubAsset manifestAsset = release.FindAsset(UpdateManifest.FileName)
            ?? throw new UpdateException(UpdateErrorKind.BadSignature, $"Release {release.TagName} has no manifest.");
        GitHubAsset signatureAsset = release.FindAsset(UpdateManifest.SignatureFileName)
            ?? throw new UpdateException(UpdateErrorKind.BadSignature, $"Release {release.TagName} has an unsigned manifest.");

        byte[] manifestBytes = await client.DownloadAssetBytesAsync(manifestAsset, ManifestJson.MaxBytes, cancellationToken).ConfigureAwait(false);
        byte[] signature = await client.DownloadAssetBytesAsync(signatureAsset, ManifestSignature.MaxBytes, cancellationToken).ConfigureAwait(false);

        // Nothing in the manifest is read before its signature is checked.
        ManifestSignature.Verify(manifestBytes, signature, keys);
        UpdateManifest manifest = ManifestJson.Parse(manifestBytes);
        if (manifest.Tag.ToString() != release.TagName)
        {
            throw new UpdateException(UpdateErrorKind.InvalidManifest, $"The manifest is for {manifest.Tag}, the release is {release.TagName}.");
        }

        foreach (ManifestAsset asset in manifest.Assets)
        {
            GitHubAsset? remote = release.FindAsset(asset.Name);
            if (remote is null || !remote.IsUploaded || remote.Size != asset.Size)
            {
                throw new UpdateException(UpdateErrorKind.InvalidManifest, $"Release {release.TagName} does not have {asset.Name} as the manifest lists it.");
            }
        }

        return new VerifiedRelease(release, manifest);
    }

    /// <summary>
    /// The decision for one verified release: an app update when its app version is newer and it
    /// has an app for the platform; otherwise a content update when its content revision is newer,
    /// it has a pack, and the running app is at least the pack's minimum version. A labelled
    /// (pre-release) version in the signed manifest counts only with <see cref="UpdateCheckOptions.IncludePrereleases"/>,
    /// whatever the release's pre-release flag on GitHub says.
    /// </summary>
    public static UpdateCheckResult Decide(UpdateCheckOptions options, VerifiedRelease release)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(release);
        UpdateManifest manifest = release.Manifest;
        if (manifest.AppVersion.IsPrerelease && !options.IncludePrereleases)
        {
            return UpdateCheckResult.UpToDate();
        }

        ManifestAsset? app = manifest.FindAsset(AssetKind.App, options.Platform);
        if (app is not null && manifest.AppVersion > options.CurrentAppVersion)
        {
            return Found(UpdateKind.App, release, app);
        }

        ManifestAsset? content = manifest.FindAsset(AssetKind.Content, options.Platform);
        if (content is not null && manifest.ContentRevision > options.InstalledContentRevision)
        {
            return options.CurrentAppVersion >= manifest.MinAppVersion
                ? Found(UpdateKind.Content, release, content)
                : UpdateCheckResult.UpToDate(newerContentNeedsNewerApp: true);
        }

        return UpdateCheckResult.UpToDate();
    }

    private static UpdateCheckResult Found(UpdateKind kind, VerifiedRelease release, ManifestAsset asset) => new()
    {
        Kind = kind,
        Release = release,
        Asset = asset,
        RemoteAsset = release.Release.FindAsset(asset.Name),
    };
}
