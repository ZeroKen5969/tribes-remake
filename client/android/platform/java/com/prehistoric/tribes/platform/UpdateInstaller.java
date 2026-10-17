package com.prehistoric.tribes.platform;

import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;

import java.io.File;

/**
 * Hands a downloaded APK to the system package installer. Called from C# through
 * JavaClassWrapper (client/src/Platform/AndroidAppInstaller.cs); every method takes the
 * Godot activity as its context.
 *
 * The APK must lie in the folder that {@link UpdateFileProvider} shares
 * (res/xml/tribes_update_paths.xml: files/updates/app/, which is Godot's user://updates/app/).
 * Methods that start a screen return an empty string on success and a short error text
 * otherwise, so the caller never depends on a Java exception crossing JNI.
 */
public final class UpdateInstaller {
	private static final String TAG = "TribesUpdate";
	private static final String APK_MIME_TYPE = "application/vnd.android.package-archive";

	/** Authority of the provider declared in AndroidManifest.xml: the application id plus this suffix. */
	private static final String AUTHORITY_SUFFIX = ".updates";

	private UpdateInstaller() {
	}

	/** Whether this app may start a package install now (the player allowed "install unknown apps"). */
	public static boolean canRequestInstall(Context context) {
		if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
			// Before Android 8 the permission is a global setting that the installer asks about itself.
			return true;
		}
		return context.getPackageManager().canRequestPackageInstalls();
	}

	/** Opens the settings screen where the player allows installs from this app. */
	public static String openInstallPermissionSettings(Context context) {
		Intent intent;
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
			intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
					Uri.parse("package:" + context.getPackageName()));
		} else {
			intent = new Intent(Settings.ACTION_SECURITY_SETTINGS);
		}
		intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
		try {
			context.startActivity(intent);
			Log.i(TAG, "opened the install permission screen");
			return "";
		} catch (ActivityNotFoundException | SecurityException e) {
			Log.w(TAG, "cannot open the install permission screen", e);
			return e.toString();
		}
	}

	/** Starts the system installer for the APK at {@code apkPath}; the system asks the player to confirm. */
	public static String launchInstaller(Context context, String apkPath) {
		File apk = new File(apkPath);
		if (!apk.isFile()) {
			return "not a file: " + apkPath;
		}

		Uri uri;
		try {
			uri = UpdateFileProvider.getUriForFile(context, context.getPackageName() + AUTHORITY_SUFFIX, apk);
		} catch (IllegalArgumentException e) {
			// The file is outside the shared folder.
			Log.w(TAG, "cannot share " + apkPath, e);
			return e.toString();
		}

		Intent intent = new Intent(Intent.ACTION_VIEW);
		intent.setDataAndType(uri, APK_MIME_TYPE);
		intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
		try {
			context.startActivity(intent);
			Log.i(TAG, "started the package installer for " + uri);
			return "";
		} catch (ActivityNotFoundException | SecurityException e) {
			Log.w(TAG, "cannot start the package installer", e);
			return e.toString();
		}
	}
}
