using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>Screen space covered by the interface on each side, in screen (logical) pixels.</summary>
/// <param name="Left">Covered on the left.</param>
/// <param name="Top">Covered at the top.</param>
/// <param name="Right">Covered on the right.</param>
/// <param name="Bottom">Covered at the bottom.</param>
public readonly record struct ScreenInsets(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Nothing covered.</summary>
    public static ScreenInsets None => default;
}

/// <summary>
/// Where the map camera may look: every edge of the map can be brought out from under the
/// interface (so nothing on the map is ever hidden for good), and a map smaller than the free
/// part of the screen stays centred in it. Zoom steps keep whole screen pixels per map pixel.
/// </summary>
public static class CameraLimits
{
    /// <summary>
    /// The camera centre nearest to <paramref name="centre"/> (map pixels) allowed at
    /// <paramref name="zoom"/> on a screen of <paramref name="viewSize"/> with the interface
    /// covering <paramref name="insets"/>, for a map spanning <paramref name="mapMin"/>..<paramref name="mapMax"/>.
    /// </summary>
    public static Vector2 Clamp(Vector2 centre, float zoom, Vector2 viewSize, Vector2 mapMin, Vector2 mapMax, ScreenInsets insets)
    {
        if (zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "zoom must be positive");
        }
        float x = Axis(centre.X, zoom, viewSize.X, mapMin.X, mapMax.X, insets.Left, insets.Right);
        float y = Axis(centre.Y, zoom, viewSize.Y, mapMin.Y, mapMax.Y, insets.Top, insets.Bottom);
        return new Vector2(x, y);
    }

    /// <summary>
    /// The camera centre that shows map point <paramref name="point"/> in the middle of the free
    /// part of the screen (between the insets).
    /// </summary>
    public static Vector2 CentreFor(Vector2 point, float zoom, ScreenInsets insets) =>
        new(point.X - ((insets.Left - insets.Right) / (2 * zoom)), point.Y - ((insets.Top - insets.Bottom) / (2 * zoom)));

    /// <summary>
    /// The zoom levels between <paramref name="minZoom"/> and <paramref name="maxZoom"/> at which
    /// one map pixel is a whole number of screen pixels for pixel scale <paramref name="pixelScale"/>.
    /// </summary>
    public static IReadOnlyList<float> PixelPerfectZooms(int pixelScale, float minZoom, float maxZoom)
    {
        int scale = Math.Max(1, pixelScale);
        var zooms = new List<float>();
        for (int k = 1; k <= maxZoom * scale + 0.001f; k++)
        {
            float zoom = k / (float)scale;
            if (zoom >= minZoom - 0.001f)
            {
                zooms.Add(zoom);
            }
        }
        if (zooms.Count == 0)
        {
            zooms.Add(Math.Clamp(1f, minZoom, maxZoom));
        }
        return zooms;
    }

    /// <summary>The next level of <paramref name="zooms"/> up (<paramref name="steps"/> &gt; 0) or down from <paramref name="zoom"/>.</summary>
    public static float Step(IReadOnlyList<float> zooms, float zoom, int steps)
    {
        ArgumentNullException.ThrowIfNull(zooms);
        int nearest = 0;
        for (int i = 1; i < zooms.Count; i++)
        {
            if (Math.Abs(zooms[i] - zoom) < Math.Abs(zooms[nearest] - zoom))
            {
                nearest = i;
            }
        }
        // From between two levels, the first step goes to the level in that direction.
        if (steps > 0 && zooms[nearest] > zoom + 0.001f)
        {
            steps--;
        }
        else if (steps < 0 && zooms[nearest] < zoom - 0.001f)
        {
            steps++;
        }
        return zooms[Math.Clamp(nearest + steps, 0, zooms.Count - 1)];
    }

    private static float Axis(float centre, float zoom, float view, float min, float max, float coverBefore, float coverAfter)
    {
        float half = view / zoom / 2;
        float low = min - (coverBefore / zoom) + half;
        float high = max + (coverAfter / zoom) - half;
        if (low > high)
        {
            // The map is smaller than the free part: centre it there.
            return ((min + max) / 2) - ((coverBefore - coverAfter) / (2 * zoom));
        }
        return Math.Clamp(centre, low, high);
    }
}
