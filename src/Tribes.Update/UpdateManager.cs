using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Update;

/// <summary>What the running game is and where updates are kept.</summary>
public sealed class UpdateManagerOptions
{
    /// <summary>The running app version.</summary>
    public required AppVersion AppVersion { get; init; }

    /// <summary>The content revision built into this app.</summary>
    public required int BuiltInContentRevision { get; init; }

    /// <summary>The platform (<see cref="UpdatePlatform.Android"/> or <see cref="UpdatePlatform.Windows"/>).</summary>
    public required UpdatePlatform Platform { get; init; }

    /// <summary>The directory for update data: the global path of <c>user://updates</c>.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Whether pre-releases count as updates (testers).</summary>
    public bool IncludePrereleases { get; init; }

    /// <summary>Needed to install Windows app updates.</summary>
    public WindowsUpdateOptions? Windows { get; init; }

    /// <summary>Needed to install Android app updates.</summary>
    public IAppInstaller? AppInstaller { get; init; }

    /// <summary>Download retry settings; defaults when null.</summary>
    public DownloadOptions? Download { get; init; }
}

/// <summary>What the client does after <see cref="UpdateManager.ApplyAsync"/>.</summary>
public enum UpdateApplyOutcome
{
    /// <summary>The content pack is installed and loads at the next start.</summary>
    RestartToApply,

    /// <summary>The Windows update is staged and the swap is waiting: quit the game now.</summary>
    QuitToFinish,

    /// <summary>The system installer was started with the APK.</summary>
    InstallerStarted,

    /// <summary>The player was sent to allow installs from this app; apply again afterwards.</summary>
    PermissionRequested,
}

/// <summary>
/// The update flow behind the "check for updates" button: <see cref="CheckAsync"/>, then
/// <see cref="DownloadAsync"/>, then <see cref="ApplyAsync"/>. Every step throws
/// <see cref="UpdateException"/> with a zh-TW <see cref="UpdateException.UserMessage"/>.
/// At start the client loads <see cref="GetContentPackToLoad"/> before any game scene.
/// </summary>
public sealed class UpdateManager : IDisposable
{
    private readonly UpdateManagerOptions options;
    private readonly UpdateChecker checker;
    private readonly UpdateDownloader downloader;
    private readonly IProcessLauncher? launcher;
    private readonly HttpClient? ownedHttp;

    /// <summary>Creates a manager over a release client; null keys means the keys built into the library.</summary>
    public UpdateManager(UpdateManagerOptions options, GitHubReleaseClient client, TrustedKeySet? keys = null, IProcessLauncher? launcher = null)
        : this(options, client, keys, launcher, ownedHttp: null)
    {
    }

