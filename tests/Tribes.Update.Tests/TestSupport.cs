using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;

namespace Tribes.Update.Tests;

/// <summary>A request as the fake server saw it.</summary>
internal sealed record SeenRequest(HttpMethod Method, Uri Uri, string? Authorization, long? RangeFrom, string? Accept);

/// <summary>A temporary directory removed at the end of the test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tribes-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test that keeps a file open (rollback tests) leaves it for the OS to clean.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>An HTTP handler answering from a function, recording every request.</summary>
internal class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        this.respond = respond;
    }

    public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public List<SeenRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(new SeenRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Range?.Ranges.FirstOrDefault()?.From,
                string.Join(",", request.Headers.Accept.Select(a => a.MediaType))));
        }

        return respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>
/// A fake GitHub: the releases API of <c>owner/repo</c> on api.github.com, with asset downloads
/// redirected to a storage host that supports Range requests.
/// </summary>
internal sealed class FakeGitHub : RecordingHandler
{
    public const string Repo = "owner/repo";
    public const string Token = "test-token-0123456789";
    public const string StorageHost = "objects.example.test";

    private readonly List<Dictionary<string, object?>> releases = [];
    private readonly Dictionary<long, byte[]> data = [];
    private long nextId = 1000;

    public FakeGitHub()
        : base((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)))
    {
        Handler = Route;
    }

    /// <summary>Answers instead of the fake when it returns a response (failure injection).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Whether the storage host honours Range.</summary>
    public bool SupportsRange { get; set; } = true;

    /// <summary>Bytes the storage host sends for an asset, when they should differ from the stored ones.</summary>
    public Dictionary<long, byte[]> Served { get; } = [];

    private Func<HttpRequestMessage, HttpResponseMessage> Handler { get; }

    public HttpClient CreateHttpClient() => new(this, disposeHandler: false);

    public GitHubReleaseClient CreateClient(HttpClient http) => new(http, Repo, Token);

    public long AddRelease(string tag, bool prerelease = false, bool draft = false)
    {
        long id = nextId++;
        releases.Add(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["tag_name"] = tag,
            ["name"] = tag,
            ["body"] = "",
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["upload_url"] = $"https://uploads.example.test/repos/{Repo}/releases/{id}/assets{{?name,label}}",
            ["assets"] = new List<Dictionary<string, object?>>(),
        });
        return id;
    }

    public long AddAsset(long releaseId, string name, byte[] bytes, long? listedSize = null)
    {
        long id = nextId++;
        data[id] = bytes;
        Assets(releaseId).Add(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["size"] = listedSize ?? bytes.Length,
            ["state"] = "uploaded",
            ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        });
        return id;
    }

    /// <summary>A release with payload files, the manifest and its signature.</summary>
    public long AddSignedRelease(UpdateManifest manifest, ECDsa key, IReadOnlyDictionary<string, byte[]> files, bool prerelease = false, string? tag = null)
    {
        long id = AddRelease(tag ?? manifest.Tag.ToString(), prerelease);
        foreach ((string name, byte[] bytes) in files)
        {
            AddAsset(id, name, bytes);
        }

        byte[] manifestBytes = ManifestJson.Serialize(manifest);
        AddAsset(id, UpdateManifest.FileName, manifestBytes);
        AddAsset(id, UpdateManifest.SignatureFileName, ManifestSignature.Sign(manifestBytes, key));
        return id;
    }

    public long AssetId(long releaseId, string name) =>
        (long)Assets(releaseId).First(a => (string)a["name"]! == name)["id"]!;

    public void ReplaceAssetData(long assetId, byte[] bytes) => data[assetId] = bytes;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(new SeenRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Range?.Ranges.FirstOrDefault()?.From,
                string.Join(",", request.Headers.Accept.Select(a => a.MediaType))));
        }

        return Task.FromResult(Intercept?.Invoke(request) ?? Handler(request));
    }

    private List<Dictionary<string, object?>> Assets(long releaseId) =>
        (List<Dictionary<string, object?>>)releases.First(r => (long)r["id"]! == releaseId)["assets"]!;

    private HttpResponseMessage Route(HttpRequestMessage request)
    {
        Uri uri = request.RequestUri!;
        string path = uri.AbsolutePath;
        if (uri.Host == StorageHost)
        {
            return Storage(request, long.Parse(path["/assets/".Length..], System.Globalization.CultureInfo.InvariantCulture));
        }

        string prefix = $"/repos/{Repo}/releases";
        if (path == prefix)
        {
            return Json(JsonSerializer.Serialize(releases));
        }

        if (path.StartsWith(prefix + "/tags/", StringComparison.Ordinal))
        {
            string tag = Uri.UnescapeDataString(path[(prefix.Length + 6)..]);
            Dictionary<string, object?>? release = releases.FirstOrDefault(r => (string)r["tag_name"]! == tag && !(bool)r["draft"]!);
            return release is null ? Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound) : Json(JsonSerializer.Serialize(release));
        }

        if (path.StartsWith(prefix + "/assets/", StringComparison.Ordinal))
        {
            long id = long.Parse(path[(prefix.Length + 8)..], System.Globalization.CultureInfo.InvariantCulture);
            if (!data.ContainsKey(id))
            {
                return Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);
            }

            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri($"https://{StorageHost}/assets/{id}?X-Amz-Signature=signed");
            return redirect;
        }

        return Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Storage(HttpRequestMessage request, long id)
    {
        byte[] bytes = Served.TryGetValue(id, out byte[]? served) ? served : data[id];
        long from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
        if (!SupportsRange || from == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }

        if (from >= bytes.Length)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        }

        var content = new ByteArrayContent(bytes, (int)from, bytes.Length - (int)from);
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, bytes.Length - 1, bytes.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }
}

/// <summary>Test keys, manifests and files.</summary>
internal static class TestData
{
    public static ECDsa NewKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static TrustedKeySet Trust(params ECDsa[] keys) => new(keys.Select(TrustedKey.FromKey));

    public static byte[] Bytes(int length, int seed = 1)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static ManifestAsset Asset(string name, byte[] bytes, AssetKind kind = AssetKind.Content, UpdatePlatform platform = UpdatePlatform.Any) =>
        new(name, platform, kind, bytes.Length, Sha(bytes));

    public static UpdateManifest Manifest(string version, int revision, IEnumerable<ManifestAsset> assets, string? minApp = null, string notes = "更新內容") =>
        new(AppVersion.Parse(version), revision, AppVersion.Parse(minApp ?? version), notes, assets);
}
