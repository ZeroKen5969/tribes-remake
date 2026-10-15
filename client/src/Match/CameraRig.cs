using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Map;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.World;

namespace Tribes.Client.MatchPlay;

/// <summary>
/// Moves the match camera: drags and keys pan, pinches and the wheel zoom (the wheel and keys in
/// steps that keep whole screen pixels per map pixel), the mouse at the window edge scrolls, and
/// the view always stays over the map with every edge reachable from under the interface.
/// </summary>
public sealed class CameraRig
{
    /// <summary>Most zoomed out and in.</summary>
    public const float MinZoom = 0.5f, MaxZoom = 6f;

    private const float KeyPanPixelsPerSecond = 420f;
    private const float EdgeBand = 4f;
    private const float EdgePanPixelsPerSecond = 360f;
    private const float GlideSeconds = 0.25f;

    private readonly MapCamera camera;
    private readonly WorldView view;
    private IReadOnlyList<float> zooms = new[] { 1f };
    private Vector2? glideTo;
    private float glideLeft;

    /// <summary>A rig for <paramref name="camera"/> over <paramref name="view"/>.</summary>
    public CameraRig(MapCamera camera, WorldView view)
    {
        this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        camera.MinZoom = MinZoom;
        camera.MaxZoom = MaxZoom;
    }

    /// <summary>Screen space the interface covers.</summary>
    public ScreenInsets Insets { get; set; }

    /// <summary>The zoom now.</summary>
    public float Zoom => camera.Zoom.X;

    /// <summary>Whether the mouse at the window edge scrolls the map.</summary>
    public bool EdgeScroll { get; set; }

    /// <summary>The camera.</summary>
    public MapCamera Camera => camera;

    /// <summary>The window's pixel scale changed: the zoom steps follow it.</summary>
    public void SetPixelScale(int scale) => zooms = CameraLimits.PixelPerfectZooms(scale, MinZoom, MaxZoom);

    /// <summary>Moves the view by <paramref name="screenDelta"/> screen pixels (the map follows the finger).</summary>
    public void PanBy(Vector2 screenDelta)
    {
        glideTo = null;
        camera.Position -= screenDelta / Zoom;
        Clamp();
    }

    /// <summary>Zooms by <paramref name="factor"/> around screen point <paramref name="focus"/> (pinch).</summary>
    public void ZoomBy(float factor, Vector2 focus) => ZoomTo(Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom), focus);

    /// <summary>Zooms <paramref name="steps"/> pixel-perfect steps around <paramref name="focus"/> (wheel, keys).</summary>
    public void ZoomSteps(int steps, Vector2 focus) => ZoomTo(CameraLimits.Step(zooms, Zoom, steps), focus);

    /// <summary>Sets the zoom keeping the map point under <paramref name="focus"/> in place.</summary>
    public void ZoomTo(float zoom, Vector2 focus)
    {
        Vector2 before = camera.ScreenToWorld(focus);
        camera.Zoom = new Vector2(zoom, zoom);
        camera.ForceUpdateScroll();
        Vector2 after = camera.ScreenToWorld(focus);
        camera.Position += before - after;
        Clamp();
    }

    /// <summary>Brings map point <paramref name="point"/> to the middle of the free part of the screen.</summary>
    public void CentreOn(Vector2 point, bool glide = false)
    {
        Vector2 target = ToGodot(CameraLimits.CentreFor(ToNumerics(point), Zoom, Insets));
        if (glide)
        {
            glideTo = target;
            glideLeft = GlideSeconds;
            return;
        }
        glideTo = null;
        camera.Position = target;
        Clamp();
    }

    /// <summary>Brings map cell (<paramref name="x"/>, <paramref name="y"/>) to the middle of the free part of the screen.</summary>
    public void CentreOnCell(int x, int y, bool glide = false) =>
        CentreOn(new Vector2((x + 0.5f) * WorldView.CellWidth, (y + 0.5f) * WorldView.CellHeight), glide);

    /// <summary>Moves on every frame: keys held, the mouse at the window edge, a glide in progress.</summary>
    public void Process(double delta, Vector2 keyDirection, Vector2? mouse, Vector2 screenSize)
    {
        float seconds = (float)delta;
        if (glideTo is { } target)
        {
            glideLeft -= seconds;
            float t = glideLeft <= 0 ? 1 : Mathf.Clamp(seconds / (glideLeft + seconds), 0, 1);
            camera.Position = camera.Position.Lerp(target, t);
            if (glideLeft <= 0)
            {
                glideTo = null;
            }
            Clamp();
        }
        Vector2 move = keyDirection * KeyPanPixelsPerSecond * seconds;
        if (EdgeScroll && mouse is { } m && m.X >= 0 && m.Y >= 0 && m.X < screenSize.X && m.Y < screenSize.Y)
        {
            Vector2 edge = Vector2.Zero;
            edge.X = m.X < EdgeBand ? -1 : m.X >= screenSize.X - EdgeBand ? 1 : 0;
            edge.Y = m.Y < EdgeBand ? -1 : m.Y >= screenSize.Y - EdgeBand ? 1 : 0;
            move += edge * EdgePanPixelsPerSecond * seconds;
        }
        if (move != Vector2.Zero)
        {
            glideTo = null;
            camera.Position += move / Zoom;
            Clamp();
        }
    }

    /// <summary>Keeps the view over the map (after resizes, zooms and moves).</summary>
    public void Clamp()
    {
        Rect2 map = view.MapBounds;
        Vector2 size = camera.GetViewportRect().Size;
        System.Numerics.Vector2 clamped = CameraLimits.Clamp(ToNumerics(camera.Position), Zoom, ToNumerics(size),
            ToNumerics(map.Position), ToNumerics(map.End), Insets);
        camera.Position = ToGodot(clamped);
        camera.ForceUpdateScroll();
    }

    /// <summary>The map point under screen point <paramref name="screen"/>.</summary>
    public Vector2 ScreenToMap(Vector2 screen) => camera.ScreenToWorld(screen);

    /// <summary>The screen point of map point <paramref name="map"/>.</summary>
    public Vector2 MapToScreen(Vector2 map) => camera.GetCanvasTransform() * map;

    /// <summary>The map rectangle seen on screen.</summary>
    public Rect2 VisibleMap()
    {
        Vector2 size = camera.GetViewportRect().Size;
        Vector2 a = ScreenToMap(Vector2.Zero), b = ScreenToMap(size);
        return new Rect2(a, b - a);
    }

    private static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);

    private static Vector2 ToGodot(System.Numerics.Vector2 v) => new(v.X, v.Y);
}
