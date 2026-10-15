using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Release;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class ReleaseToolTests : IDisposable
{
    private readonly TempDir temp = new();
    private readonly FakeAdminGitHub github = new();
    private readonly HttpClient http;
    private readonly string keyPath;
    private readonly TrustedKeySet trusted;

    public ReleaseToolTests()
    {
        http = new HttpClient(github);
        keyPath = temp.File("signing.pem");
        using ECDsa key = TestData.NewKey();
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        trusted = TestData.Trust(key);
        File.WriteAllBytes(temp.File("app.apk"), TestData.Bytes(5000, 1));
        File.WriteAllBytes(temp.File("app.zip"), TestData.Bytes(6000, 2));
        File.WriteAllBytes(temp.File("content.pck"), TestData.Bytes(700, 3));
        File.WriteAllText(temp.File("notes.txt"), "第一行\r\n第二行\r\n", Encoding.UTF8);
    }

    public void Dispose()
    {
        http.Dispose();
        github.Dispose();
        temp.Dispose();
    }

    [Fact]
    public void BuildPlan_HashesSignsAndNamesTheFiles()
    {
        ReleasePlan plan = Plan("--content-rev", "2");

        Assert.Equal("v1.2.0-r2", plan.Tag.ToString());
        Assert.Equal(
            ["PrehistoricTribes-1.2.0-android.apk", "PrehistoricTribes-1.2.0-windows.zip", "content-1.2.0-r2.pck", "manifest.json", "manifest.json.sig"],
            plan.AllFiles.Select(f => f.Name));
        UpdateManifest manifest = ManifestJson.Parse(plan.ManifestBytes);
        Assert.Equal("第一行\n第二行", manifest.Notes);
        Assert.Equal(TestData.Sha(File.ReadAllBytes(temp.File("app.apk"))), manifest.Assets[0].Sha256);
        Assert.Equal(plan.KeyId, ManifestSignature.Verify(plan.ManifestBytes, File.ReadAllBytes(plan.ManifestFiles[1].LocalPath), trusted));
        Assert.Equal(plan.ManifestBytes, File.ReadAllBytes(plan.ManifestFiles[0].LocalPath));
    }

    [Theory]
    [InlineData("--pck", "x", "--version", "1.0.0")]
    [InlineData("--version", "1.0.0-beta")]
    [InlineData("--version", "bad")]
    [InlineData("--min-app-version", "bad")]
    public void BuildPlan_RejectsBadInput(params string[] extra)
    {
        string[] args = ["publish", "--notes", temp.File("notes.txt"), "--key", keyPath, "--out", temp.File("out"), "--apk", temp.File("app.apk"), "--version", "1.0.0", .. extra];
        if (extra.Contains("--pck"))
        {
            File.WriteAllText(temp.File("x"), "x");
            args = [.. args.Select(a => a == "x" ? temp.File("x") : a)];
        }

        Assert.Throws<ToolException>(() => Publisher.BuildPlan(new Options(args), trusted));
    }

    [Fact]
    public void BuildPlan_NeedsAFileAndATrustedKey()
    {
        var none = new Options(["publish", "--version", "1.0.0", "--notes", temp.File("notes.txt"), "--key", keyPath, "--out", temp.File("out")]);
        var missing = new Options(["publish", "--version", "1.0.0", "--apk", temp.File("nope.apk"), "--notes", temp.File("notes.txt"), "--key", keyPath]);

        Assert.Throws<ToolException>(() => Publisher.BuildPlan(none, trusted));
        Assert.Throws<ToolException>(() => Publisher.BuildPlan(missing, trusted));
        Assert.Throws<ToolException>(() => Publisher.BuildPlan(Options(), new TrustedKeySet([])));
    }

    [Fact]
    public async Task Publish_CreatesDraftUploadsEverythingAndPublishes()
    {
        ReleasePlan plan = Plan("--content-rev", "2");

        GitHubRelease release = await Publish(plan);

        Assert.False(release.Draft);
        Assert.Equal(plan.AllFiles.Select(f => f.Name).Order(), release.Assets.Select(a => a.Name).Order());
        Assert.Equal(["PrehistoricTribes-1.2.0-android.apk", "PrehistoricTribes-1.2.0-windows.zip", "content-1.2.0-r2.pck", "manifest.json", "manifest.json.sig"], github.Uploads);
        Assert.Equal("legacy", github.LastMakeLatest);

        VerifiedRelease verified = await new UpdateChecker(new GitHubReleaseClient(http, FakeAdminGitHub.Repo, "read"), trusted).FetchVerifiedAsync(release);
        Assert.Equal(plan.Tag, verified.Manifest.Tag);
    }

    [Fact]
    public async Task Publish_AgainWithTheSameFiles_DoesNothing()
    {
        ReleasePlan plan = Plan();
        await Publish(plan);
        int uploads = github.Uploads.Count;

        GitHubRelease again = await Publish(Plan());

        Assert.Equal(uploads, github.Uploads.Count);
        Assert.False(again.Draft);
    }

    [Fact]
    public async Task Publish_PublishedWithOtherFiles_IsRefused()
    {
        await Publish(Plan());
        File.WriteAllBytes(temp.File("app.apk"), TestData.Bytes(5000, 42));

        await Assert.ThrowsAsync<ToolException>(() => Publish(Plan()));
    }

    [Fact]
    public async Task Publish_ContinuesAnUnfinishedDraft()
    {
        ReleasePlan plan = Plan("--prerelease", "--version", "1.2.0-test");
        long draft = github.AddRelease(plan.Tag.ToString(), draft: true, prerelease: true);
        github.AddAsset(draft, "PrehistoricTribes-1.2.0-test-android.apk", File.ReadAllBytes(temp.File("app.apk")));
        github.AddAsset(draft, "stray.bin", [1, 2, 3]);
        github.AddAsset(draft, "PrehistoricTribes-1.2.0-test-windows.zip", [9], state: "starter");

        GitHubRelease release = await Publish(plan);

        Assert.DoesNotContain("PrehistoricTribes-1.2.0-test-android.apk", github.Uploads);
        Assert.Contains("PrehistoricTribes-1.2.0-test-windows.zip", github.Uploads);
        Assert.Null(release.FindAsset("stray.bin"));
        Assert.True(release.Prerelease);
        Assert.Null(github.LastMakeLatest);
        Assert.Equal(1, github.ReleaseCount);
    }

    [Fact]
    public async Task Publish_SendsUploadsOnlyToTheUploadHost()
    {
        var admin = new GitHubAdmin(http, FakeAdminGitHub.Repo, "write");

        await Assert.ThrowsAsync<ToolException>(() => Publisher.PublishAsync(admin, new GitHubReleaseClient(http, FakeAdminGitHub.Repo, "write"), Plan(), CancellationToken.None));
        Assert.Empty(github.Uploads);
    }

    [Fact]
    public void BuildPlan_RejectsRevisionsTooLargeForATag()
    {
        Assert.Throws<ToolException>(() => Plan("--content-rev", "123456789"));
    }

    [Fact]
    public async Task Publish_RetriesAFailedUpload()
    {
        github.FailNextUploads = 1;

        GitHubRelease release = await Publish(Plan());

        Assert.False(release.Draft);
        Assert.Equal(["PrehistoricTribes-1.2.0-android.apk", "PrehistoricTribes-1.2.0-windows.zip", "manifest.json", "manifest.json.sig"], github.Uploads);
        Assert.Equal(4, release.Assets.Count);
    }

    [Fact]
    public void KeyGen_CreatesAProtectedKeyAndTrustsIt()
    {
        string key = temp.File("new-key.pem");
        string keys = temp.File("keys.json");
        var options = new Options(["keygen", "--key", key, "--keys-file", keys]);

        Assert.Equal(0, SigningKeys.KeyGen(options));
        Assert.Equal(0, SigningKeys.KeyGen(options));

        using ECDsa loaded = SigningKeys.Load(key);
        TrustedKeySet set = SigningKeys.ReadTrusted(keys);
        Assert.NotNull(set.Find(TrustedKey.FromKey(loaded).KeyId));
        Assert.Single(set.Keys);
        if (OperatingSystem.IsWindows())
        {
            var rules = new FileInfo(key).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            Assert.Single(rules);
            Assert.False(Assert.IsType<System.Security.AccessControl.FileSystemAccessRule>(rules[0]).IsInherited);
        }
    }

    [Fact]
    public void Trust_ListsAndRemovesKeys()
    {
        string key = temp.File("k.pem");
        string keys = temp.File("keys.json");
        SigningKeys.KeyGen(new Options(["keygen", "--key", key, "--keys-file", keys]));
        using ECDsa other = TestData.NewKey();
        string otherId = TrustedKey.FromKey(other).KeyId;
        File.WriteAllBytes(keys, SigningKeys.ReadTrusted(keys).With(TrustedKey.FromKey(other)).Serialize());
        using ECDsa current = SigningKeys.Load(key);
        string currentId = TrustedKey.FromKey(current).KeyId;

        Assert.Equal(0, SigningKeys.Trust(new Options(["trust", "list", "--keys-file", keys, "--key", key])));
        Assert.Throws<ToolException>(() => SigningKeys.Trust(new Options(["trust", "remove", currentId, "--keys-file", keys, "--key", key])));
        Assert.Throws<ToolException>(() => SigningKeys.Trust(new Options(["trust", "remove", "0000000000000000", "--keys-file", keys, "--key", key])));
        Assert.Equal(0, SigningKeys.Trust(new Options(["trust", "remove", otherId, "--keys-file", keys, "--key", key])));
        Assert.Null(SigningKeys.ReadTrusted(keys).Find(otherId));
    }

    [Fact]
    public void Options_ParseValuesFlagsAndPositionals()
    {
        var options = new Options(["publish", "--version", "1.0.0", "--dry-run", "extra", "--content-rev", "3"]);

        Assert.Equal("publish", options.Command);
        Assert.Equal("1.0.0", options.Require("version"));
        Assert.True(options.Flag("dry-run"));
        Assert.Equal(3, options.GetInt("content-rev", 0));
        Assert.Equal(7, options.GetInt("missing", 7));
        Assert.Equal(["extra"], options.Positional);
        Assert.Throws<ToolException>(() => options.Require("apk"));
        Assert.Throws<ToolException>(() => new Options(["x", "--version"]));
        Assert.Throws<ToolException>(() => new Options(["x", "--content-rev", "-1"]).GetInt("content-rev", 0));
    }

    [Fact]
    public void Output_RedactsRegisteredSecrets()
    {
        Out.RegisterSecret("synthetic-secret-value");

        Assert.Equal("token *** end", Out.Redact("token synthetic-secret-value end"));
    }

    private ReleasePlan Plan(params string[] extra)
    {
        return Publisher.BuildPlan(Options(extra), trusted);
    }

    private Options Options(params string[] extra)
    {
        string version = extra.SkipWhile(a => a != "--version").Skip(1).FirstOrDefault() ?? "1.2.0";
        var args = new List<string>
        {
            "publish", "--version", version, "--notes", temp.File("notes.txt"), "--key", keyPath, "--out", temp.File("out"),
            "--apk", temp.File("app.apk"), "--windows-zip", temp.File("app.zip"),
        };
        if (extra.Contains("--content-rev"))
        {
            args.AddRange(["--pck", temp.File("content.pck")]);
        }

        for (int i = 0; i < extra.Length; i++)
        {
            if (extra[i] == "--version")
            {
                i++;
                continue;
            }

            args.Add(extra[i]);
        }

        return new Options(args);
    }

    private Task<GitHubRelease> Publish(ReleasePlan plan) =>
        Publisher.PublishAsync(
            new GitHubAdmin(http, FakeAdminGitHub.Repo, "write", uploadBase: new Uri("https://uploads.example.test/")),
            new GitHubReleaseClient(http, FakeAdminGitHub.Repo, "write"),
            plan,
            CancellationToken.None);
}

