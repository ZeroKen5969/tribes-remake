package com.prehistoric.tribes.platform;

import android.app.Activity;
import android.graphics.Point;
import android.os.Build;
import android.util.Log;
import android.view.RoundedCorner;
import android.view.View;
import android.view.WindowInsets;

/**
 * Reads how the screen's corners are rounded, so the game keeps its buttons inside the rounding
 * (Godot's safe area only covers camera cut-outs). Called from C# through JavaClassWrapper
 * (client/src/Platform/ScreenShape.cs). Android tells the rounding from version 12 (API 31) on;
 * older versions and square screens report no rounding.
 */
public final class ScreenCorners {
	private static final String TAG = "TribesScreen";
	private static final int[] POSITIONS = {
		RoundedCorner.POSITION_TOP_LEFT,
		RoundedCorner.POSITION_TOP_RIGHT,
		RoundedCorner.POSITION_BOTTOM_RIGHT,
		RoundedCorner.POSITION_BOTTOM_LEFT,
	};

	private ScreenCorners() {
	}

	/**
	 * The window's width and height, then for the top-left, top-right, bottom-right and
	 * bottom-left corner the rounding's radius and its centre's x and y, all in window pixels
	 * (14 numbers). A corner without rounding has radius 0; without any information all are 0.
	 */
	public static int[] read(Activity activity) {
		int[] result = new int[2 + (3 * POSITIONS.length)];
		try {
			View view = activity.getWindow().getDecorView();
			result[0] = view.getWidth();
			result[1] = view.getHeight();
			if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
				return result;
			}
			WindowInsets insets = view.getRootWindowInsets();
			if (insets == null) {
				return result;
			}
			for (int i = 0; i < POSITIONS.length; i++) {
				RoundedCorner corner = insets.getRoundedCorner(POSITIONS[i]);
				if (corner == null) {
					continue;
				}
				Point centre = corner.getCenter();
				result[2 + (3 * i)] = corner.getRadius();
				result[3 + (3 * i)] = centre.x;
				result[4 + (3 * i)] = centre.y;
			}
		} catch (RuntimeException e) {
			Log.w(TAG, "rounded corners unavailable", e);
		}
		return result;
	}
}
