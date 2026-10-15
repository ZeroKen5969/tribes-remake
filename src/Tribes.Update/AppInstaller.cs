namespace Tribes.Update;

/// <summary>
/// The platform's package installer, implemented by the client. On Android: the APK lies in a
/// directory shared through a <c>FileProvider</c>; <see cref="LaunchInstaller"/> starts an
/// <c>ACTION_VIEW</c> intent with the <c>content://</c> URI, MIME type
/// <c>application/vnd.android.package-archive</c> and <c>FLAG_GRANT_READ_URI_PERMISSION</c>; the
/// manifest declares <c>REQUEST_INSTALL_PACKAGES</c>. Android installs the APK only when it is
/// signed with the same key as the installed app.
/// </summary>
public interface IAppInstaller
{
    /// <summary>Whether the app may start an install now (Android: <c>PackageManager.canRequestPackageInstalls()</c>).</summary>
    bool CanRequestInstall { get; }

    /// <summary>Opens the screen where the player allows installs from this app (Android: <c>ACTION_MANAGE_UNKNOWN_APP_SOURCES</c>).</summary>
    void RequestInstallPermission();

    /// <summary>Hands the verified package to the system installer; the system asks the player to confirm.</summary>
    void LaunchInstaller(string packagePath);
}

/// <summary>The installer of builds that cannot install packages: every call reports <see cref="UpdateErrorKind.Unsupported"/>.</summary>
public sealed class UnsupportedAppInstaller : IAppInstaller
{
    /// <inheritdoc/>
    public bool CanRequestInstall => false;

    /// <inheritdoc/>
    public void RequestInstallPermission() =>
        throw new UpdateException(UpdateErrorKind.Unsupported, "This build cannot install packages.");

    /// <inheritdoc/>
    public void LaunchInstaller(string packagePath) =>
        throw new UpdateException(UpdateErrorKind.Unsupported, "This build cannot install packages.");
}