/// <summary>An in-memory GitHub with the release administration endpoints the tool uses.</summary>
internal sealed class FakeAdminGitHub : HttpMessageHandler
{
    public const string Repo = "owner/repo";

    private readonly List<JsonObject> releases = [];
    private readonly Dictionary<long, byte[]> data = [];
    private long nextId = 10;

    public List<string> Uploads { get; } = [];

    public string? LastMakeLatest { get; private set; }

    public int FailNextUploads { get; set; }

    public int ReleaseCount => releases.Count;

    public long AddRelease(string tag, bool draft, bool prerelease)
    {
        long id = nextId++;
        releases.Add(new JsonObject
        {
            ["id"] = id,
            ["tag_name"] = tag,
            ["name"] = tag,
            ["body"] = "",
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["upload_url"] = $"https://uploads.example.test/repos/{Repo}/releases/{id}/assets{{?name,label}}",
            ["assets"] = new JsonArray(),
        });
        return id;
    }

    public long AddAsset(long releaseId, string name, byte[] bytes, string state = "uploaded")
    {
        long id = nextId++;
        data[id] = bytes;
        Release(releaseId)["assets"]!.AsArray().Add(new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["size"] = bytes.Length,
            ["state"] = state,
            ["digest"] = "sha256:" + TestData.Sha(bytes),
        });
        return id;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri uri = request.RequestUri!;
        string path = uri.AbsolutePath;
        string prefix = $"/repos/{Repo}/";
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        if (uri.Host == "storage.example.test")
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data[Id(path["/assets/".Length..])]) };
        }

        if (uri.Host == "uploads.example.test")
        {
            if (FailNextUploads > 0)
            {
                FailNextUploads--;
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            long releaseId = Id(path[(prefix.Length + "releases/".Length)..].Split('/')[0]);
            string name = Uri.UnescapeDataString(uri.Query["?name=".Length..]);
            Uploads.Add(name);
            long assetId = AddAsset(releaseId, name, body);
            return Json(Release(releaseId)["assets"]!.AsArray().Last(a => (long)a!["id"]! == assetId)!, HttpStatusCode.Created);
        }

        string rest = path[prefix.Length..];
        return (request.Method.Method, rest.Split('/')) switch
        {
            ("GET", ["releases"]) => Json(new JsonArray(Query(uri, "page") == "1" ? releases.Select(r => (JsonNode)r.DeepClone()).ToArray() : [])),
            ("POST", ["releases"]) => Create(JsonNode.Parse(body)!.AsObject()),
            ("GET", ["releases", "assets", string id]) => Redirect(Id(id)),
            ("DELETE", ["releases", "assets", string id]) => DeleteAsset(Id(id)),
            ("GET", ["releases", string id]) => Json(Release(Id(id))),
            ("PATCH", ["releases", string id]) => Patch(Id(id), JsonNode.Parse(body)!.AsObject()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static long Id(string text) => long.Parse(text, CultureInfo.InvariantCulture);

    private static string? Query(Uri uri, string name) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).Where(p => p[0] == name).Select(p => p[1]).FirstOrDefault();

    private static HttpResponseMessage Json(JsonNode node, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private JsonObject Release(long id) => releases.First(r => (long)r["id"]! == id);

    private HttpResponseMessage Create(JsonObject request)
    {
        long id = AddRelease((string)request["tag_name"]!, (bool)request["draft"]!, (bool)request["prerelease"]!);
        Release(id)["body"] = (string)request["body"]!;
        return Json(Release(id), HttpStatusCode.Created);
    }

    private HttpResponseMessage Patch(long id, JsonObject fields)
    {
        JsonObject release = Release(id);
        foreach ((string name, JsonNode? value) in fields)
        {
            if (name == "make_latest")
            {
                LastMakeLatest = (string)value!;
                continue;
            }

            release[name] = value?.DeepClone();
        }

        return Json(release);
    }

    private HttpResponseMessage DeleteAsset(long id)
    {
        foreach (JsonObject release in releases)
        {
            JsonArray assets = release["assets"]!.AsArray();
            JsonNode? asset = assets.FirstOrDefault(a => (long)a!["id"]! == id);
            if (asset is not null)
            {
                assets.Remove(asset);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Redirect(long id)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri($"https://storage.example.test/assets/{id}");
        return response;
    }
}
