using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;

namespace Tribes.Release;

/// <summary>Release administration with the write token: drafts, uploads, publishing, deletion.</summary>
internal sealed class GitHubAdmin
{
    private const int PageSize = 100;
    private const int MaxPages = 10;
    private readonly HttpClient http;
    private readonly string token;
    private readonly Uri apiBase;
    private readonly Uri uploadBase;

    /// <summary>Where GitHub takes release asset uploads; the write token is sent nowhere else.</summary>
    public static readonly Uri DefaultUploadBase = new("https://uploads.github.com/");

    public GitHubAdmin(HttpClient http, string repository, string token, Uri? apiBase = null, Uri? uploadBase = null)
    {
        if (!GitHubReleaseClient.IsRepositoryName(repository))
        {
            throw new ToolException("The repository must be 'owner/name'.");
        }

        this.http = http;
        this.token = token;
        this.apiBase = apiBase ?? GitHubReleaseClient.DefaultApiBase;
        this.uploadBase = uploadBase ?? DefaultUploadBase;
        Repository = repository;
    }

    public string Repository { get; }

    /// <summary>Every release, drafts included (the write token sees drafts).</summary>
    public async Task<IReadOnlyList<GitHubRelease>> ListAllReleasesAsync(CancellationToken ct)
    {
        var all = new List<GitHubRelease>();
        for (int page = 1; page <= MaxPages; page++)
        {
            using JsonDocument document = await SendJsonAsync(
                HttpMethod.Get,
                string.Create(CultureInfo.InvariantCulture, $"repos/{Repository}/releases?per_page={PageSize}&page={page}"),
                null,
                "listing releases",
                ct);
            IReadOnlyList<GitHubRelease> releases = GitHubRelease.ListFromJson(document.RootElement);
            all.AddRange(releases);
            if (releases.Count < PageSize)
            {
                break;
            }
        }

        return all;
    }

    public async Task<GitHubRelease> GetReleaseAsync(long id, CancellationToken ct)
    {
        using JsonDocument document = await SendJsonAsync(HttpMethod.Get, RepoPath($"releases/{id}"), null, "reading the release", ct);
        return GitHubRelease.FromJson(document.RootElement);
    }

    public async Task<GitHubRelease> CreateDraftAsync(string tag, string name, string body, bool prerelease, CancellationToken ct)
    {
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Post,
            RepoPath("releases"),
            new Dictionary<string, object> { ["tag_name"] = tag, ["name"] = name, ["body"] = body, ["draft"] = true, ["prerelease"] = prerelease },
            "creating the draft release",
            ct);
        return GitHubRelease.FromJson(document.RootElement);
    }

    public async Task<GitHubRelease> UpdateReleaseAsync(long id, IReadOnlyDictionary<string, object> fields, CancellationToken ct)
    {
        using JsonDocument document = await SendJsonAsync(HttpMethod.Patch, RepoPath($"releases/{id}"), fields, "updating the release", ct);
        return GitHubRelease.FromJson(document.RootElement);
    }

    public Task DeleteAssetAsync(long assetId, CancellationToken ct) =>
        SendNoContentAsync(HttpMethod.Delete, RepoPath($"releases/assets/{assetId}"), "deleting an asset", allowNotFound: true, ct);

    public Task DeleteReleaseAsync(long id, CancellationToken ct) =>
        SendNoContentAsync(HttpMethod.Delete, RepoPath($"releases/{id}"), "deleting the release", allowNotFound: false, ct);

    /// <summary>Deletes a tag; false when it did not exist.</summary>
    public async Task<bool> DeleteTagAsync(string tag, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, new Uri(apiBase, RepoPath("git/refs/tags/" + Uri.EscapeDataString(tag))), null, ct, "deleting the tag");
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            return false;
        }

        await EnsureSuccessAsync(response, "deleting the tag", ct);
        return true;
    }

    /// <summary>Uploads a file as a release asset through uploads.github.com.</summary>
    public async Task<GitHubAsset> UploadAssetAsync(GitHubRelease release, string name, Stream content, long length, string contentType, CancellationToken ct)
    {
        string template = release.UploadUrl ?? throw new ToolException("The release has no upload URL.");
        int brace = template.IndexOf('{', StringComparison.Ordinal);
        var uri = new Uri((brace >= 0 ? template[..brace] : template) + "?name=" + Uri.EscapeDataString(name));
        if (Uri.Compare(uri, uploadBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw new ToolException($"Refusing to upload to {uri.Scheme}://{uri.Host}; uploads go to {uploadBase.Host} only.");
        }
        var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Headers.ContentLength = length;
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, uri, body, ct, "uploading " + name);
        await EnsureSuccessAsync(response, "uploading " + name, ct);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        return GitHubAsset.FromJson(document.RootElement);
    }

    private string RepoPath(string relative) => $"repos/{Repository}/{relative}";

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? payload, string what, CancellationToken ct)
    {
        HttpContent? content = payload is null
            ? null
            : new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await SendAsync(method, new Uri(apiBase, path), content, ct, what);
        await EnsureSuccessAsync(response, what, ct);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, string what, bool allowNotFound, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(method, new Uri(apiBase, path), null, ct, what);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, what, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken ct, string what)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        GitHubHttp.AddStandardHeaders(request, token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new ToolException($"No answer from GitHub while {what}: {e.Message}", e);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        UpdateException error = GitHubHttp.ErrorFor(response, what);
        string message = "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if (document.RootElement.TryGetProperty("message", out JsonElement value))
            {
                message = " " + value.GetString();
            }

            if (document.RootElement.TryGetProperty("errors", out JsonElement errors))
            {
                message += " " + errors.GetRawText();
            }
        }
        catch (JsonException)
        {
            message = "";
        }

        throw new ToolException($"{error.Message} ({error.Kind}){message}", error);
    }
}
