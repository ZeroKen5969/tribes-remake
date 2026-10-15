using System;
using System.IO;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class ContentPackStoreTests : IDisposable
{
    private static readonly byte[] Pack = TestData.Bytes(5000, 4);
    private static readonly AppVersion App = AppVersion.Parse("1.2.0");

    private readonly TempDir temp = new();
    private readonly ContentPackStore store;
    private readonly ManifestAsset asset = TestData.Asset("content-1.2.0-r3.pck", Pack);
    private readonly UpdateManifest manifest;

    public ContentPackStoreTests()
    {
        store = new ContentPackStore(Path.Combine(temp.Path, "content"));
        manifest = TestData.Manifest("1.2.0", 3, [asset], minApp: "1.1.0");
    }

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Install_RecordsThePackForTheNextStart()
    {
        Place(Pack);

        InstalledContentPack installed = store.Install(manifest, asset);

        Assert.Equal(installed, store.ReadState());
        Assert.Equal(store.PathFor(asset), store.GetPackToLoad(App, builtInContentRevision: 1, verifyHash: true));
        Assert.Equal(3, store.GetCurrentRevision(App, 1));
        Assert.Equal(store.PathFor(asset), store.GetPackToLoad(AppVersion.Parse("1.1.0"), 1));
    }

    [Fact]
    public void Pack_IsIgnoredWhenTheBuiltInContentIsNewerOrTheAppTooOld()
    {
        Place(Pack);
        store.Install(manifest, asset);

        Assert.Null(store.GetPackToLoad(AppVersion.Parse("1.3.0"), builtInContentRevision: 3));
        Assert.Equal(5, store.GetCurrentRevision(AppVersion.Parse("1.3.0"), 5));
        Assert.Null(store.GetPackToLoad(AppVersion.Parse("1.0.9"), 0));
    }

    [Fact]
    public void Pack_IsIgnoredWhenTheFileIsMissingOrDamaged()
    {
        Place(Pack);
        store.Install(manifest, asset);
        byte[] damaged = (byte[])Pack.Clone();
        damaged[0] ^= 1;
        Place(damaged);

        Assert.NotNull(store.GetPackToLoad(App, 0));
        Assert.Null(store.GetPackToLoad(App, 0, verifyHash: true));

        Place(Pack[..10]);
        Assert.Null(store.GetPackToLoad(App, 0));

        File.Delete(store.PathFor(asset));
        Assert.Null(store.GetPackToLoad(App, 0));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"contentRevision\":3}")]
    [InlineData("{\"contentRevision\":3,\"file\":\"../x.pck\",\"appVersion\":\"1.0.0\",\"minAppVersion\":\"1.0.0\",\"size\":1,\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}")]
    [InlineData("{\"contentRevision\":3,\"file\":\"x.pck\",\"appVersion\":\"bad\",\"minAppVersion\":\"1.0.0\",\"size\":1,\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}")]
    public void DamagedRecord_MeansNoPack(string json)
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(Path.Combine(store.Directory, ContentPackStore.StateFileName), json);

        Assert.Null(store.ReadState());
        Assert.Null(store.GetPackToLoad(App, 0));
        Assert.Equal(0, store.GetCurrentRevision(App, 0));
    }

    [Fact]
    public void RecordWithInvalidUtf8_MeansNoPack()
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllBytes(Path.Combine(store.Directory, ContentPackStore.StateFileName), [.. "{\"file\":\""u8.ToArray(), 0xC3, 0x28, .. "\"}"u8.ToArray()]);

        Assert.Null(store.ReadState());
    }

    [Fact]
    public void Install_RequiresTheDownloadedFile()
    {
        UpdateException error = Assert.Throws<UpdateException>(() => store.Install(manifest, asset));

        Assert.Equal(UpdateErrorKind.IntegrityMismatch, error.Kind);
    }

    [Fact]
    public void Uninstall_ForgetsThePack()
    {
        Place(Pack);
        store.Install(manifest, asset);

        store.Uninstall();
        store.Uninstall();

        Assert.Null(store.ReadState());
        Assert.Null(store.GetPackToLoad(App, 0));
    }

    [Fact]
    public void RemoveStaleFiles_KeepsOnlyTheRecordedPack()
    {
        Assert.Empty(store.RemoveStaleFiles());
        Place(Pack);
        store.Install(manifest, asset);
        string old = Path.Combine(store.Directory, "content-1.1.0-r2.pck");
        string part = Path.Combine(store.Directory, "content-1.3.0-r4.pck.part");
        string other = Path.Combine(store.Directory, "notes.txt");
        File.WriteAllText(old, "old");
        File.WriteAllText(part, "part");
        File.WriteAllText(other, "keep");

        Assert.Empty(store.RemoveStaleFiles());

        Assert.False(File.Exists(old));
        Assert.False(File.Exists(part));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(store.PathFor(asset)));
    }

    [Fact]
    public void RemoveStaleFiles_ReportsFilesInUse()
    {
        Directory.CreateDirectory(store.Directory);
        string old = Path.Combine(store.Directory, "content-1.0.0-r1.pck");
        File.WriteAllText(old, "old");
        using FileStream locked = new(old, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(OperatingSystem.IsWindows() ? [old] : [], store.RemoveStaleFiles());
    }

    [Fact]
    public void PathFor_AcceptsOnlyContentPacks()
    {
        Assert.Throws<ArgumentException>(() => store.PathFor(TestData.Asset("a.apk", Pack, AssetKind.App, UpdatePlatform.Android)));
    }

    private void Place(byte[] bytes)
    {
        Directory.CreateDirectory(store.Directory);
        File.WriteAllBytes(store.PathFor(asset), bytes);
    }
}
