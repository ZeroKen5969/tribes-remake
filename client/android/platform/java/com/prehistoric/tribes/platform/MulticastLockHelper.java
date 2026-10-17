package com.prehistoric.tribes.platform;

import android.content.Context;
import android.net.wifi.WifiManager;
import android.util.Log;

/**
 * Holds the Wi-Fi multicast lock. Android drops multicast and broadcast packets addressed to
 * other hosts to save power; LAN game discovery needs them while it listens. Called from C#
 * through JavaClassWrapper (client/src/Platform/AndroidMulticast.cs). Needs the
 * CHANGE_WIFI_MULTICAST_STATE permission.
 */
public final class MulticastLockHelper {
	private static final String TAG = "TribesNet";
	private static final String LOCK_TAG = "tribes-lan-discovery";

	private static WifiManager.MulticastLock lock;

	private MulticastLockHelper() {
	}

	/** Takes the lock (once; repeated calls keep holding it). Returns whether the lock is held. */
	public static synchronized boolean acquire(Context context) {
		try {
			if (lock == null) {
				WifiManager wifi = (WifiManager) context.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
				if (wifi == null) {
					Log.w(TAG, "multicast lock: no Wi-Fi service");
					return false;
				}
				lock = wifi.createMulticastLock(LOCK_TAG);
				lock.setReferenceCounted(false);
			}
			if (!lock.isHeld()) {
				lock.acquire();
			}
			boolean held = lock.isHeld();
			Log.i(TAG, "multicast lock acquire: held=" + held);
			return held;
		} catch (RuntimeException e) {
			// SecurityException when the permission is missing.
			Log.w(TAG, "multicast lock acquire failed", e);
			return false;
		}
	}

	/** Gives the lock back. Returns whether the lock is now released. */
	public static synchronized boolean release() {
		if (lock == null || !lock.isHeld()) {
			return true;
		}
		try {
			lock.release();
			boolean held = lock.isHeld();
			Log.i(TAG, "multicast lock release: held=" + held);
			return !held;
		} catch (RuntimeException e) {
			Log.w(TAG, "multicast lock release failed", e);
			return false;
		}
	}

	/** Whether the lock is held now. */
	public static synchronized boolean isHeld() {
		return lock != null && lock.isHeld();
	}
}
