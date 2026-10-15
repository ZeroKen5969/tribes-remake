using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class WindowsUpdaterTests : IDisposable
{
    private readonly TempDir temp = new();
    private readonly string install;
    private readonly string work;

    public WindowsUpdaterTests()
    {
        install = Path.Combine(temp.Path, "Game 遊戲");
        work = Path.Combine(temp.Path, "user", "updates", "windows");
        Write("Game.exe", "old exe");
        Write("data/a.txt", "old a");
        Write("old_only.txt", "old");
        Write("editor_data/save.txt", "save");
    }

    public void Dispose() => temp.Dispose();

    [Fact]
    public async Task Stage_UnpacksTheZip()
    {
        WindowsAppUpdater updater = Updater();

        await updater.StageAsync(NewZip());

        Assert.Equal("new exe", File.ReadAllText(Path.Combine(updater.StagingDirectory, "Game.exe")));
        Assert.Equal("new a", File.ReadAllText(Path.Combine(updater.StagingDirectory, "data", "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(updater.StagingDirectory, "empty")));
    }

    [Fact]
    public async Task Stage_StripsASingleTopFolderAndClearsLeftovers()
    {
        WindowsAppUpdater updater = Updater();
        Write(WindowsAppUpdater.StagingFolderName + "/junk.txt", "junk");
        Write(WindowsAppUpdater.BackupFolderName + "/junk.txt", "junk");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, WindowsAppUpdater.ResultFileName), "{\"status\":\"succeeded\"}");

        await updater.StageAsync(Zip(("PrehistoricTribes-1.1.0/Game.exe", "new exe"), ("PrehistoricTribes-1.1.0/data/a.txt", "new a")));

        Assert.True(File.Exists(Path.Combine(updater.StagingDirectory, "Game.exe")));
        Assert.False(File.Exists(Path.Combine(updater.StagingDirectory, "junk.txt")));
        Assert.False(Directory.Exists(Path.Combine(install, WindowsAppUpdater.BackupFolderName)));
    }

    [Fact]
    public async Task Stage_KeepsABackupThatMayHoldTheOnlyOldFiles()
    {
        WindowsAppUpdater updater = Updater();
        Write(WindowsAppUpdater.BackupFolderName + "/Game.exe", "old exe");

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => updater.StageAsync(NewZip()));

        Assert.Equal(UpdateErrorKind.InstallFailed, error.Kind);
        Assert.True(File.Exists(Path.Combine(install, WindowsAppUpdater.BackupFolderName, "Game.exe")));

        File.Delete(Path.Combine(install, WindowsAppUpdater.BackupFolderName, "Game.exe"));
        await updater.StageAsync(NewZip());
        Assert.False(Directory.Exists(Path.Combine(install, WindowsAppUpdater.BackupFolderName)));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("data/../../evil.txt")]
    [InlineData("data/stream.txt:hidden")]
    public async Task Stage_RejectsEntriesOutsideTheFolder(string entry)
    {
        WindowsAppUpdater updater = Updater();

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => updater.StageAsync(Zip(("Game.exe", "new"), (entry, "x"))));

        Assert.Equal(UpdateErrorKind.InstallFailed, error.Kind);
        Assert.False(Directory.Exists(updater.StagingDirectory));
        Assert.False(File.Exists(Path.Combine(temp.Path, "evil.txt")));
    }

    [Fact]
    public async Task Stage_RequiresTheExecutableAndAValidZip()
    {
        WindowsAppUpdater updater = Updater();
        string notZip = temp.File("broken.zip");
        await File.WriteAllTextAsync(notZip, "not a zip");

        UpdateException missingExe = await Assert.ThrowsAsync<UpdateException>(() => updater.StageAsync(Zip(("Other.exe", "x"))));
        UpdateException damaged = await Assert.ThrowsAsync<UpdateException>(() => updater.StageAsync(notZip));

        Assert.Equal(UpdateErrorKind.InstallFailed, missingExe.Kind);
        Assert.Equal(UpdateErrorKind.InstallFailed, damaged.Kind);
        Assert.Equal(UpdateErrorKind.StorageError, (await Assert.ThrowsAsync<UpdateException>(() => updater.StageAsync(temp.File("missing.zip")))).Kind);
    }

    [Fact]
    public void Constructor_RequiresTheExecutableInsideTheInstall()
    {
        Assert.Throws<ArgumentException>(() => new WindowsAppUpdater(new WindowsUpdateOptions
        {
            InstallDirectory = install,
            ExecutablePath = temp.File("Elsewhere.exe"),
            WorkDirectory = work,
        }));
    }

    [Fact]
    public async Task WriteSwapFiles_WritesPlanAndScript()
    {
        WindowsAppUpdater updater = Updater(relaunchArguments: ["--updated", "two words"]);
        Assert.Equal(UpdateErrorKind.InstallFailed, Assert.Throws<UpdateException>(() => updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), 1, null)).Kind);
        await updater.StageAsync(NewZip());

        string planPath = updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), 4242, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        using JsonDocument plan = JsonDocument.Parse(await File.ReadAllBytesAsync(planPath));
        Assert.Equal(4242, plan.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal(install, plan.RootElement.GetProperty("installDir").GetString());
        Assert.Equal("--updated \"two words\"", plan.RootElement.GetProperty("relaunchArguments").GetString());
        Assert.Equal(Path.Combine(install, "Game.exe"), plan.RootElement.GetProperty("relaunchPath").GetString());
        byte[] script = await File.ReadAllBytesAsync(Path.Combine(work, WindowsAppUpdater.ScriptFileName));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, script[..3]);
        Assert.Equal("notRun", updater.ReadLastResult()!.Status);
    }

    [Fact]
    public async Task ScheduleSwap_StartsHiddenPowerShellForThisProcess()
    {
        var launcher = new RecordingLauncher();
        WindowsAppUpdater updater = Updater(launcher: launcher);
        await updater.StageAsync(NewZip());

        updater.ScheduleSwap(AppVersion.Parse("1.1.0"));

        ProcessStartInfo info = Assert.Single(launcher.Started);
        Assert.EndsWith("powershell.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);
        Assert.True(info.UseShellExecute);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File"], info.ArgumentList.Take(7));
        using JsonDocument plan = JsonDocument.Parse(await File.ReadAllBytesAsync(info.ArgumentList[^1]));
        Assert.Equal(Environment.ProcessId, plan.RootElement.GetProperty("pid").GetInt32());
        Assert.True(plan.RootElement.GetProperty("processStartUtcTicks").GetInt64() > 0);
    }

    [WindowsFact]
    public async Task Swap_ReplacesTheInstallAfterTheGameExits()
    {
        WindowsAppUpdater updater = Updater();
        await updater.StageAsync(NewZip());
        using Process game = StartShortProcess();
        string plan = updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), game.Id, game.StartTime.ToUniversalTime());

        int exit = await RunScriptAsync(plan);

        Assert.True(game.HasExited);
        Assert.Equal(0, exit);
        Assert.Equal("new exe", Read("Game.exe"));
        Assert.Equal("new a", Read("data/a.txt"));
        Assert.Equal("new", Read("new_only.txt"));
        Assert.False(File.Exists(Path.Combine(install, "old_only.txt")));
        Assert.Equal("save", Read("editor_data/save.txt"));
        Assert.False(Directory.Exists(updater.StagingDirectory));
        Assert.False(Directory.Exists(Path.Combine(install, WindowsAppUpdater.BackupFolderName)));
        WindowsSwapResult result = updater.ReadLastResult()!;
        Assert.True(result.Succeeded);
        Assert.Equal("1.1.0", result.Version);

        updater.ClearLastResult();
        Assert.Null(updater.ReadLastResult());
    }

    [WindowsFact]
    public async Task Swap_RollsBackWhenAnOldFileIsLocked()
    {
        Write("zz_locked.bin", "locked");
        WindowsAppUpdater updater = Updater();
        await updater.StageAsync(NewZip());
        string plan = updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), 0, null);

        int exit;
        using (new FileStream(Path.Combine(install, "zz_locked.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exit = await RunScriptAsync(plan);
        }

        Assert.Equal(1, exit);
        Assert.Equal("old exe", Read("Game.exe"));
        Assert.Equal("old a", Read("data/a.txt"));
        Assert.Equal("old", Read("old_only.txt"));
        Assert.False(File.Exists(Path.Combine(install, "new_only.txt")));
        Assert.False(Directory.Exists(Path.Combine(install, WindowsAppUpdater.BackupFolderName)));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(updater.StagingDirectory, "Game.exe")));
        WindowsSwapResult result = updater.ReadLastResult()!;
        Assert.Equal("rolledBack", result.Status);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Detail);
    }

    [WindowsFact]
    public async Task Swap_RollsBackWhenANewFileCannotBePlaced()
    {
        WindowsAppUpdater updater = Updater();
        await updater.StageAsync(NewZip());
        string plan = updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), 0, null);

        int exit;
        using (new FileStream(Path.Combine(updater.StagingDirectory, "new_only.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exit = await RunScriptAsync(plan);
        }

        Assert.Equal(1, exit);
        Assert.Equal("old exe", Read("Game.exe"));
        Assert.Equal("old a", Read("data/a.txt"));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(updater.StagingDirectory, "Game.exe")));
        Assert.Equal("rolledBack", updater.ReadLastResult()!.Status);
    }

    [WindowsFact]
    public async Task Swap_RefusesWithoutStagingOrWithALeftoverBackup()
    {
        WindowsAppUpdater updater = Updater();
        await updater.StageAsync(NewZip());
        string plan = updater.WriteSwapFiles(AppVersion.Parse("1.1.0"), 0, null);
        Directory.CreateDirectory(Path.Combine(install, WindowsAppUpdater.BackupFolderName));

        Assert.Equal(2, await RunScriptAsync(plan));
        Assert.Equal("failed", updater.ReadLastResult()!.Status);
        Assert.Equal("old exe", Read("Game.exe"));

        Directory.Delete(Path.Combine(install, WindowsAppUpdater.BackupFolderName));
        Directory.Delete(updater.StagingDirectory, recursive: true);
        Assert.Equal(2, await RunScriptAsync(plan));
        Assert.Equal("failed", updater.ReadLastResult()!.Status);
    }

    [WindowsFact]
    public async Task SwapScript_DoesNotInheritTheGamesOpenFiles()
    {
        // Godot opens files through the C runtime, whose handles are inheritable. A swap script
        // that inherited them would keep files of the install directory open after the game exits.
        string held = Path.Combine(install, "held.pck");
        File.WriteAllText(held, "pack");
        string script = temp.File("sleep.ps1");
        File.WriteAllText(script, "param([string]$PlanPath) Set-Content -LiteralPath $PlanPath -Value started; Start-Sleep -Seconds 4");
        string marker = Path.Combine(install, "marker 標記.txt");
        var handle = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Inheritable);

        new WindowsAppUpdater.SystemProcessLauncher().Start(WindowsAppUpdater.CreateStartInfo(script, marker));
        handle.Dispose();

        File.Move(held, held + ".moved");
        Assert.True(File.Exists(held + ".moved"));

        // The arguments arrive intact (the path has a space and CJK characters).
        for (int i = 0; i < 150 && !File.Exists(marker); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void ReadLastResult_ReportsUnreadableResults()
    {
        WindowsAppUpdater updater = Updater();
        Assert.Null(updater.ReadLastResult());
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, WindowsAppUpdater.ResultFileName), "garbage");

        Assert.Equal("failed", updater.ReadLastResult()!.Status);
    }

    [Fact]
    public void ClearLastResult_DoesNothingBeforeTheWorkFolderExists()
    {
        WindowsAppUpdater updater = Updater();
        Assert.False(Directory.Exists(work));

        updater.ClearLastResult();

        Assert.Null(updater.ReadLastResult());
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\dir with space\\", "\"C:\\dir with space\\\\\"")]
    [InlineData("a\\\\\"b c", "\"a\\\\\\\\\\\"b c\"")]
    public void QuoteArgument_FollowsWindowsRules(string argument, string expected)
    {
        Assert.Equal(expected, WindowsAppUpdater.QuoteArgument(argument));
    }

    private WindowsAppUpdater Updater(IReadOnlyList<string>? relaunchArguments = null, IProcessLauncher? launcher = null) => new(
        new WindowsUpdateOptions
        {
            InstallDirectory = install,
            ExecutablePath = Path.Combine(install, "Game.exe"),
            WorkDirectory = work,
            Relaunch = relaunchArguments is not null,
            RelaunchArguments = relaunchArguments ?? [],
            MoveAttempts = 2,
            MoveRetryDelay = TimeSpan.FromMilliseconds(50),
            ExitTimeout = TimeSpan.FromSeconds(30),
        },
        launcher ?? new RecordingLauncher());

    private string NewZip() => Zip(("Game.exe", "new exe"), ("data/a.txt", "new a"), ("new_only.txt", "new"), ("empty/", null));

    private string Zip(params (string Name, string? Text)[] entries)
    {
        string path = temp.File(Guid.NewGuid().ToString("N") + ".zip");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string name, string? text) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                if (text is not null)
                {
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(text);
                }
            }
        }

        return path;
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(install, relative));

    private static Process StartShortProcess() =>
        Process.Start(new ProcessStartInfo("ping", "-n 3 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;

    private async Task<int> RunScriptAsync(string planPath)
    {
        ProcessStartInfo info = WindowsAppUpdater.CreateStartInfo(Path.Combine(work, WindowsAppUpdater.ScriptFileName), planPath);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        using Process process = Process.Start(info)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        Assert.Equal("", (await errors).Trim());
        return process.ExitCode;
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public List<ProcessStartInfo> Started { get; } = [];

        public void Start(ProcessStartInfo startInfo) => Started.Add(startInfo);
    }
}

/// <summary>A test that needs Windows (it runs the PowerShell swap script).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows PowerShell.";
        }
    }
}
