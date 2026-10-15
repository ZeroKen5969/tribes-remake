using System;
using System.IO;
using Tribes.Update;

namespace Tribes.Client.Platform;

/// <summary>
/// Android's package installer for app updates (<see cref="IAppInstaller"/> of Tribes.Update).
/// Calls <c>com.prehistoric.tribes.platform.UpdateInstaller</c>, which shares the APK with the
/// system installer through a FileProvider limited to <see cref="SharedFolder"/>; that is where
/// Tribes.Update keeps the downloaded APK. The new APK must be signed with the same key as the
/// installed app and have a larger version code, otherwise Android refuses it.
/// </summary>
public sealed class AndroidAppInstaller : IAppInstaller
{
    private const string JavaClassName = "com.prehistoric.tribes.platform.UpdateInstaller";

    /// <summary>The only folder whose files the installer can hand to Android.</summary>
    public const string SharedFolder = "user://updates/app/";

    /// <summary>The installer for this platform: this one on Android, otherwise one that reports "not supported".</summary>
    public static IAppInstaller ForCurrentPlatform() =>
        AndroidJava.IsAndroid ? new AndroidAppInstaller() : new UnsupportedAppInstaller();

    /// <inheritdoc/>
    public bool CanRequestInstall => Call("canRequestInstall", result => AndroidJava.ToBool(result, "canRequestInstall"));

    /// <inheritdoc/>
    public void RequestInstallPermission() =>
        ThrowOnError(Call("openInstallPermissionSettings", result => AndroidJava.ToText(result, "openInstallPermissionSettings")),
            "open the install permission screen");

    /// <inheritdoc/>
    public void LaunchInstaller(string packagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(packagePath);
        ThrowOnError(Call("launchInstaller", result => AndroidJava.ToText(result, "launchInstaller"), Path.GetFullPath(packagePath)),
            "start the package installer");
    }

    /// <summary>Calls the Java method and converts its result; any failure becomes an <see cref="UpdateException"/>.</summary>
    private static T Call<T>(string method, Func<Godot.Variant, T> convert, params Godot.Variant[] args)
    {
        try
        {
            return convert(AndroidJava.CallWithActivity(JavaClassName, method, args));
        }
        catch (InvalidOperationException e)
        {
            throw new UpdateException(UpdateErrorKind.Unsupported, $"The Android installer is not available: {e.Message}", e);
        }
    }

    /// <summary>The Java side returns an empty string on success and an error text otherwise.</summary>
    private static void ThrowOnError(string error, string action)
    {
        if (error.Length > 0)
        {
            throw new UpdateException(UpdateErrorKind.InstallFailed, $"Cannot {action}: {error}");
        }
    }
}
