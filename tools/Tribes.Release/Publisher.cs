using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;

namespace Tribes.Release;

/// <summary>A file of the release: a payload, the manifest or its signature.</summary>
internal sealed record PlannedFile(string Name, string LocalPath, long Size, string Sha256, string ContentType);

/// <summary>Everything a publish run uploads, computed before any request.</summary>
internal sealed record ReleasePlan(
    ReleaseTag Tag,
    bool Prerelease,
    string Title,
    string Notes,
    byte[] ManifestBytes,
    string KeyId,
    IReadOnlyList<PlannedFile> Payloads,
    IReadOnlyList<PlannedFile> ManifestFiles)
{
    public IEnumerable<PlannedFile> AllFiles => Payloads.Concat(ManifestFiles);
}

/// <summary>
/// publish: hashes the files, writes and signs the manifest, then creates a draft release, uploads
/// the files (payloads first, the manifest and its signature last) and publishes it. Re-running
/// continues an unfinished draft and does nothing for a release already published identically.
/// </summary>
internal static class Publisher
{
    private const int UploadAttempts = 3;

    public static async Task<int> RunAsync(Options options, CancellationToken ct)
    {
        ReleasePlan plan = BuildPlan(options, TrustedKeySet.LoadEmbedded());
        Out.Line($"Release {plan.Tag}{(plan.Prerelease ? " (pre-release)" : "")}, signed with key {plan.KeyId}");
        foreach (PlannedFile file in plan.AllFiles)
        {
            Out.Line($"  {file.Name,-44} {file.Size,12:N0} B  sha256 {file.Sha256}");
        }

        Out.Line($"Manifest and signature written to {Path.GetDirectoryName(plan.ManifestFiles[0].LocalPath)}");
        if (options.Flag("dry-run"))
        {
            Out.Line("Dry run: nothing was sent to GitHub.");
            return 0;
        }

        GitHubSecrets secrets = GitHubSecrets.Load(RepoPaths.SecretsFile(options));
        string writeToken = secrets.RequireWriteToken();
        using HttpClientPair http = new();
        var admin = new GitHubAdmin(http.Upload, secrets.Repository, writeToken);
        var reader = new GitHubReleaseClient(http.Api, secrets.Repository, writeToken);
        GitHubRelease release = await PublishAsync(admin, reader, plan, ct);
        Out.Line($"Published release id {release.Id}: https://github.com/{secrets.Repository}/releases/tag/{release.TagName}");
        Out.Line($"Check it as the game would: dotnet run --project tools/Tribes.Release -- verify --tag {release.TagName}");
        return 0;
    }

