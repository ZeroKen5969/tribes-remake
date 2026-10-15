using System;
using System.Security.Cryptography;
using System.Text;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class ManifestTests
{
    private static UpdateManifest Sample() => new(
        AppVersion.Parse("1.2.0"),
        3,
        AppVersion.Parse("1.1.0"),
        "新增：更新按鈕\n修正：\"引號\" & <符號> +1",
        [
            new ManifestAsset("PrehistoricTribes-1.2.0-android.apk", UpdatePlatform.Android, AssetKind.App, 1234, new string('a', 64)),
            new ManifestAsset("PrehistoricTribes-1.2.0-windows.zip", UpdatePlatform.Windows, AssetKind.App, 5678, new string('b', 64)),
            new ManifestAsset("content-1.2.0-r3.pck", UpdatePlatform.Any, AssetKind.Content, 90, new string('c', 64)),
        ]);

    [Fact]
    public void RoundTrip_KeepsEverything()
    {
        UpdateManifest original = Sample();

        byte[] json = ManifestJson.Serialize(original);
        UpdateManifest parsed = ManifestJson.Parse(json);

        Assert.Equal(original.AppVersion, parsed.AppVersion);
        Assert.Equal(original.ContentRevision, parsed.ContentRevision);
        Assert.Equal(original.MinAppVersion, parsed.MinAppVersion);
        Assert.Equal(original.Notes, parsed.Notes);
        Assert.Equal(original.Assets, parsed.Assets);
        Assert.Equal("v1.2.0-r3", parsed.Tag.ToString());
        Assert.Equal(json, ManifestJson.Serialize(parsed));
    }

    [Fact]
    public void Serialize_KeepsNotesReadable()
    {
        string json = Encoding.UTF8.GetString(ManifestJson.Serialize(Sample()));

        Assert.Contains("新增：更新按鈕", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FindAsset_PrefersThePlatformThenAny()
    {
        UpdateManifest manifest = Sample();

        Assert.Equal(UpdatePlatform.Android, manifest.FindAsset(AssetKind.App, UpdatePlatform.Android)!.Platform);
        Assert.Equal("content-1.2.0-r3.pck", manifest.FindAsset(AssetKind.Content, UpdatePlatform.Windows)!.Name);
        Assert.Null(manifest.FindAsset(AssetKind.App, UpdatePlatform.Any));
    }

    [Fact]
    public void StandardNames()
    {
        AppVersion version = AppVersion.Parse("1.2.0");

        Assert.Equal("PrehistoricTribes-1.2.0-android.apk", UpdateManifest.AndroidAppName(version));
        Assert.Equal("PrehistoricTribes-1.2.0-windows.zip", UpdateManifest.WindowsAppName(version));
        Assert.Equal("content-1.2.0-r4.pck", UpdateManifest.ContentPackName(version, 4));
    }

    [Theory]
    [InlineData("../evil.pck")]
    [InlineData("a/b.pck")]
    [InlineData(".hidden")]
    [InlineData("manifest.json")]
    [InlineData("")]
    public void Constructor_RejectsUnsafeNames(string name)
    {
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1, [new ManifestAsset(name, UpdatePlatform.Any, AssetKind.Content, 1, new string('a', 64))]));
    }

    [Fact]
    public void Constructor_RejectsInconsistentAssets()
    {
        string sha = new('a', 64);
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1, [new ManifestAsset("a.pck", UpdatePlatform.Any, AssetKind.Content, -1, sha)]));
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1, [new ManifestAsset("a.pck", UpdatePlatform.Any, AssetKind.Content, 1, "ABC")]));
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1, [new ManifestAsset("a.apk", UpdatePlatform.Any, AssetKind.App, 1, sha)]));
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1, [new ManifestAsset("a.apk", (UpdatePlatform)9, AssetKind.App, 1, sha)]));
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1,
        [
            new ManifestAsset("a.pck", UpdatePlatform.Any, AssetKind.Content, 1, sha),
            new ManifestAsset("b.pck", UpdatePlatform.Any, AssetKind.Content, 1, sha),
        ]));
        Assert.Throws<ArgumentException>(() => TestData.Manifest("1.0.0", 1,
        [
            new ManifestAsset("a.pck", UpdatePlatform.Any, AssetKind.Content, 1, sha),
            new ManifestAsset("A.PCK", UpdatePlatform.Android, AssetKind.Content, 1, sha),
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestData.Manifest("1.0.0", -1, []));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"schemaVersion":2,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"x","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"1.0.0","contentRevision":"0","minAppVersion":"1.0.0","notes":"","assets":[]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[{"name":"a.pck","platform":"ios","kind":"content","size":1,"sha256":"aa"}]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[{"name":"a.pck","platform":"any","kind":"code","size":1,"sha256":"aa"}]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[{"name":"a.pck","platform":"any","kind":"content","size":1.5,"sha256":"aa"}]}""")]
    [InlineData("""{"schemaVersion":1,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"","assets":[]} trailing""")]
    public void Parse_RejectsBadManifests(string json)
    {
        UpdateException error = Assert.Throws<UpdateException>(() => ManifestJson.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal(UpdateErrorKind.InvalidManifest, error.Kind);
    }

    [Fact]
    public void Parse_RejectsOversizedManifest()
    {
        UpdateException error = Assert.Throws<UpdateException>(() => ManifestJson.Parse(new byte[ManifestJson.MaxBytes + 1]));

        Assert.Equal(UpdateErrorKind.InvalidManifest, error.Kind);
    }

    [Fact]
    public void Parse_IgnoresUnknownProperties()
    {
        string json = """{"schemaVersion":1,"future":true,"appVersion":"1.0.0","contentRevision":0,"minAppVersion":"1.0.0","notes":"n","assets":[]}""";

        Assert.Equal("n", ManifestJson.Parse(Encoding.UTF8.GetBytes(json)).Notes);
    }

    [Fact]
    public void Helpers_ValidateNamesAndHashes()
    {
        Assert.True(UpdateManifest.IsSafeFileName("content-1.0.0-r1.pck"));
        Assert.False(UpdateManifest.IsSafeFileName(new string('a', 129)));
        Assert.False(UpdateManifest.IsSafeFileName(null));
        Assert.True(UpdateManifest.IsSha256Hex(TestData.Sha([1])));
        Assert.False(UpdateManifest.IsSha256Hex(Convert.ToHexString(SHA256.HashData(new byte[1]))));
        Assert.False(UpdateManifest.IsSha256Hex(null));
        Assert.Equal(UpdatePlatform.Windows, ManifestJson.ParsePlatform("windows"));
        Assert.Equal("any", ManifestJson.PlatformName(UpdatePlatform.Any));
        Assert.Throws<FormatException>(() => ManifestJson.ParsePlatform("ios"));
    }
}
