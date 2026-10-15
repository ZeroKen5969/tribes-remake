using Godot;

namespace Tribes.Client.Map;

/// <summary>
/// The camera over the map: pans with drags, zooms around the touched point with pinches or the
/// mouse wheel, and stays over the map. Zoom 1 shows the map at its original pixel size.
/// </summary>
public partial class MapCamera : Camera2D
{
    /// <summary>Most zoomed out.</summary>
    [Export] public float MinZoom { get; set; } = 0.5f;

    /// <summary>Most zoomed in.</summary>
    [Export] public float MaxZoom { get; set; } = 6f;

    /// <summary>The area the camera may show, in world pixels.</summary>
    public Rect2 Bounds { get; set; } = new(0, 0, 1, 1);

    /// <summary>Moves the view by <paramref name="screenDelta"/> screen pixels (content follows the finger).</summary>
    public void PanBy(Vector2 screenDelta)
    {
        Position -= screenDelta / Zoom;
        KeepInBounds();
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping the world point under <paramref name="screenFocus"/> in place.</summary>
    public void ZoomBy(float factor, Vector2 screenFocus)
    {
        Vector2 before = ScreenToWorld(screenFocus);
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        ForceUpdateScroll();
        Vector2 after = ScreenToWorld(screenFocus);
        Position += before - after;
        KeepInBounds();
    }

    /// <summary>Centres the view on <paramref name="worldPoint"/>.</summary>
    public void CentreOn(Vector2 worldPoint)
    {
        Position = worldPoint;
        KeepInBounds();
    }

    /// <summary>The world point under screen position <paramref name="screen"/>.</summary>
    public Vector2 ScreenToWorld(Vector2 screen) => GetCanvasTransform().AffineInverse() * screen;

    /// <summary>Keeps the view over the map; a map smaller than the view stays centred.</summary>
    public void KeepInBounds()
    {
        Vector2 half = GetViewportRect().Size / Zoom / 2;
        Position = new Vector2(Clamp(Position.X, Bounds.Position.X, Bounds.End.X, half.X),
            Clamp(Position.Y, Bounds.Position.Y, Bounds.End.Y, half.Y));
        ForceUpdateScroll();
    }

    private static float Clamp(float value, float min, float max, float half) =>
        max - min <= half * 2 ? (min + max) / 2 : Mathf.Clamp(value, min + half, max - half);
}
