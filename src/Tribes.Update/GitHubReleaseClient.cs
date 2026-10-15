using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Update;

/// <summary>Opens the download of a release asset, from a byte offset on.</summary>
public interface IAssetSource
{
    /// <summary>
    /// Starts downloading an asset. The response is 200 (whole file), 206 (from
    /// <paramref name="offset"/>) or 416 (offset past the end); other answers throw
    /// <see cref="UpdateException"/>. The caller disposes the response.
    /// </summary>
    Task<HttpResponseMessage> OpenAssetAsync(long assetId, long offset, CancellationToken cancellationToken);
}

/// <summary>
/// Read access to the releases of one GitHub repository. Sends the token as
/// <c>Authorization: Bearer</c> to the API host only: asset downloads are answered with a
/// redirect to a storage host, which is followed without the header. Give it an
/// <see cref="HttpClient"/> that does not follow redirects itself (<see cref="UpdateHttp.CreateClient"/>).
/// </summary>
public sealed class GitHubReleaseClient : IAssetSource
{
    /// <summary>The public GitHub API.</summary>
    public static readonly Uri DefaultApiBase = new("https://api.github.com/");

    private const int MaxRedirects = 5;
    private const int MaxJsonBytes = 4 * 1024 * 1024;
    private const int ReleasesPerPage = 100;

    private readonly HttpClient http;
    private readonly string? token;
    private readonly Uri apiBase;

    /// <summary>Creates a client for <c>owner/name</c>; a null token sends anonymous requests.</summary>
    public GitHubReleaseClient(HttpClient http, string repository, string? token, Uri? apiBase = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!IsRepositoryName(repository))
        {
            throw new ArgumentException("The repository must be 'owner/name'.", nameof(repository));
        }

        this.http = http;
        this.token = string.IsNullOrEmpty(token) ? null : token;
        this.apiBase = apiBase ?? DefaultApiBase;
        Repository = repository;
    }

    /// <summary>The repository, <c>owner/name</c>.</summary>
    public string Repository { get; }

    /// <summary>Whether <c>owner/name</c> is a well-formed repository name.</summary>
    public static bool IsRepositoryName(string? repository)
    {
        string[] parts = repository?.Split('/') ?? [];
        return parts.Length == 2 && parts.All(p => p.Length > 0 && p.Length <= 100 && p.All(IsNameChar) && p != "." && p != "..");

        static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';
    }

    /// <summary>
    /// The newest releases (up to 100, newest first). Drafts appear only with a token that can
    /// write to the repository.
    /// </summary>
    public async Task<IReadOnlyList<GitHubRelease>> ListReleasesAsync(CancellationToken cancellationToken = default)
    {
        using JsonDocument? document = await GetJsonAsync(
            string.Create(CultureInfo.InvariantCulture, $"repos/{Repository}/releases?per_page={ReleasesPerPage}"),
            allowNotFound: false,
            "listing releases",
            cancellationToken).ConfigureAwait(false);
        return Parse(() => GitHubRelease.ListFromJson(document!.RootElement));
    }

    /// <summary>The published release with a tag, or null when there is none.</summary>
    public async Task<GitHubRelease?> GetReleaseByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        using JsonDocument? document = await GetJsonAsync(
            $"repos/{Repository}/releases/tags/{Uri.EscapeDataString(tag)}",
            allowNotFound: true,
            "reading a release",
            cancellationToken).ConfigureAwait(false);
        return document is null ? null : Parse(() => GitHubRelease.FromJson(document.RootElement));
    }

    /// <summary>Downloads a small asset (a manifest or a signature) into memory.</summary>
    public async Task<byte[]> DownloadAssetBytesAsync(GitHubAsset asset, int maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Size > maxBytes)
        {
            throw new UpdateException(UpdateErrorKind.InvalidManifest, $"Asset {asset.Name} is larger than {maxBytes} bytes.");
        }

        using HttpResponseMessage response = await OpenAssetAsync(asset.Id, 0, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new UpdateException(UpdateErrorKind.ServerError, $"Unexpected status {(int)response.StatusCode} downloading {asset.Name}.");
        }

        return await GitHubHttp.ReadLimitedAsync(response.Content, maxBytes, "downloading " + asset.Name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> OpenAssetAsync(long assetId, long offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var uri = new Uri(apiBase, string.Create(CultureInfo.InvariantCulture, $"repos/{Repository}/releases/assets/{assetId}"));
        HttpResponseMessage response = await SendAsync(
            uri,
            request =>
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                }
            },
            "downloading an asset",
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return response;
        }

        using (response)
        {
            throw GitHubHttp.ErrorFor(response, "downloading an asset");
        }
    }

    private static T Parse<T>(Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or KeyNotFoundException)
        {
            throw new UpdateException(UpdateErrorKind.ServerError, "GitHub answered with an unexpected document.", e);
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, bool allowNotFound, string what, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            new Uri(apiBase, path),
            request => request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json")),
            what,
            cancellationToken).ConfigureAwait(false);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw GitHubHttp.ErrorFor(response, what);
        }

        byte[] body = await GitHubHttp.ReadLimitedAsync(response.Content, MaxJsonBytes, what, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new UpdateException(UpdateErrorKind.ServerError, $"GitHub answered with invalid JSON while {what}.", e);
        }
    }

    /// <summary>
    /// Sends a GET and follows redirects by hand. The token goes only to the API's own origin;
    /// once a redirect leaves it, no later request carries the token. Redirects must stay on HTTPS.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(Uri uri, Action<HttpRequestMessage> configure, string what, CancellationToken cancellationToken)
    {
        bool sendToken = token is not null;
        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            GitHubHttp.AddStandardHeaders(request, sendToken && IsApiOrigin(uri) ? token : null);
            configure(request);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw UpdateErrors.FromNetwork(e, what);
            }

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirects >= MaxRedirects)
            {
                throw new UpdateException(UpdateErrorKind.ServerError, $"Bad or endless redirect while {what}.");
            }

            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new UpdateException(UpdateErrorKind.ServerError, $"Refused a redirect away from HTTPS while {what}.");
            }

            sendToken &= IsApiOrigin(uri);
        }
    }

    private bool IsApiOrigin(Uri uri) =>
        Uri.Compare(uri, apiBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}

