using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class CheckerTests : IDisposable
{
    private static readonly byte[] Apk = TestData.Bytes(300, 1);
    private static readonly byte[] Zip = TestData.Bytes(400, 2);
    private static readonly byte[] Pack = TestData.Bytes(200, 3);

    private readonly ECDsa key = TestData.NewKey();
    private readonly FakeGitHub github = new();
    private readonly HttpClient http;

    public CheckerTests()
    {
        http = github.CreateHttpClient();
    }

    public void Dispose()
    {
        http.Dispose();
        github.Dispose();
        key.Dispose();
    }

    [Fact]
    public async Task NoReleases_IsUpToDate()
    {
        UpdateCheckResult result = await Check("1.0.0", 0, UpdatePlatform.Android);

        Assert.Equal(UpdateKind.None, result.Kind);
        Assert.Equal("", result.Notes);
        Assert.Null(result.Tag);
    }

    [Fact]
    public async Task NewerApp_IsAnAppUpdateForTheRightPlatform()
    {
        PublishFull("1.1.0", 0);

        UpdateCheckResult android = await Check("1.0.0", 0, UpdatePlatform.Android);
        UpdateCheckResult windows = await Check("1.0.0", 0, UpdatePlatform.Windows);

        Assert.Equal((UpdateKind.App, "PrehistoricTribes-1.1.0-android.apk"), (android.Kind, android.Asset!.Name));
        Assert.Equal((UpdateKind.App, "PrehistoricTribes-1.1.0-windows.zip"), (windows.Kind, windows.Asset!.Name));
        Assert.Equal(windows.Asset.Name, windows.RemoteAsset!.Name);
        Assert.Equal("v1.1.0", windows.Tag!.ToString());
        Assert.Equal("更新內容", windows.Notes);
    }

    [Fact]
    public async Task NewerContent_IsAContentUpdate_WhenTheAppIsRecentEnough()
    {
        PublishContent("1.0.0", 2, minApp: "1.0.0");

        UpdateCheckResult current = await Check("1.0.0", 1, UpdatePlatform.Android);
        UpdateCheckResult newerApp = await Check("1.0.5", 1, UpdatePlatform.Windows);
        UpdateCheckResult alreadyThere = await Check("1.0.0", 2, UpdatePlatform.Android);

        Assert.Equal(UpdateKind.Content, current.Kind);
        Assert.Equal(UpdateKind.Content, newerApp.Kind);
        Assert.Equal(UpdateKind.None, alreadyThere.Kind);
    }

    [Fact]
    public async Task ContentNeedingANewerApp_IsNotOffered()
    {
        PublishContent("1.2.0", 5, minApp: "1.2.0");

        UpdateCheckResult result = await Check("1.1.0", 1, UpdatePlatform.Android);

        Assert.Equal(UpdateKind.None, result.Kind);
        Assert.True(result.NewerContentNeedsNewerApp);
    }

    [Fact]
    public async Task NewestReleaseWithoutThisPlatform_FallsBackToOlderContent()
    {
        PublishContent("1.0.0", 3, minApp: "1.0.0");
        UpdateManifest androidOnly = TestData.Manifest("1.1.0", 3, [TestData.Asset("PrehistoricTribes-1.1.0-android.apk", Apk, AssetKind.App, UpdatePlatform.Android)]);
        github.AddSignedRelease(androidOnly, key, new Dictionary<string, byte[]> { ["PrehistoricTribes-1.1.0-android.apk"] = Apk });

        UpdateCheckResult result = await Check("1.0.0", 1, UpdatePlatform.Windows);

        Assert.Equal(UpdateKind.Content, result.Kind);
        Assert.Equal("v1.0.0-r3", result.Tag!.ToString());
    }

    [Fact]
    public async Task PrereleasesAndDrafts_AreSkippedUnlessAskedFor()
    {
        UpdateManifest beta = TestData.Manifest("2.0.0-beta", 0, [TestData.Asset("PrehistoricTribes-2.0.0-beta-windows.zip", Zip, AssetKind.App, UpdatePlatform.Windows)]);
        github.AddSignedRelease(beta, key, new Dictionary<string, byte[]> { ["PrehistoricTribes-2.0.0-beta-windows.zip"] = Zip }, prerelease: true);
        long draft = github.AddRelease("v3.0.0", draft: true);
        github.AddAsset(draft, "manifest.json", [1]);
        github.AddAsset(draft, "manifest.json.sig", [1]);

        UpdateCheckResult players = await Check("1.0.0", 0, UpdatePlatform.Windows);
        UpdateCheckResult testers = await Check("1.0.0", 0, UpdatePlatform.Windows, prereleases: true);

        Assert.Equal(UpdateKind.None, players.Kind);
        Assert.Equal((UpdateKind.App, "v2.0.0-beta"), (testers.Kind, testers.Tag!.ToString()));
    }

    [Fact]
    public async Task ReleasesWithoutSignatureOrWithOtherTags_AreIgnoredInTheList()
    {
        long unsigned = github.AddRelease("v5.0.0");
        github.AddAsset(unsigned, "manifest.json", [1]);
        long foreign = github.AddRelease("nightly");
        github.AddAsset(foreign, "manifest.json", [1]);
        github.AddAsset(foreign, "manifest.json.sig", [1]);

        Assert.Equal(UpdateKind.None, (await Check("1.0.0", 0, UpdatePlatform.Android)).Kind);
    }

    [Fact]
    public async Task TamperedManifest_IsRejected()
    {
        long release = PublishFull("1.1.0", 0);
        byte[] tampered = ManifestJson.Serialize(TestData.Manifest("1.1.0", 0, [TestData.Asset("PrehistoricTribes-1.1.0-windows.zip", TestData.Bytes(400, 9), AssetKind.App, UpdatePlatform.Windows)]));
        github.ReplaceAssetData(github.AssetId(release, "manifest.json"), tampered);

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Check("1.0.0", 0, UpdatePlatform.Windows));

        Assert.Equal(UpdateErrorKind.BadSignature, error.Kind);
    }

    [Fact]
    public async Task ManifestSignedByAnotherKey_IsRejected()
    {
        using ECDsa other = TestData.NewKey();
        UpdateManifest manifest = TestData.Manifest("1.1.0", 0, [TestData.Asset("PrehistoricTribes-1.1.0-windows.zip", Zip, AssetKind.App, UpdatePlatform.Windows)]);
        github.AddSignedRelease(manifest, other, new Dictionary<string, byte[]> { ["PrehistoricTribes-1.1.0-windows.zip"] = Zip });

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Check("1.0.0", 0, UpdatePlatform.Windows));

        Assert.Equal(UpdateErrorKind.BadSignature, error.Kind);
    }

    [Fact]
    public async Task ManifestUnderAnotherTag_IsRejected()
    {
        UpdateManifest old = TestData.Manifest("1.1.0", 0, [TestData.Asset("PrehistoricTribes-1.1.0-windows.zip", Zip, AssetKind.App, UpdatePlatform.Windows)]);
        github.AddSignedRelease(old, key, new Dictionary<string, byte[]> { ["PrehistoricTribes-1.1.0-windows.zip"] = Zip }, tag: "v9.0.0");

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Check("1.0.0", 0, UpdatePlatform.Windows));

        Assert.Equal(UpdateErrorKind.InvalidManifest, error.Kind);
    }

    [Fact]
    public async Task ManifestListingAMissingFile_IsRejected()
    {
        UpdateManifest manifest = TestData.Manifest("1.1.0", 0, [TestData.Asset("PrehistoricTribes-1.1.0-windows.zip", Zip, AssetKind.App, UpdatePlatform.Windows)]);
        github.AddSignedRelease(manifest, key, new Dictionary<string, byte[]>());

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Check("1.0.0", 0, UpdatePlatform.Windows));

        Assert.Equal(UpdateErrorKind.InvalidManifest, error.Kind);
    }

    [Fact]
    public async Task BrokenNewestRelease_DoesNotHideAValidOlderOne()
    {
        PublishContent("1.0.0", 2, minApp: "1.0.0");
        using ECDsa retired = TestData.NewKey();
        UpdateManifest newer = TestData.Manifest("1.0.0", 3, [TestData.Asset("content-1.0.0-r3.pck", Pack)]);
        github.AddSignedRelease(newer, retired, new Dictionary<string, byte[]> { ["content-1.0.0-r3.pck"] = Pack });

        UpdateCheckResult found = await Check("1.0.0", 1, UpdatePlatform.Android);
        UpdateException nothingElse = await Assert.ThrowsAsync<UpdateException>(() => Check("1.0.0", 2, UpdatePlatform.Android));

        Assert.Equal("v1.0.0-r2", found.Tag!.ToString());
        Assert.Equal(UpdateErrorKind.BadSignature, nothingElse.Kind);
    }

    [Fact]
    public async Task SignedPrereleaseVersion_IsNotOfferedEvenWithoutTheGitHubFlag()
    {
        UpdateManifest beta = TestData.Manifest("2.0.0-beta", 0, [TestData.Asset("PrehistoricTribes-2.0.0-beta-windows.zip", Zip, AssetKind.App, UpdatePlatform.Windows)]);
        github.AddSignedRelease(beta, key, new Dictionary<string, byte[]> { ["PrehistoricTribes-2.0.0-beta-windows.zip"] = Zip }, prerelease: false);

        Assert.Equal(UpdateKind.None, (await Check("1.0.0", 0, UpdatePlatform.Windows)).Kind);
        Assert.Equal(UpdateKind.App, (await Check("1.0.0", 0, UpdatePlatform.Windows, prereleases: true)).Kind);
    }

    [Fact]
    public async Task FetchVerified_RequiresManifestAndSignature()
    {
        var checker = new UpdateChecker(github.CreateClient(http), TestData.Trust(key));
        var noManifest = new GitHubRelease(1, "v1.0.0", "", "", false, false, null, []);
        var noSignature = noManifest with { Assets = [new GitHubAsset(2, "manifest.json", 1, "uploaded", null)] };

        Assert.Equal(UpdateErrorKind.BadSignature, (await Assert.ThrowsAsync<UpdateException>(() => checker.FetchVerifiedAsync(noManifest))).Kind);
        Assert.Equal(UpdateErrorKind.BadSignature, (await Assert.ThrowsAsync<UpdateException>(() => checker.FetchVerifiedAsync(noSignature))).Kind);
    }

    [Fact]
    public void Decide_PrefersAppOverContent()
    {
        UpdateManifest manifest = TestData.Manifest("1.1.0", 4,
        [
            TestData.Asset("PrehistoricTribes-1.1.0-android.apk", Apk, AssetKind.App, UpdatePlatform.Android),
            TestData.Asset("content-1.1.0-r4.pck", Pack),
        ], minApp: "1.0.0");
        var release = new VerifiedRelease(new GitHubRelease(1, "v1.1.0-r4", "", "", false, false, null, []), manifest);

        Assert.Equal(UpdateKind.App, UpdateChecker.Decide(Options("1.0.0", 0, UpdatePlatform.Android), release).Kind);
        Assert.Equal(UpdateKind.Content, UpdateChecker.Decide(Options("1.0.0", 0, UpdatePlatform.Windows), release).Kind);
        Assert.Equal(UpdateKind.None, UpdateChecker.Decide(Options("1.1.0", 4, UpdatePlatform.Android), release).Kind);
    }

    private static UpdateCheckOptions Options(string version, int revision, UpdatePlatform platform, bool prereleases = false) => new()
    {
        CurrentAppVersion = AppVersion.Parse(version),
        InstalledContentRevision = revision,
        Platform = platform,
        IncludePrereleases = prereleases,
    };

    private Task<UpdateCheckResult> Check(string version, int revision, UpdatePlatform platform, bool prereleases = false) =>
        new UpdateChecker(github.CreateClient(http), TestData.Trust(key)).CheckAsync(Options(version, revision, platform, prereleases));

    private long PublishFull(string version, int revision)
    {
        string apk = $"PrehistoricTribes-{version}-android.apk";
        string zip = $"PrehistoricTribes-{version}-windows.zip";
        UpdateManifest manifest = TestData.Manifest(version, revision,
        [
            TestData.Asset(apk, Apk, AssetKind.App, UpdatePlatform.Android),
            TestData.Asset(zip, Zip, AssetKind.App, UpdatePlatform.Windows),
        ]);
        return github.AddSignedRelease(manifest, key, new Dictionary<string, byte[]> { [apk] = Apk, [zip] = Zip });
    }

    private long PublishContent(string version, int revision, string minApp)
    {
        string name = $"content-{version}-r{revision}.pck";
        UpdateManifest manifest = TestData.Manifest(version, revision, [TestData.Asset(name, Pack)], minApp);
        return github.AddSignedRelease(manifest, key, new Dictionary<string, byte[]> { [name] = Pack });
    }
}
