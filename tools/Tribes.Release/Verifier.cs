using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;

namespace Tribes.Release;

/// <summary>
/// verify, check and delete. verify and check use the read token and the library's code path
/// (the same one the game runs), with the keys built into the library.
/// </summary>
internal static class Verifier
{
    /// <summary>verify --tag T: signature, manifest, and every file downloaded and hashed.</summary>
    public static async Task<int> VerifyAsync(Options options, CancellationToken ct)
    {
        string tag = TagFrom(options);
        GitHubSecrets secrets = GitHubSecrets.Load(RepoPaths.SecretsFile(options));
        using HttpClientPair http = new();
        var client = new GitHubReleaseClient(http.Api, secrets.Repository, secrets.RequireReadToken());
        GitHubRelease release = await client.GetReleaseByTagAsync(tag, ct)
            ?? throw new ToolException($"No published release {tag} is visible with the read token.");

        VerifiedRelease verified = await new UpdateChecker(client, TrustedKeySet.LoadEmbedded()).FetchVerifiedAsync(release, ct);
        UpdateManifest manifest = verified.Manifest;
        Out.Line($"Release {release.TagName} (id {release.Id}{(release.Prerelease ? ", pre-release" : "")}): signature valid, manifest matches the release.");
        Out.Line($"  app {manifest.AppVersion}, content r{manifest.ContentRevision}, content needs app >= {manifest.MinAppVersion}");

        string dir = Path.GetFullPath(options.Get("download-dir") ?? Path.Combine(Path.GetTempPath(), "tribes-verify", release.TagName));
        bool createdDir = !Directory.Exists(dir);
        var downloaded = new List<string>();
        var downloader = new UpdateDownloader(client);
        foreach (ManifestAsset asset in manifest.Assets)
        {
            string path = Path.Combine(dir, asset.Name);
            downloaded.Add(path);
            await downloader.DownloadAsync(release.FindAsset(asset.Name)!, asset, path, null, ct);
            Out.Line($"  {asset.Name}: {asset.Size:N0} B, SHA-256 verified ({ManifestJson.PlatformName(asset.Platform)}, {asset.Kind})");
        }

        foreach (UpdatePlatform platform in new[] { UpdatePlatform.Android, UpdatePlatform.Windows })
        {
            UpdateCheckResult decision = UpdateChecker.Decide(
                new UpdateCheckOptions
                {
                    CurrentAppVersion = new AppVersion(0, 0, 0),
                    InstalledContentRevision = 0,
                    Platform = platform,
                    IncludePrereleases = release.Prerelease || manifest.AppVersion.IsPrerelease,
                },
                verified);
            Out.Line($"  a {ManifestJson.PlatformName(platform)} install of 0.0.0 would get: {Describe(decision)}");
        }

        if (!options.Flag("keep"))
        {
            // Only what this run downloaded; the folder itself only when this run created it.
            downloaded.ForEach(File.Delete);
            if (createdDir && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }

        Out.Line("Verified.");
        return 0;
    }

    /// <summary>check --current X --platform P: the decision the game would make right now.</summary>
    public static async Task<int> CheckAsync(Options options, CancellationToken ct)
    {
        AppVersion current = AppVersion.TryParse(options.Require("current"), out AppVersion? parsed)
            ? parsed
            : throw new ToolException("--current must be a version.");
        UpdatePlatform platform = ManifestJson.ParsePlatform(options.Require("platform"));
        GitHubSecrets secrets = GitHubSecrets.Load(RepoPaths.SecretsFile(options));
        using HttpClientPair http = new();
        var client = new GitHubReleaseClient(http.Api, secrets.Repository, secrets.RequireReadToken());
        UpdateCheckResult result = await new UpdateChecker(client, TrustedKeySet.LoadEmbedded()).CheckAsync(
            new UpdateCheckOptions
            {
                CurrentAppVersion = current,
                InstalledContentRevision = options.GetInt("content-rev", 0),
                Platform = platform,
                IncludePrereleases = options.Flag("prerelease"),
            },
            ct);
        Out.Line(Describe(result));
        if (result.Kind != UpdateKind.None)
        {
            Out.Line("Notes:");
            Out.Line(result.Notes);
        }

        return 0;
    }

    /// <summary>
    /// delete --tag T --release-id N --yes: deletes one release and its tag, only when the id
    /// matches the tag (so the release is the one that was meant) and, without --allow-stable,
    /// only pre-releases and drafts.
    /// </summary>
    public static async Task<int> DeleteAsync(Options options, CancellationToken ct)
    {
        string tag = TagFrom(options);
        long id = long.TryParse(options.Require("release-id"), out long parsed) ? parsed : throw new ToolException("--release-id must be a number.");
        GitHubSecrets secrets = GitHubSecrets.Load(RepoPaths.SecretsFile(options));
        using HttpClientPair http = new();
        var admin = new GitHubAdmin(http.Upload, secrets.Repository, secrets.RequireWriteToken());
        GitHubRelease[] sameTag = (await admin.ListAllReleasesAsync(ct)).Where(r => r.TagName == tag).ToArray();
        if (sameTag.Length > 1)
        {
            throw new ToolException($"{sameTag.Length} releases use the tag {tag}; delete the extra ones on GitHub first.");
        }

        GitHubRelease release = sameTag.FirstOrDefault() ?? throw new ToolException($"There is no release {tag}.");
        if (release.Id != id)
        {
            throw new ToolException($"Release {tag} has id {release.Id}, not {id}; nothing deleted.");
        }

        if (!release.Prerelease && !release.Draft && !options.Flag("allow-stable"))
        {
            throw new ToolException($"{tag} is a published stable release; pass --allow-stable to delete it.");
        }

        Out.Line($"Release {tag} (id {release.Id}, {release.Assets.Count} assets{(release.Prerelease ? ", pre-release" : "")}{(release.Draft ? ", draft" : "")})");
        if (!options.Flag("yes"))
        {
            Out.Line("Pass --yes to delete it and its tag.");
            return 0;
        }

        await admin.DeleteReleaseAsync(release.Id, ct);
        bool tagDeleted = await admin.DeleteTagAsync(tag, ct);
        Out.Line($"Deleted release {release.Id}; tag {tag} {(tagDeleted ? "deleted" : "did not exist")}.");
        return 0;
    }

    private static string TagFrom(Options options)
    {
        if (options.Get("tag") is string tag)
        {
            return ReleaseTag.TryParse(tag) is null ? throw new ToolException($"{tag} is not a release tag.") : tag;
        }

        AppVersion version = AppVersion.TryParse(options.Require("version"), out AppVersion? parsed)
            ? parsed
            : throw new ToolException("--version must be a version.");
        return new ReleaseTag(version, options.GetInt("content-rev", 0)).ToString();
    }

    private static string Describe(UpdateCheckResult result) => result.Kind switch
    {
        UpdateKind.App => $"app update to {result.Tag} ({result.Asset!.Name})",
        UpdateKind.Content => $"content update to {result.Tag} ({result.Asset!.Name})",
        _ => result.NewerContentNeedsNewerApp ? "no update (newer content needs a newer app)" : "no update",
    };
}
