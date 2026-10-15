using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class GitHubClientTests
{
    private const string Bearer = "Bearer " + FakeGitHub.Token;

    [Fact]
    public async Task ListReleases_SendsTokenAndStandardHeaders()
    {
        using var github = new FakeGitHub();
        github.AddRelease("v1.0.0");
        using HttpClient http = github.CreateHttpClient();

        var releases = await github.CreateClient(http).ListReleasesAsync();

        Assert.Equal("v1.0.0", Assert.Single(releases).TagName);
        SeenRequest request = Assert.Single(github.Requests);
        Assert.Equal(Bearer, request.Authorization);
        Assert.Equal("application/vnd.github+json", request.Accept);
        Assert.Equal("https://api.github.com/repos/owner/repo/releases?per_page=100", request.Uri.ToString());
    }

    [Fact]
    public async Task AssetDownload_FollowsRedirectWithoutForwardingTheToken()
    {
        using var github = new FakeGitHub();
        long release = github.AddRelease("v1.0.0");
        long asset = github.AddAsset(release, "a.pck", [1, 2, 3]);
        using HttpClient http = github.CreateHttpClient();

        using HttpResponseMessage response = await github.CreateClient(http).OpenAssetAsync(asset, 0, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(2, github.Requests.Count);
        Assert.Equal(("api.github.com", Bearer, "application/octet-stream"), (github.Requests[0].Uri.Host, github.Requests[0].Authorization, github.Requests[0].Accept));
        Assert.Equal(FakeGitHub.StorageHost, github.Requests[1].Uri.Host);
        Assert.Null(github.Requests[1].Authorization);
    }

    [Fact]
    public async Task Redirect_KeepsTokenOnTheApiOriginOnlyUntilItLeaves()
    {
        int step = 0;
        using var handler = new RecordingHandler(request =>
        {
            step++;
            var response = new HttpResponseMessage(step < 3 ? HttpStatusCode.MovedPermanently : HttpStatusCode.OK);
            response.Headers.Location = step switch
            {
                1 => new Uri("/repositories/42/releases?per_page=100", UriKind.Relative),
                2 => new Uri("https://elsewhere.example.test/x"),
                _ => null,
            };
            response.Content = new StringContent("[]");
            return response;
        });
        using var http = new HttpClient(handler);

        await new GitHubReleaseClient(http, FakeGitHub.Repo, FakeGitHub.Token).ListReleasesAsync();

        Assert.Equal([Bearer, Bearer, null], handler.Requests.Select(r => r.Authorization));
        Assert.Equal("https://api.github.com/repositories/42/releases?per_page=100", handler.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Redirect_RefusesPlainHttpAndEndlessLoops()
    {
        using var toHttp = new RecordingHandler(_ => Redirect("http://insecure.example.test/x"));
        using var loop = new RecordingHandler(_ => Redirect("https://api.github.com/again"));
        using var empty = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));

        foreach (RecordingHandler handler in new[] { toHttp, loop, empty })
        {
            using var http = new HttpClient(handler);
            UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubReleaseClient(http, FakeGitHub.Repo, null).OpenAssetAsync(1, 0, CancellationToken.None));
            Assert.Equal(UpdateErrorKind.ServerError, error.Kind);
        }

        Assert.Equal(6, loop.Requests.Count);
        Assert.All(toHttp.Requests, r => Assert.Null(r.Authorization));
    }

    [Theory]
    [InlineData(401, null, null, UpdateErrorKind.AuthFailed)]
    [InlineData(403, null, null, UpdateErrorKind.AuthFailed)]
    [InlineData(403, "0", null, UpdateErrorKind.RateLimited)]
    [InlineData(403, null, 60, UpdateErrorKind.RateLimited)]
    [InlineData(429, null, 30, UpdateErrorKind.RateLimited)]
    [InlineData(404, null, null, UpdateErrorKind.NotFound)]
    [InlineData(500, null, null, UpdateErrorKind.ServerError)]
    [InlineData(503, null, null, UpdateErrorKind.ServerError)]
    public async Task Errors_MapToCategories(int status, string? remaining, int? retryAfter, UpdateErrorKind expected)
    {
        using var handler = new RecordingHandler(_ =>
        {
            var response = RecordingHandler.Json("{\"message\":\"x\"}", (HttpStatusCode)status);
            if (remaining is not null)
            {
                response.Headers.Add("X-RateLimit-Remaining", remaining);
                response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (retryAfter is int seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        });
        using var http = new HttpClient(handler);

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubReleaseClient(http, FakeGitHub.Repo, FakeGitHub.Token).ListReleasesAsync());

        Assert.Equal(expected, error.Kind);
        Assert.DoesNotContain(FakeGitHub.Token, error.Message, StringComparison.Ordinal);
        Assert.Equal(expected == UpdateErrorKind.RateLimited, error.RetryAfter is not null);
    }

    [Fact]
    public async Task NetworkFailures_AreOffline()
    {
        using var refused = new RecordingHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("refused")));
        using var timeout = new RecordingHandler((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException())));

        foreach (RecordingHandler handler in new[] { refused, timeout })
        {
            using var http = new HttpClient(handler);
            UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubReleaseClient(http, FakeGitHub.Repo, null).ListReleasesAsync());
            Assert.Equal(UpdateErrorKind.Offline, error.Kind);
            Assert.True(error.IsTransient);
        }
    }

    [Fact]
    public async Task CallerCancellation_IsNotAnError()
    {
        using var handler = new RecordingHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));
        using var http = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubReleaseClient(http, FakeGitHub.Repo, null).ListReleasesAsync(cancel.Token));
    }

    [Fact]
    public async Task GetReleaseByTag_ReturnsNullWhenMissing()
    {
        using var github = new FakeGitHub();
        github.AddRelease("v1.0.0");
        github.AddRelease("v2.0.0", draft: true);
        using HttpClient http = github.CreateHttpClient();
        GitHubReleaseClient client = github.CreateClient(http);

        Assert.Equal("v1.0.0", (await client.GetReleaseByTagAsync("v1.0.0"))!.TagName);
        Assert.Null(await client.GetReleaseByTagAsync("v2.0.0"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"not\":\"an array\"}")]
    [InlineData("[{\"id\":1}]")]
    public async Task UnexpectedDocuments_AreServerErrors(string body)
    {
        using var handler = new RecordingHandler(_ => RecordingHandler.Json(body));
        using var http = new HttpClient(handler);

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => new GitHubReleaseClient(http, FakeGitHub.Repo, null).ListReleasesAsync());

        Assert.Equal(UpdateErrorKind.ServerError, error.Kind);
    }

    [Fact]
    public async Task SmallAssets_AreSizeLimited()
    {
        using var github = new FakeGitHub();
        long release = github.AddRelease("v1.0.0");
        long id = github.AddAsset(release, "manifest.json", new byte[100]);
        long lying = github.AddAsset(release, "manifest.json.sig", new byte[100], listedSize: 10);
        using HttpClient http = github.CreateHttpClient();
        GitHubReleaseClient client = github.CreateClient(http);

        UpdateException tooLarge = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAssetBytesAsync(new GitHubAsset(id, "manifest.json", 100, "uploaded", null), 50));
        UpdateException grew = await Assert.ThrowsAsync<UpdateException>(() => client.DownloadAssetBytesAsync(new GitHubAsset(lying, "manifest.json.sig", 10, "uploaded", null), 50));

        Assert.Equal(UpdateErrorKind.InvalidManifest, tooLarge.Kind);
        Assert.Equal(UpdateErrorKind.ServerError, grew.Kind);
    }

    [Theory]
    [InlineData("owner/repo", true)]
    [InlineData("Owner42/game-releases", true)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("owner/..", false)]
    [InlineData("own er/repo", false)]
    [InlineData(null, false)]
    public void RepositoryNames(string? name, bool valid)
    {
        Assert.Equal(valid, GitHubReleaseClient.IsRepositoryName(name));
    }

    [Fact]
    public void Constructor_RejectsBadRepository()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(() => new GitHubReleaseClient(http, "bad", null));
    }

    [Fact]
    public void Models_ReadDigestsAndStates()
    {
        var asset = new GitHubAsset(1, "a", 2, "starter", "sha256:ABCDEF");

        Assert.Equal("abcdef", asset.Sha256);
        Assert.False(asset.IsUploaded);
        Assert.Null((asset with { Digest = null }).Sha256);
        Assert.Null((asset with { Digest = "md5:00" }).Sha256);
    }

    [Fact]
    public void DefaultClient_DoesNotFollowRedirects()
    {
        using HttpClient http = UpdateHttp.CreateClient();

        Assert.Equal(UpdateHttp.HeaderTimeout, http.Timeout);
    }

    [Fact]
    public async Task HeaderTimeout_DoesNotLimitReadingTheBody()
    {
        // A local server that answers at once and then trickles the body for longer than the timeout.
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            using System.Net.Sockets.TcpClient connection = await listener.AcceptTcpClientAsync();
            using System.Net.Sockets.NetworkStream stream = connection.GetStream();
            byte[] request = new byte[4096];
            _ = await stream.ReadAsync(request);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\n"u8.ToArray());
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(400);
                await stream.WriteAsync(new byte[] { (byte)i });
            }
        });
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMilliseconds(500) };

        using HttpResponseMessage response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/"), HttpCompletionOption.ResponseHeadersRead);
        byte[] body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(new byte[] { 0, 1, 2 }, body);
        await server;
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }
}
