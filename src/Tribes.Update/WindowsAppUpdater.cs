using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Update;

/// <summary>Where the Windows game is installed and how its update is swapped in.</summary>
public sealed class WindowsUpdateOptions
{
    /// <summary>Entries of the install directory that an update leaves alone (Godot's self-contained mode keeps user data there).</summary>
    public static readonly IReadOnlyList<string> DefaultPreservedEntries = ["_sc_", "._sc_", "editor_data"];

    /// <summary>The directory the game runs from (the directory of <c>OS.GetExecutablePath()</c>).</summary>
    public required string InstallDirectory { get; init; }

    /// <summary>The game executable, inside <see cref="InstallDirectory"/>; started again after the swap.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>A directory for the swap plan, script, log and result (e.g. <c>user://updates/windows</c>).</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>Install directory entries the swap does not touch.</summary>
    public IReadOnlyList<string> PreservedEntries { get; init; } = DefaultPreservedEntries;

    /// <summary>Arguments for the relaunched game.</summary>
    public IReadOnlyList<string> RelaunchArguments { get; init; } = [];

    /// <summary>Whether the swap starts the game again when done.</summary>
    public bool Relaunch { get; init; } = true;

    /// <summary>How long the swap waits for the game to exit.</summary>
    public TimeSpan ExitTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Attempts per moved entry (files can stay locked briefly after exit).</summary>
    public int MoveAttempts { get; init; } = 20;

    /// <summary>The wait between move attempts.</summary>
    public TimeSpan MoveRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>Starts processes (replaced in tests).</summary>
public interface IProcessLauncher
{
    /// <summary>Starts a process and does not wait for it.</summary>
    void Start(ProcessStartInfo startInfo);
}

/// <summary>The outcome of the last swap, as the swap script recorded it.</summary>
/// <param name="Status"><c>succeeded</c>, <c>rolledBack</c>, <c>rollbackFailed</c>, <c>failed</c>, or <c>notRun</c> (scheduled, but no result was written).</param>
/// <param name="Detail">The failure reason, empty on success.</param>
/// <param name="Version">The version that was being installed.</param>
public sealed record WindowsSwapResult(string Status, string Detail, string Version)
{
    /// <summary>Whether the new version is installed.</summary>
    public bool Succeeded => Status == "succeeded";
}

/// <summary>
/// Installs a Windows app update. <see cref="StageAsync"/> unpacks the verified zip into a staging
/// folder inside the install directory while the game runs; <see cref="ScheduleSwap"/> writes a
/// plan and a PowerShell script and starts it; the game then quits. The script waits for the
/// game to exit, moves the old files into a backup folder and the staged files into place, moves
/// everything back if a step fails, and starts the game again. Only the game's own files move:
/// those on the installed <see cref="FileListName"/> and on the update's. Other files in the
/// install directory are never touched, so a game unpacked into a shared folder is safe to update.
/// </summary>
public sealed class WindowsAppUpdater
{
    /// <summary>The staging folder inside the install directory.</summary>
    public const string StagingFolderName = "_update_staging";

    /// <summary>The backup folder inside the install directory, present only during a swap.</summary>
    public const string BackupFolderName = "_update_backup";

    /// <summary>The plan file in the work directory.</summary>
    public const string PlanFileName = "swap-plan.json";

    /// <summary>The script file in the work directory.</summary>
    public const string ScriptFileName = "swap.ps1";

    /// <summary>The result file in the work directory.</summary>
    public const string ResultFileName = "swap-result.json";

    /// <summary>The log file in the work directory.</summary>
    public const string LogFileName = "swap.log";

    /// <summary>
    /// The list of the game's files in the install directory: one path per line, relative and
    /// '/'-separated, the list itself included. The package ships it (for a game unpacked by hand)
    /// and staging rewrites it from the staged files; the swap moves only the files on the
    /// installed list and the staged one.
    /// </summary>
    public const string FileListName = "install-files.txt";

    private const string ScriptResource = "Tribes.Update.WindowsSwap.ps1";
    private const long ExtractMargin = 16 * 1024 * 1024;

    private readonly WindowsUpdateOptions options;
    private readonly IProcessLauncher launcher;
    private readonly string installDirectory;
    private readonly string executableRelative;

    /// <summary>Creates an updater; the executable must be inside the install directory.</summary>
    public WindowsAppUpdater(WindowsUpdateOptions options, IProcessLauncher? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.InstallDirectory));
        executableRelative = Path.GetRelativePath(installDirectory, Path.GetFullPath(options.ExecutablePath));
        if (executableRelative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(executableRelative))
        {
            throw new ArgumentException("The executable must be inside the install directory.", nameof(options));
        }