/// <summary>Request headers and error mapping shared by the GitHub clients.</summary>
public static class GitHubHttp
{
    /// <summary>The User-Agent sent to GitHub (it rejects requests without one).</summary>
    public const string UserAgent = "PrehistoricTribes-Updater/1";

    /// <summary>The REST API version requested.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>Adds User-Agent, API version and, when given, the bearer token.</summary>
    public static void AddStandardHeaders(HttpRequestMessage request, string? token)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>The update error for an unsuccessful GitHub answer.</summary>
    public static UpdateException ErrorFor(HttpResponseMessage response, string what)
    {
        ArgumentNullException.ThrowIfNull(response);
        int status = (int)response.StatusCode;
        string detail = string.Create(CultureInfo.InvariantCulture, $"GitHub answered {status} while {what}.");
        TimeSpan? retryAfter = RetryAfter(response);
        bool limitReached = Header(response, "X-RateLimit-Remaining") == "0";
        return status switch
        {
            401 => new UpdateException(UpdateErrorKind.AuthFailed, detail),
            403 when limitReached || retryAfter is not null => new UpdateException(UpdateErrorKind.RateLimited, detail, retryAfter: retryAfter),
            403 => new UpdateException(UpdateErrorKind.AuthFailed, detail),
            429 => new UpdateException(UpdateErrorKind.RateLimited, detail, retryAfter: retryAfter),
            404 or 410 => new UpdateException(UpdateErrorKind.NotFound, detail),
            _ => new UpdateException(UpdateErrorKind.ServerError, detail),
        };
    }

    /// <summary>Reads a response body, failing when it is larger than allowed.</summary>
    public static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, string what, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength > maxBytes)
        {
            throw new UpdateException(UpdateErrorKind.ServerError, $"The answer is too large while {what}.");
        }

        try
        {
            using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new UpdateException(UpdateErrorKind.ServerError, $"The answer is too large while {what}.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception e) when (UpdateErrors.IsNetworkFailure(e) || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw UpdateErrors.FromNetwork(e, what);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        const long LatestUnixSeconds = 253_402_300_799; // 9999-12-31
        if (long.TryParse(Header(response, "X-RateLimit-Reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long reset)
            && reset <= LatestUnixSeconds
            && Header(response, "X-RateLimit-Remaining") == "0")
        {
            TimeSpan wait = DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
}

/// <summary>The HTTP client the updater expects.</summary>
public static class UpdateHttp
{
    /// <summary>Time allowed until the answer's headers arrive.</summary>
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A client that does not follow redirects on its own (<see cref="GitHubReleaseClient"/> follows
    /// them and decides where the token may go). Keep one for the life of the game.
    /// </summary>
    public static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new HttpClient(handler, disposeHandler: true) { Timeout = HeaderTimeout };
    }
}
