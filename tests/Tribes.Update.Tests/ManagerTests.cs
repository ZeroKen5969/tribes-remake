using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class ManagerTests : IDisposable
{
    private readonly ECDsa key = TestData.NewKey();
    private readonly FakeGitHub github = new();
    private readonly HttpClient http;
    private readonly TempDir temp = new();

    public ManagerTests()
    {
        http = github.CreateHttpClient();
    }

    public void Dispose()
    {
        http.Dispose();
        github.Dispose();
        temp.Dispose();
        key.Dispose();
    }

    [Fact]
    public async Task ContentUpdate_IsDownloadedRecordedAndLoadedNextStart()
    {
        byte[] pack = TestData.Bytes(4000, 5);
        github.AddSignedRelease(
            TestData.Manifest("1.0.0", 2, [TestData.Asset("content-1.0.0-r2.pck", pack)]),
            key,
            new Dictionary<string, byte[]> { ["content-1.0.0-r2.pck"] = pack });
        using UpdateManager manager = Manager(UpdatePlatform.Android);
        Assert.Null(manager.GetContentPackToLoad());

        UpdateCheckResult check = await manager.CheckAsync();
        string file = await manager.DownloadAsync(check);
        UpdateApplyOutcome outcome = await manager.ApplyAsync(check, file);

        Assert.Equal(UpdateApplyOutcome.RestartToApply, outcome);
        Assert.Equal(file, manager.GetContentPackToLoad(verifyHash: true));
        Assert.Equal(UpdateKind.None, (await manager.CheckAsync()).Kind);
    }

    [Fact]
    public async Task AndroidUpdate_AsksForPermissionThenStartsTheInstaller()
    {
        byte[] apk = TestData.Bytes(3000, 6);
        PublishApp("1.1.0", "PrehistoricTribes-1.1.0-android.apk", apk, UpdatePlatform.Android);
        var installer = new FakeInstaller();
        using UpdateManager manager = Manager(UpdatePlatform.Android, installer: installer);
        Directory.CreateDirectory(Path.Combine(temp.Path, "app"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "app", "PrehistoricTribes-1.0.0-android.apk"), "old");

        UpdateCheckResult check = await manager.CheckAsync();
        string file = await manager.DownloadAsync(check);

        Assert.Equal(UpdateApplyOutcome.PermissionRequested, await manager.ApplyAsync(check, file));
        installer.Allowed = true;
        Assert.Equal(UpdateApplyOutcome.InstallerStarted, await manager.ApplyAsync(check, file));
        Assert.Equal([file], installer.Launched);
        Assert.Equal(1, installer.PermissionRequests);
        Assert.False(File.Exists(Path.Combine(temp.Path, "app", "PrehistoricTribes-1.0.0-android.apk")));
    }

    [Fact]
    public async Task WindowsUpdate_StagesAndSchedulesTheSwap()
    {
        string install = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(install, "Game.exe"), "old");
        byte[] zip = GameZip();
        PublishApp("1.1.0", "PrehistoricTribes-1.1.0-windows.zip", zip, UpdatePlatform.Windows);
        var launcher = new FakeLauncher();
        var windows = new WindowsUpdateOptions
        {
            InstallDirectory = install,
            ExecutablePath = Path.Combine(install, "Game.exe"),
            WorkDirectory = Path.Combine(temp.Path, "windows"),
        };
        using UpdateManager manager = Manager(UpdatePlatform.Windows, windows: windows, launcher: launcher);

        UpdateCheckResult check = await manager.CheckAsync();
        string file = await manager.DownloadAsync(check);

        Assert.Equal(UpdateApplyOutcome.QuitToFinish, await manager.ApplyAsync(check, file));
        Assert.Single(launcher.Started);
        Assert.True(File.Exists(Path.Combine(install, WindowsAppUpdater.StagingFolderName, "Game.exe")));
    }

    [Fact]
    public async Task Apply_RejectsMissingOptionsAndUnverifiedFiles()
    {
        byte[] apk = TestData.Bytes(3000, 6);
        PublishApp("1.1.0", "PrehistoricTribes-1.1.0-android.apk", apk, UpdatePlatform.Android);
        using UpdateManager manager = Manager(UpdatePlatform.Android);
        UpdateCheckResult check = await manager.CheckAsync();
        string file = await manager.DownloadAsync(check);
        string other = temp.File("other.apk");
        await File.WriteAllBytesAsync(other, TestData.Bytes(3000, 7));

        Assert.Equal(UpdateErrorKind.Unsupported, (await Assert.ThrowsAsync<UpdateException>(() => manager.ApplyAsync(check, file))).Kind);
        Assert.Equal(UpdateErrorKind.IntegrityMismatch, (await Assert.ThrowsAsync<UpdateException>(() => manager.ApplyAsync(check, other))).Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => manager.ApplyAsync(UpdateCheckResult.UpToDate(), file));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.DownloadAsync(UpdateCheckResult.UpToDate()));
    }

    [Fact]
    public async Task Apply_WindowsWithoutOptions_IsUnsupported()
    {
        byte[] zip = GameZip();
        PublishApp("1.1.0", "PrehistoricTribes-1.1.0-windows.zip", zip, UpdatePlatform.Windows);
        using UpdateManager manager = Manager(UpdatePlatform.Windows);
        UpdateCheckResult check = await manager.CheckAsync();
        string file = await manager.DownloadAsync(check);

        Assert.Equal(UpdateErrorKind.Unsupported, (await Assert.ThrowsAsync<UpdateException>(() => manager.ApplyAsync(check, file))).Kind);
    }

    [Fact]
    public async Task ContentPack_MustBeAppliedFromItsDownloadPlace()
    {
        byte[] pack = TestData.Bytes(4000, 5);
        github.AddSignedRelease(
            TestData.Manifest("1.0.0", 2, [TestData.Asset("content-1.0.0-r2.pck", pack)]),
            key,
            new Dictionary<string, byte[]> { ["content-1.0.0-r2.pck"] = pack });
        using UpdateManager manager = Manager(UpdatePlatform.Windows);
        UpdateCheckResult check = await manager.CheckAsync();
        string elsewhere = temp.File("copy.pck");
        await File.WriteAllBytesAsync(elsewhere, pack);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.ApplyAsync(check, elsewhere));
    }

    [Fact]
    public void UnsupportedInstaller_ReportsUnsupported()
    {
        var installer = new UnsupportedAppInstaller();

        Assert.False(installer.CanRequestInstall);
        Assert.Equal(UpdateErrorKind.Unsupported, Assert.Throws<UpdateException>(installer.RequestInstallPermission).Kind);
        Assert.Equal(UpdateErrorKind.Unsupported, Assert.Throws<UpdateException>(() => installer.LaunchInstaller("x.apk")).Kind);
    }

    [Fact]
    public void EmbeddedConfig_IsConsistent()
    {
        var options = new UpdateManagerOptions
        {
            AppVersion = AppVersion.Parse("1.0.0"),
            BuiltInContentRevision = 0,
            Platform = UpdatePlatform.Windows,
            DataDirectory = temp.Path,
        };

        if (EmbeddedUpdateConfig.IsConfigured)
        {
            Assert.True(GitHubReleaseClient.IsRepositoryName(EmbeddedUpdateConfig.Repository));
            using UpdateManager manager = UpdateManager.CreateDefault(options);
            Assert.NotNull(manager.ContentPacks);
        }
        else
        {
            Assert.Equal(UpdateErrorKind.Unsupported, Assert.Throws<UpdateException>(() => UpdateManager.CreateDefault(options)).Kind);
        }
    }

    [Fact]
    public void TokenMask_RoundTrips()
    {
        byte[] mask = SHA256.HashData([1, 2, 3]);

        byte[] concealed = TokenMask.Conceal("synthetic_value_for_test", mask);

        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes("synthetic_value_for_test"), concealed);
        Assert.Equal("synthetic_value_for_test", TokenMask.Reveal(concealed, mask));
        Assert.Empty(TokenMask.Apply([1, 2], []));
    }

    private UpdateManager Manager(UpdatePlatform platform, IAppInstaller? installer = null, WindowsUpdateOptions? windows = null, IProcessLauncher? launcher = null) => new(
        new UpdateManagerOptions
        {
            AppVersion = AppVersion.Parse("1.0.0"),
            BuiltInContentRevision = 1,
            Platform = platform,
            DataDirectory = temp.Path,
            AppInstaller = installer,
            Windows = windows,
            Download = new DownloadOptions { FreeSpace = _ => null },
        },
        github.CreateClient(http),
        TestData.Trust(key),
        launcher);

    private void PublishApp(string version, string name, byte[] bytes, UpdatePlatform platform) =>
        github.AddSignedRelease(
            TestData.Manifest(version, 0, [TestData.Asset(name, bytes, AssetKind.App, platform)]),
            key,
            new Dictionary<string, byte[]> { [name] = bytes });

    private static byte[] GameZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("Game.exe").Open());
            writer.Write("new");
        }

        return buffer.ToArray();
    }

    private sealed class FakeInstaller : IAppInstaller
    {
        public bool Allowed { get; set; }

        public int PermissionRequests { get; private set; }

        public List<string> Launched { get; } = [];

        public bool CanRequestInstall => Allowed;

        public void RequestInstallPermission() => PermissionRequests++;

        public void LaunchInstaller(string packagePath) => Launched.Add(packagePath);
    }

    private sealed class FakeLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> Started { get; } = [];

        public void Start(ProcessStartInfo startInfo) => Started.Add(startInfo);
    }
}