        this.options = options;
        this.launcher = launcher ?? new SystemProcessLauncher();
    }

    /// <summary>The staging folder.</summary>
    public string StagingDirectory => Path.Combine(installDirectory, StagingFolderName);

    private string WorkDirectory => Path.GetFullPath(options.WorkDirectory);

    /// <summary>
    /// Unpacks a verified zip into the staging folder and writes the list of its files there. The
    /// zip holds the install directory's contents, at its root or inside one top-level folder, and
    /// must contain the executable. Leftover staging and backup folders are removed first: the
    /// running game shows that the current files work.
    /// </summary>
    public Task StageAsync(string verifiedZipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifiedZipPath);
        return Task.Run(() => Stage(verifiedZipPath, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Writes the plan and the script for the staged update and starts the script for this
    /// process. Quit the game right after; the script waits for that.
    /// </summary>
    public void ScheduleSwap(AppVersion version)
    {
        DateTime? started;
        try
        {
            using Process current = Process.GetCurrentProcess();
            started = current.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            started = null; // The script then skips its check against a reused process id.
        }

        string plan = WriteSwapFiles(version, Environment.ProcessId, started);
        launcher.Start(CreateStartInfo(Path.Combine(WorkDirectory, ScriptFileName), plan));
    }

    /// <summary>Writes the plan and script into the work directory; returns the plan path.</summary>
    public string WriteSwapFiles(AppVersion version, int processId, DateTime? processStartUtc)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!Directory.Exists(StagingDirectory) || !File.Exists(Path.Combine(StagingDirectory, executableRelative))
            || !File.Exists(Path.Combine(StagingDirectory, FileListName)))
        {
            throw new UpdateException(UpdateErrorKind.InstallFailed, "No staged update to install.");
        }

        string work = WorkDirectory;
        string planPath = Path.Combine(work, PlanFileName);
        try
        {
            Directory.CreateDirectory(work);
            File.Delete(Path.Combine(work, ResultFileName));
            File.WriteAllText(Path.Combine(work, ScriptFileName), ReadScript(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.WriteAllBytes(planPath, BuildPlan(version, processId, processStartUtc));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "writing the swap plan");
        }

        return planPath;
    }

    /// <summary>
    /// How the script is started: Windows PowerShell from the system directory, hidden, through the
    /// shell so it inherits none of the game's handles (Godot opens files with inheritable handles;
    /// an inherited handle would keep files of the install directory locked after the game exits).
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string scriptPath, string planPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", scriptPath, "-PlanPath", planPath })
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>The outcome of the last scheduled swap, or null when none was scheduled.</summary>
    public WindowsSwapResult? ReadLastResult()
    {
        string result = Path.Combine(WorkDirectory, ResultFileName);
        try
        {
            if (File.Exists(result))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(result));
                JsonElement root = document.RootElement;
                return new WindowsSwapResult(
                    Json.RequiredString(root, "status"),
                    Json.OptionalString(root, "detail") ?? "",
                    Json.OptionalString(root, "version") ?? "");
            }

            return File.Exists(Path.Combine(WorkDirectory, PlanFileName))
                ? new WindowsSwapResult("notRun", "The swap did not record a result.", "")
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
        {
            return new WindowsSwapResult("failed", "The swap result cannot be read: " + e.GetType().Name, "");
        }
    }

    /// <summary>Removes the plan, script and result after the game has reported the outcome.</summary>
    public void ClearLastResult()
    {
        string work = WorkDirectory;
        if (!Directory.Exists(work))
        {
            return; // nothing was ever scheduled (first start of a new install)
        }

        try
        {
            foreach (string name in new[] { ResultFileName, PlanFileName, ScriptFileName })
            {
                File.Delete(Path.Combine(work, name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "removing the swap files");
        }
    }

    /// <summary>A command-line argument quoted the way Windows programs split them.</summary>
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private void Stage(string zipPath, CancellationToken cancellationToken)
    {
        try
        {
            RemoveDirectory(StagingDirectory);
            RemoveLeftoverBackup();
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            long unpacked = zip.Entries.Sum(e => e.Length);
            if (StorageSpace.FreeBytes(installDirectory) is long free && free < unpacked + ExtractMargin)
            {
                throw new UpdateException(UpdateErrorKind.DiskFull, "Not enough free space to unpack the update.");
            }

            Extract(zip, StagingDirectory, cancellationToken);
            if (!File.Exists(Path.Combine(StagingDirectory, executableRelative)))
            {
                throw new UpdateException(UpdateErrorKind.InstallFailed, $"The update does not contain {executableRelative}.");
            }

            WriteFileList(StagingDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or UpdateException
            or OperationCanceledException or OverflowException or NotSupportedException or ArgumentException)
        {
            TryRemoveStaging();
            if (e is OperationCanceledException)
            {
                throw;
            }

            throw e is InvalidDataException or OverflowException or NotSupportedException or ArgumentException
                ? new UpdateException(UpdateErrorKind.InstallFailed, "The update archive is damaged.", e)
                : UpdateErrors.FromStorage(e, "unpacking the update");
        }
    }

    /// <summary>
    /// Removes the backup folder of an earlier swap. One that still holds files is removed only
    /// after a successful swap; after a failed rollback it may hold the only copy of the old files.
    /// </summary>
    private void RemoveLeftoverBackup()
    {
        string backup = Path.Combine(installDirectory, BackupFolderName);
        if (Directory.Exists(backup) && Directory.EnumerateFileSystemEntries(backup).Any() && ReadLastResult()?.Succeeded != true)
        {
            throw new UpdateException(
                UpdateErrorKind.InstallFailed,
                $"{BackupFolderName} from an unfinished update is still in the install directory; move its files back by hand first.");
        }

        RemoveDirectory(backup);
    }

    private static void Extract(ZipArchive zip, string target, CancellationToken cancellationToken)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + Path.DirectorySeparatorChar;
        string prefix = SingleTopFolder(zip);
        Directory.CreateDirectory(root);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            name = name[prefix.Length..];
            if (name.Length == 0)
            {
                continue;
            }

            string path = Path.GetFullPath(Path.Combine(root, name));
            if (name.Contains(':', StringComparison.Ordinal) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(UpdateErrorKind.InstallFailed, $"The update archive has an entry outside its folder: {entry.FullName}.");
            }

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: false);
        }
    }

    /// <summary>
    /// Writes <see cref="FileListName"/> for the files under <paramref name="root"/>, leaving out
    /// the entries the swap does not touch (preserved, staging, backup).
    /// </summary>
    private void WriteFileList(string root)
    {
        var names = new List<string> { FileListName };
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(root, path).Replace('\\', '/');
            string top = name.Split('/')[0];
            if (!string.Equals(name, FileListName, StringComparison.OrdinalIgnoreCase) && !IsUntouched(top))
            {
                names.Add(name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(root, FileListName), string.Join('\n', names) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private bool IsUntouched(string topLevelName) =>
        options.PreservedEntries.Append(StagingFolderName).Append(BackupFolderName)
            .Contains(topLevelName, StringComparer.OrdinalIgnoreCase);

    /// <summary>"folder/" when every entry is inside one top-level folder, otherwise "".</summary>
    private static string SingleTopFolder(ZipArchive zip)
    {
        string? folder = null;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            int slash = name.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                return "";
            }

            string top = name[..(slash + 1)];
            if (folder is not null && folder != top)
            {
                return "";
            }

            folder = top;
        }

        return folder ?? "";
    }

    private byte[] BuildPlan(AppVersion version, int processId, DateTime? processStartUtc)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, ManifestJson.WriterOptions))
        {
            string work = WorkDirectory;
            writer.WriteStartObject();
            writer.WriteString("version", version.ToString());
            writer.WriteString("installDir", installDirectory);
            writer.WriteString("stagingName", StagingFolderName);
            writer.WriteString("backupName", BackupFolderName);
            writer.WriteString("fileListName", FileListName);
            writer.WriteStartArray("preserve");
            foreach (string name in options.PreservedEntries)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteNumber("pid", processId);
            writer.WriteNumber("processStartUtcTicks", processStartUtc?.Ticks ?? 0);
            writer.WriteNumber("waitTimeoutMs", (long)options.ExitTimeout.TotalMilliseconds);
            writer.WriteNumber("moveAttempts", Math.Max(1, options.MoveAttempts));
            writer.WriteNumber("retryDelayMs", (long)options.MoveRetryDelay.TotalMilliseconds);
            writer.WriteString("relaunchPath", options.Relaunch ? Path.Combine(installDirectory, executableRelative) : "");
            writer.WriteString("relaunchArguments", string.Join(' ', options.RelaunchArguments.Select(QuoteArgument)));
            writer.WriteString("resultPath", Path.Combine(work, ResultFileName));
            writer.WriteString("logPath", Path.Combine(work, LogFileName));
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string ReadScript()
    {
        using Stream stream = typeof(WindowsAppUpdater).Assembly.GetManifestResourceStream(ScriptResource)
            ?? throw new UpdateException(UpdateErrorKind.Unsupported, "The swap script is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void RemoveDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void TryRemoveStaging()
    {
        try
        {
            RemoveDirectory(StagingDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is what gets reported; the next StageAsync removes the rest.
            Trace.TraceWarning("Tribes.Update: could not remove the staging folder: " + e.Message);
        }
    }

    internal sealed class SystemProcessLauncher : IProcessLauncher
    {
        public void Start(ProcessStartInfo startInfo)
        {
            try
            {
                using Process process = Process.Start(startInfo)
                    ?? throw new UpdateException(UpdateErrorKind.InstallFailed, "The swap script did not start.");
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                throw new UpdateException(UpdateErrorKind.InstallFailed, "The swap script could not be started.", e);
            }
        }
    }
}
