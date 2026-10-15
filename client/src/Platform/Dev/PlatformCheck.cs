using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Tribes.Update;

namespace Tribes.Client.Platform.Dev;

/// <summary>
/// Development check of the platform layer without any UI, registered as the autoload
/// "PlatformCheck". It does nothing unless a debug build gets the user argument
/// <c>--platform-check=CHECKS</c> (checks joined by <c>+</c> or <c>,</c>); then it prints
/// <c>PLATFORM_CHECK</c> lines while the game starts normally.
/// <list type="bullet">
/// <item><c>multicast</c>: takes and gives back the Wi-Fi multicast lock.</item>
/// <item><c>install</c>: copies this app's own APK into user://updates/app/ and hands it to the
/// installer the way an app update does (permission screen first, then the system installer).</item>
/// <item><c>update</c>: asks the update server for a newer version (needs the network).</item>
/// </list>
/// Editor: <c>Godot --path client -- --platform-check=multicast+update</c>.
/// Android emulator (as root, because Godot takes launch arguments only from its non-exported
/// activity): <c>adb shell su 0 am start -n com.prehistoric.tribes.remake/com.godot.game.GodotApp
/// --esa command_line_params --,--platform-check=multicast+install</c>.
/// </summary>
public partial class PlatformCheck : Node
{
    private const string Argument = "--platform-check=";
    private const string CopiedPackageName = "PrehistoricTribes-platform-check.apk";

    /// <inheritdoc/>
    public override void _Ready()
    {
        string? requested = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(Argument, StringComparison.Ordinal));
        if (requested is null || !OS.IsDebugBuild())
        {
            QueueFree();
            return;
        }

        string[] checks = requested[Argument.Length..].Split(['+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        BuildInfo build = BuildInfo.Current;
        Log($"start os={OS.GetName()} version={build.Version} code={build.VersionCode} contentRev={build.ContentRevision} "
            + $"userData={OS.GetUserDataDir()} sharedFolder={ProjectSettings.GlobalizePath(AndroidAppInstaller.SharedFolder)} checks={string.Join('+', checks)}");

        if (checks.Contains("multicast"))
        {
            CheckMulticast();
        }

        if (checks.Contains("install"))
        {
            CheckInstaller();
        }

        if (checks.Contains("update"))
        {
            _ = CheckUpdateServerAsync();
        }
        else
        {
            Log("done");
        }
    }

    private static void CheckMulticast()
    {
        bool acquired = AndroidMulticast.Acquire();
        bool held = AndroidMulticast.IsHeld;
        bool released = AndroidMulticast.Release();
        bool heldAfter = AndroidMulticast.IsHeld;
        Log($"multicast acquire={acquired} held={held} release={released} heldAfter={heldAfter}");
    }

    /// <summary>The same steps as UpdateManager.ApplyAsync for an Android app update.</summary>
    private static void CheckInstaller()
    {
        IAppInstaller installer = AndroidAppInstaller.ForCurrentPlatform();
        try
        {
            string package = CopyOwnPackage();
            Log($"install package={package} size={new FileInfo(package).Length}");
            if (!installer.CanRequestInstall)
            {
                installer.RequestInstallPermission();
                Log($"install outcome={UpdateApplyOutcome.PermissionRequested}");
                return;
            }

            installer.LaunchInstaller(package);
            Log($"install outcome={UpdateApplyOutcome.InstallerStarted}");
        }
        catch (UpdateException e)
        {
            Log($"install error kind={e.Kind} message={e.Message}");
        }
        catch (IOException e)
        {
            Log($"install error copying the package: {e.Message}");
        }
    }

    /// <summary>Copies the installed APK of this app into the folder the installer shares.</summary>
    private static string CopyOwnPackage()
    {
        if (!AndroidJava.IsAndroid)
        {
            throw new UpdateException(UpdateErrorKind.Unsupported, "Only Android installs packages.");
        }

        string source = AndroidJava.Activity().Call("getPackageCodePath").AsString();
        string folder = ProjectSettings.GlobalizePath(AndroidAppInstaller.SharedFolder);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, CopiedPackageName);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    private static async Task CheckUpdateServerAsync()
    {
        try
        {
            using UpdateManager updates = UpdateManager.CreateDefault(UpdatePlatformOptions.Create());
            UpdateCheckResult result = await updates.CheckAsync();
            Log($"update kind={result.Kind} tag={result.Tag} needsNewerApp={result.NewerContentNeedsNewerApp}");
        }
        catch (UpdateException e)
        {
            Log($"update error kind={e.Kind} message={e.Message}");
        }

        Log("done");
    }

    private static void Log(string message) => GD.Print($"PLATFORM_CHECK {message}");
}
