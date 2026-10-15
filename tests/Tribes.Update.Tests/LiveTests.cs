using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

/// <summary>
/// LIVE tests against the real resource repository. They run only with TRIBES_LIVE_TESTS=1, read
/// secrets/github.json at run time, use only the read token, and only read. With TRIBES_LIVE_TAG
/// set they also verify that release completely, the way the game would.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveTests
{
    [LiveFact]
    public async Task ReadToken_CanListReleases()
    {
        using HttpClient http = UpdateHttp.CreateClient();
        GitHubReleaseClient client = LiveClient(http);

        var releases = await client.ListReleasesAsync();

        Assert.All(releases, r => Assert.False(r.Draft));
    }

    [LiveFact]
    public async Task TaggedRelease_VerifiesLikeTheGame()
    {
        string? tag = Environment.GetEnvironmentVariable("TRIBES_LIVE_TAG");
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        var recorder = new HostRecorder();
        using var http = new HttpClient(recorder) { Timeout = UpdateHttp.HeaderTimeout };
        GitHubReleaseClient client = LiveClient(http);
        GitHubRelease release = (await client.GetReleaseByTagAsync(tag))!;
        Assert.NotNull(release);

        VerifiedRelease verified = await new UpdateChecker(client, TrustedKeySet.LoadEmbedded()).FetchVerifiedAsync(release);
        using var temp = new TempDir();
        var downloader = new UpdateDownloader(client);
        foreach (ManifestAsset asset in verified.Manifest.Assets)
        {
            string path = await downloader.DownloadAsync(release.FindAsset(asset.Name)!, asset, temp.File(asset.Name));
            Assert.True(await UpdateDownloader.MatchesAsync(path, asset));
        }

        UpdateCheckResult check = await new UpdateChecker(client, TrustedKeySet.LoadEmbedded()).CheckAsync(new UpdateCheckOptions
        {
            CurrentAppVersion = new AppVersion(0, 0, 0),
            InstalledContentRevision = 0,
            Platform = UpdatePlatform.Windows,
            IncludePrereleases = true,
        });
        Assert.NotEqual(UpdateKind.None, check.Kind);

        // The asset downloads were redirected to GitHub's storage host, which never saw the token.
        Assert.Contains(recorder.Seen, s => s.Host != "api.github.com");
        Assert.All(recorder.Seen, s => Assert.Equal(s.Host == "api.github.com", s.HadToken));
    }

    /// <summary>Records the host of every request and whether it carried a token (never the token).</summary>
    private sealed class HostRecorder : DelegatingHandler
    {
        public HostRecorder()
            : base(new HttpClientHandler { AllowAutoRedirect = false })
        {
        }

        public System.Collections.Generic.List<(string Host, bool HadToken)> Seen { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            lock (Seen)
            {
                Seen.Add((request.RequestUri!.Host, request.Headers.Authorization is not null));
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private static GitHubReleaseClient LiveClient(HttpClient http)
    {
        using JsonDocument secrets = JsonDocument.Parse(File.ReadAllBytes(LiveFactAttribute.SecretsPath()!));
        return new GitHubReleaseClient(
            http,
            secrets.RootElement.GetProperty("repo").GetString()!,
            secrets.RootElement.GetProperty("readToken").GetString());
    }
}

/// <summary>A live test: skipped unless TRIBES_LIVE_TESTS=1 and secrets/github.json exists.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRIBES_LIVE_TESTS") != "1")
        {
            Skip = "Live test: set TRIBES_LIVE_TESTS=1 to run against GitHub.";
        }
        else if (SecretsPath() is null)
        {
            Skip = "Live test: secrets/github.json not found.";
        }
    }

    internal static string? SecretsPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "secrets", "github.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