    public static ReleasePlan BuildPlan(Options options, TrustedKeySet trusted)
    {
        AppVersion version = AppVersion.TryParse(options.Require("version"), out AppVersion? parsed)
            ? parsed
            : throw new ToolException("--version must be MAJOR.MINOR.PATCH[-label].");
        int revision = options.GetInt("content-rev", 0);
        if (revision > ReleaseTag.MaxContentRevision)
        {
            throw new ToolException($"--content-rev must be at most {ReleaseTag.MaxContentRevision}.");
        }
        AppVersion minApp = options.Get("min-app-version") is string min
            ? (AppVersion.TryParse(min, out AppVersion? m) ? m : throw new ToolException("--min-app-version is not a version."))
            : version;
        bool prerelease = options.Flag("prerelease");
        if (version.IsPrerelease && !prerelease)
        {
            throw new ToolException($"{version} has a label; publish it with --prerelease so players do not get it.");
        }

        var payloads = new List<(PlannedFile File, ManifestAsset Asset)>();
        AddPayload(payloads, options.Get("apk"), UpdateManifest.AndroidAppName(version), UpdatePlatform.Android, AssetKind.App, "application/vnd.android.package-archive");
        AddPayload(payloads, options.Get("windows-zip"), UpdateManifest.WindowsAppName(version), UpdatePlatform.Windows, AssetKind.App, "application/zip");
        if (options.Get("pck") is not null && revision < 1)
        {
            throw new ToolException("A content pack needs --content-rev 1 or higher.");
        }

        AddPayload(payloads, options.Get("pck"), UpdateManifest.ContentPackName(version, revision), UpdatePlatform.Any, AssetKind.Content, "application/octet-stream");
        if (payloads.Count == 0)
        {
            throw new ToolException("Give at least one of --apk, --windows-zip, --pck.");
        }

        string notes = File.ReadAllText(options.Require("notes"), Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var manifest = new UpdateManifest(version, revision, minApp, notes, payloads.Select(p => p.Asset));
        byte[] manifestBytes = ManifestJson.Serialize(manifest);
        using ECDsa key = SigningKeys.Load(RepoPaths.SigningKey(options));
        string keyId = TrustedKey.FromKey(key).KeyId;
        if (trusted.Find(keyId) is null)
        {
            throw new ToolException($"Key {keyId} is not among the keys the game trusts; run keygen and rebuild first.");
        }

        byte[] signature = ManifestSignature.Sign(manifestBytes, key);
        ManifestSignature.Verify(manifestBytes, signature, trusted);

        string outDir = Path.GetFullPath(options.Get("out") ?? Path.Combine(Path.GetTempPath(), "tribes-release", manifest.Tag.ToString()));
        Directory.CreateDirectory(outDir);
        PlannedFile manifestFile = WriteOut(outDir, UpdateManifest.FileName, manifestBytes, "application/json");
        PlannedFile signatureFile = WriteOut(outDir, UpdateManifest.SignatureFileName, signature, "application/octet-stream");
        return new ReleasePlan(
            manifest.Tag,
            prerelease,
            options.Get("title") ?? manifest.Tag.ToString(),
            notes,
            manifestBytes,
            keyId,
            payloads.Select(p => p.File).ToArray(),
            [manifestFile, signatureFile]);
    }

    public static async Task<GitHubRelease> PublishAsync(GitHubAdmin admin, GitHubReleaseClient reader, ReleasePlan plan, CancellationToken ct)
    {
        string tag = plan.Tag.ToString();
        GitHubRelease[] sameTag = (await admin.ListAllReleasesAsync(ct)).Where(r => r.TagName == tag).ToArray();
        GitHubRelease? existing = sameTag.FirstOrDefault(r => !r.Draft) ?? sameTag.FirstOrDefault();
        if (existing is not null && !existing.Draft)
        {
            if (await IsSameAsync(reader, existing, plan, ct))
            {
                Out.Line($"Release {tag} is already published with these files; nothing to do.");
                return existing;
            }

            throw new ToolException($"Release {tag} is already published with other files. Publish a new version or content revision instead.");
        }

        GitHubRelease release = existing is null
            ? await admin.CreateDraftAsync(tag, plan.Title, plan.Notes, plan.Prerelease, ct)
            : await admin.UpdateReleaseAsync(existing.Id, new Dictionary<string, object> { ["name"] = plan.Title, ["body"] = plan.Notes, ["prerelease"] = plan.Prerelease }, ct);
        Out.Line(existing is null ? $"Created draft release id {release.Id}." : $"Continuing draft release id {release.Id}.");

        var wanted = plan.AllFiles.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (GitHubAsset stray in release.Assets.Where(a => !wanted.Contains(a.Name)))
        {
            Out.Line($"  removing {stray.Name} (not part of this release)");
            await admin.DeleteAssetAsync(stray.Id, ct);
        }

        foreach (PlannedFile file in plan.Payloads)
        {
            GitHubAsset? remote = release.FindAsset(file.Name);
            if (remote is not null && remote.IsUploaded && remote.Size == file.Size && remote.Sha256 == file.Sha256)
            {
                Out.Line($"  {file.Name}: already uploaded");
                continue;
            }

            release = await UploadAsync(admin, release, file, ct);
        }

        // The signature is new on every run (ECDSA is randomized), so both are always replaced.
        foreach (PlannedFile file in plan.ManifestFiles)
        {
            release = await UploadAsync(admin, release, file, ct);
        }

        var publish = new Dictionary<string, object> { ["draft"] = false };
        if (!plan.Prerelease)
        {
            publish["make_latest"] = "legacy";
        }

        return await admin.UpdateReleaseAsync(release.Id, publish, ct);
    }

    private static async Task<bool> IsSameAsync(GitHubReleaseClient reader, GitHubRelease published, ReleasePlan plan, CancellationToken ct)
    {
        if (plan.AllFiles.Any(f => published.FindAsset(f.Name) is not { IsUploaded: true } remote || remote.Size != f.Size && f.Name != UpdateManifest.SignatureFileName))
        {
            return false;
        }

        byte[] manifest = await reader.DownloadAssetBytesAsync(published.FindAsset(UpdateManifest.FileName)!, ManifestJson.MaxBytes, ct);
        return manifest.AsSpan().SequenceEqual(plan.ManifestBytes);
    }

    private static async Task<GitHubRelease> UploadAsync(GitHubAdmin admin, GitHubRelease release, PlannedFile file, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            GitHubAsset? old = release.FindAsset(file.Name);
            if (old is not null)
            {
                await admin.DeleteAssetAsync(old.Id, ct);
            }

            try
            {
                Out.Line($"  uploading {file.Name} ({file.Size:N0} B)");
                await using FileStream stream = File.OpenRead(file.LocalPath);
                GitHubAsset uploaded = await admin.UploadAssetAsync(release, file.Name, stream, file.Size, file.ContentType, ct);
                if (uploaded.Size != file.Size || (uploaded.Sha256 is string sha && sha != file.Sha256))
                {
                    throw new ToolException($"GitHub stored {file.Name} with another size or digest.");
                }

                return await admin.GetReleaseAsync(release.Id, ct);
            }
            catch (ToolException e) when (attempt < UploadAttempts)
            {
                Out.Line($"  upload of {file.Name} failed ({e.Message}); retrying");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                release = await admin.GetReleaseAsync(release.Id, ct);
            }
        }
    }

    private static void AddPayload(List<(PlannedFile, ManifestAsset)> payloads, string? path, string name, UpdatePlatform platform, AssetKind kind, string contentType)
    {
        if (path is null)
        {
            return;
        }

        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new ToolException($"{full} does not exist.");
        }

        long size = new FileInfo(full).Length;
        string sha;
        using (FileStream stream = File.OpenRead(full))
        {
            sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        payloads.Add((new PlannedFile(name, full, size, sha, contentType), new ManifestAsset(name, platform, kind, size, sha)));
    }

    private static PlannedFile WriteOut(string dir, string name, byte[] bytes, string contentType)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return new PlannedFile(name, path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), contentType);
    }
}

/// <summary>A client for API reads (redirects followed by hand) and one with a long timeout for uploads.</summary>
internal sealed class HttpClientPair : IDisposable
{
    public System.Net.Http.HttpClient Api { get; } = UpdateHttp.CreateClient();

    public System.Net.Http.HttpClient Upload { get; } = new(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromHours(1),
    };

    public void Dispose()
    {
        Api.Dispose();
        Upload.Dispose();
    }
}