    private UpdateManager(UpdateManagerOptions options, GitHubReleaseClient client, TrustedKeySet? keys, IProcessLauncher? launcher, HttpClient? ownedHttp)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(options.DataDirectory);
        this.options = options;
        this.launcher = launcher;
        this.ownedHttp = ownedHttp;
        checker = new UpdateChecker(client, keys ?? TrustedKeySet.LoadEmbedded());
        downloader = new UpdateDownloader(client, options.Download);
        ContentPacks = new ContentPackStore(Path.Combine(options.DataDirectory, "content"));
    }

    /// <summary>The downloaded content packs.</summary>
    public ContentPackStore ContentPacks { get; }

    private string AppDownloadDirectory => Path.Combine(options.DataDirectory, "app");

    /// <summary>
    /// A manager using the embedded repository, token and keys; throws
    /// <see cref="UpdateErrorKind.Unsupported"/> when the build has no update access.
    /// </summary>
    public static UpdateManager CreateDefault(UpdateManagerOptions options)
    {
        HttpClient http = UpdateHttp.CreateClient();
        try
        {
            return new UpdateManager(options, EmbeddedUpdateConfig.CreateClient(http), keys: null, launcher: null, ownedHttp: http);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The content pack to load at start, or null for the built-in content. Old packs and partial
    /// downloads are removed first.
    /// </summary>
    public string? GetContentPackToLoad(bool verifyHash = false)
    {
        foreach (string path in ContentPacks.RemoveStaleFiles())
        {
            Trace.TraceWarning("Tribes.Update: could not remove " + Path.GetFileName(path) + "; trying again at the next start.");
        }

        return ContentPacks.GetPackToLoad(options.AppVersion, options.BuiltInContentRevision, verifyHash);
    }

    /// <summary>Looks for an update for this app and its installed content.</summary>
    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        checker.CheckAsync(
            new UpdateCheckOptions
            {
                CurrentAppVersion = options.AppVersion,
                InstalledContentRevision = ContentPacks.GetCurrentRevision(options.AppVersion, options.BuiltInContentRevision),
                Platform = options.Platform,
                IncludePrereleases = options.IncludePrereleases,
            },
            cancellationToken);

    /// <summary>Downloads and verifies the update's file; returns its path.</summary>
    public Task<string> DownloadAsync(UpdateCheckResult update, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        (ManifestAsset asset, GitHubAsset remote) = Require(update);
        string path = update.Kind == UpdateKind.Content
            ? ContentPacks.PathFor(asset)
            : Path.Combine(AppDownloadDirectory, asset.Name);
        if (update.Kind == UpdateKind.App)
        {
            RemoveOtherDownloads(asset.Name);
        }

        return downloader.DownloadAsync(remote, asset, path, progress, cancellationToken);
    }

    /// <summary>
    /// Installs a downloaded update (checked again first): records a content pack, stages and
    /// schedules a Windows swap, or hands an APK to the installer.
    /// </summary>
    public async Task<UpdateApplyOutcome> ApplyAsync(UpdateCheckResult update, string verifiedFile, CancellationToken cancellationToken = default)
    {
        (ManifestAsset asset, GitHubAsset _) = Require(update);
        ArgumentException.ThrowIfNullOrEmpty(verifiedFile);
        if (!await UpdateDownloader.MatchesAsync(verifiedFile, asset, cancellationToken).ConfigureAwait(false))
        {
            throw new UpdateException(UpdateErrorKind.IntegrityMismatch, $"{asset.Name} does not match the manifest.");
        }

        UpdateManifest manifest = update.Release!.Manifest;
        if (update.Kind == UpdateKind.Content)
        {
            if (!string.Equals(Path.GetFullPath(verifiedFile), ContentPacks.PathFor(asset), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A content pack must be applied from where DownloadAsync put it.", nameof(verifiedFile));
            }

            ContentPacks.Install(manifest, asset);
            return UpdateApplyOutcome.RestartToApply;
        }

        return options.Platform switch
        {
            UpdatePlatform.Windows => await ApplyWindowsAsync(manifest, verifiedFile, cancellationToken).ConfigureAwait(false),
            UpdatePlatform.Android => ApplyAndroid(verifiedFile),
            _ => throw new UpdateException(UpdateErrorKind.Unsupported, "App updates need a platform."),
        };
    }

    /// <inheritdoc/>
    public void Dispose() => ownedHttp?.Dispose();

    private static (ManifestAsset Asset, GitHubAsset Remote) Require(UpdateCheckResult update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.Kind == UpdateKind.None || update.Asset is null || update.RemoteAsset is null || update.Release is null)
        {
            throw new ArgumentException("There is no update to download.", nameof(update));
        }

        return (update.Asset, update.RemoteAsset);
    }

    private async Task<UpdateApplyOutcome> ApplyWindowsAsync(UpdateManifest manifest, string zip, CancellationToken cancellationToken)
    {
        WindowsUpdateOptions windows = options.Windows
            ?? throw new UpdateException(UpdateErrorKind.Unsupported, "Windows update options are not set.");
        WindowsAppUpdater updater;
        try
        {
            updater = new WindowsAppUpdater(windows, launcher);
        }
        catch (ArgumentException e)
        {
            throw new UpdateException(UpdateErrorKind.Unsupported, "The Windows update options are invalid: " + e.Message, e);
        }

        await updater.StageAsync(zip, cancellationToken).ConfigureAwait(false);
        updater.ScheduleSwap(manifest.AppVersion);
        return UpdateApplyOutcome.QuitToFinish;
    }

    private UpdateApplyOutcome ApplyAndroid(string apk)
    {
        IAppInstaller installer = options.AppInstaller
            ?? throw new UpdateException(UpdateErrorKind.Unsupported, "No app installer is set.");
        if (!installer.CanRequestInstall)
        {
            installer.RequestInstallPermission();
            return UpdateApplyOutcome.PermissionRequested;
        }

        installer.LaunchInstaller(apk);
        return UpdateApplyOutcome.InstallerStarted;
    }

    private void RemoveOtherDownloads(string keep)
    {
        string[] files;
        try
        {
            files = Directory.Exists(AppDownloadDirectory) ? Directory.GetFiles(AppDownloadDirectory) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "listing old downloads");
        }

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            if (name == keep || name == keep + UpdateDownloader.PartSuffix)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Tribes.Update: could not remove the old download " + name + ": " + e.Message);
            }
        }
    }
}
