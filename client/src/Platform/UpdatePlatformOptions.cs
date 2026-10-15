using System;
using System.IO;
using Godot;
using Tribes.Update;

namespace Tribes.Client.Platform;

/// <summary>
/// The <see cref="UpdateManagerOptions"/> for this build and platform, set up as docs/UPDATES.md
/// describes: version from <see cref="BuildInfo"/>, update data in <c>user://updates</c>, the
/// Android installer on Android and the install folder swap on Windows.
/// </summary>
public static class UpdatePlatformOptions
{
    /// <summary>
    /// Creates the options. Pre-release versions are offered when <paramref name="includePrereleases"/>
    /// says so, by default only to a build that is itself a pre-release (a tester's build keeps
    /// receiving the next test build; a release build only ever gets releases).
    /// </summary>
    public static UpdateManagerOptions Create(bool? includePrereleases = null)
    {
        BuildInfo build = BuildInfo.Current;
        AppVersion version = AppVersion.Parse(build.Version);
        return new UpdateManagerOptions
        {
            AppVersion = version,
            BuiltInContentRevision = build.ContentRevision,
            Platform = AndroidJava.IsAndroid ? UpdatePlatform.Android : UpdatePlatform.Windows,
            DataDirectory = ProjectSettings.GlobalizePath("user://updates"),
            IncludePrereleases = includePrereleases ?? version.IsPrerelease,
            AppInstaller = AndroidAppInstaller.ForCurrentPlatform(),
            Windows = CreateWindowsOptions(),
        };
    }

    /// <summary>
    /// Only an exported Windows game replaces its own folder; a game started from the editor
    /// would otherwise swap the editor's files.
    /// </summary>
    private static WindowsUpdateOptions? CreateWindowsOptions()
    {
        if (OS.GetName() != "Windows" || !OS.HasFeature("template"))
        {
            return null;
        }

        string executable = OS.GetExecutablePath();
        string workDirectory = ProjectSettings.GlobalizePath("user://updates/windows");
        return new WindowsUpdateOptions
        {
            InstallDirectory = Path.GetDirectoryName(executable)!,
            ExecutablePath = executable,
            WorkDirectory = workDirectory,
        };
    }
}
