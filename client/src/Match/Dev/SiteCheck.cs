using System;
using System.Threading.Tasks;
using Godot;
using Tribes.Client.World;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Dev;

/// <summary>
/// Development check "--site-check" (debug builds): with the game held, a construction site or
/// building is put on cleared, fully seen ground and the real screen is captured with the camera
/// at many zooms and with the building across each edge of the screen; every pixel of the
/// building that is on screen must look as it does with the building in the middle of the screen
/// (pixels that change between two whole views, its animations, are left out). Prints
/// SITE_CHECK lines and SITE_CHECK_DONE cases=N failures=M, then quits.
/// </summary>
public partial class SiteCheck : Node
{
    private const int Slot = 30;
    private static readonly int[] Kinds = { 12, 13, 14, 15, 16, 17, 18, 19, 0, 2, 3, 8, 11 };
    private static readonly int[] ScreenPixelsPerMapPixel = { 1, 2, 3, 4, 6 };
    private static readonly float[] Cuts = { 0.15f, 0.4f, 0.65f, 0.9f };

    private MatchScreen screen = null!;
    private int cases, failures;

    /// <summary>Runs the check on <paramref name="match"/> once it plays.</summary>
    public static void Start(MatchScreen match)
    {
        ArgumentNullException.ThrowIfNull(match);
        match.AddChild(new SiteCheck { Name = "SiteCheck", screen = match });
    }

    /// <inheritdoc/>
    public override async void _Ready()
    {
        while (screen.Runner.Simulation.CurrentScene is not null || !screen.IsOpen)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        screen.Runner.SetPaused(true);
        screen.Hud.Visible = false;
        screen.View.SmoothMotion = false;
        // "--site-check-original": the original's scan, to see the check catch its cut-off buildings.
        screen.View.WholeBuildings = !Tribes.Client.Ui.Dev.DevOptions.Has("site-check-original");
        GameWorld world = screen.World;
        GridPoint origin = ClearArea(world);
        foreach (int kind in Kinds)
        {
            BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
            Place(world, kind, origin, definition);
            foreach (int pixels in ScreenPixelsPerMapPixel)
            {
                await CheckZoom(kind, origin, definition, pixels);
            }
            Remove(world, origin, definition);
        }
        GD.Print($"SITE_CHECK_DONE cases={cases} failures={failures}");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private async Task CheckZoom(int kind, GridPoint origin, BuildingKind definition, int pixels)
    {
        float scale = GetTree().Root.ContentScaleFactor;
        float zoom = pixels / scale;
        screen.CameraRig.Camera.Zoom = new Vector2(zoom, zoom);
        var picture = new Rect2(origin.X * WorldView.CellWidth - 11, (origin.Y * WorldView.CellHeight) - 48,
            (definition.Width * WorldView.CellWidth) + 22, (definition.Height * WorldView.CellHeight) + 64);
        // Camera places on whole logical pixels: the screen snaps 2D drawing to them.
        Vector2 Snap(Vector2 p) => new(Mathf.Round(p.X * zoom) / zoom, Mathf.Round(p.Y * zoom) / zoom);
        (Image a, Vector2 middle) = await Capture(Snap(picture.GetCenter()));
        (Image b, Vector2 shifted) = await Capture(middle + new Vector2(2 * WorldView.CellWidth, 3 * WorldView.CellHeight));
        Vector2 view = GetViewport().GetVisibleRect().Size / zoom;
        foreach (float cut in Cuts)
        {
            // The camera centre that puts the share "cut" of the picture past each edge of the screen.
            var places = new[]
            {
                new Vector2(picture.Position.X + (picture.Size.X * cut) + (view.X / 2), middle.Y),
                new Vector2(picture.End.X - (picture.Size.X * cut) - (view.X / 2), middle.Y),
                new Vector2(middle.X, picture.Position.Y + (picture.Size.Y * cut) + (view.Y / 2)),
                new Vector2(middle.X, picture.End.Y - (picture.Size.Y * cut) - (view.Y / 2)),
            };
            foreach (Vector2 place in places)
            {
                (Image shot, Vector2 camera) = await Capture(Snap(place));
                cases++;
                int wrong = Compare(a, b, shot, middle, shifted, camera, picture, pixels);
                if (wrong > 0)
                {
                    failures++;
                    GD.Print($"SITE_CHECK kind={kind} zoom={zoom:0.###} camera={camera} differs in {wrong} pixels");
                }
            }
        }
    }

    /// <summary>Pixels of the building, on screen in <paramref name="shot"/>, that differ from the middle view (left out: those that changed between the two whole views).</summary>
    private static int Compare(Image a, Image b, Image shot, Vector2 middle, Vector2 shifted, Vector2 camera, Rect2 picture, int pixels)
    {
        Vector2 screenSize = new(shot.GetWidth(), shot.GetHeight());
        float perMap = pixels;
        // Screen pixel of map point p for a camera centre c: (p - c) * perMap + screen / 2.
        Vector2 ToScreen(Vector2 p, Vector2 c) => ((p - c) * perMap) + (screenSize / 2);
        int wrong = 0;
        Vector2 topLeft = ToScreen(picture.Position, camera), bottomRight = ToScreen(picture.End, camera);
        int x0 = Math.Max(0, Mathf.RoundToInt(topLeft.X)), y0 = Math.Max(0, Mathf.RoundToInt(topLeft.Y));
        int x1 = Math.Min(shot.GetWidth(), Mathf.RoundToInt(bottomRight.X)), y1 = Math.Min(shot.GetHeight(), Mathf.RoundToInt(bottomRight.Y));
        Vector2 toA = (ToScreen(picture.Position, middle) - topLeft).Round();
        Vector2 toB = (ToScreen(picture.Position, shifted) - topLeft).Round();
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int ax = x + (int)toA.X, ay = y + (int)toA.Y, bx = x + (int)toB.X, by = y + (int)toB.Y;
                if (!Inside(a, ax, ay) || !Inside(b, bx, by))
                {
                    continue;
                }
                Color expected = a.GetPixel(ax, ay);
                if (expected != b.GetPixel(bx, by))
                {
                    continue;
                }
                if (shot.GetPixel(x, y) != expected)
                {
                    wrong++;
                }
            }
        }
        return wrong;
    }

    private static bool Inside(Image image, int x, int y) => x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight();

    /// <summary>
    /// The screen with the camera at <paramref name="camera"/>, and where the camera really was
    /// (the screen keeps it over the map: near the map's edge it cannot go everywhere asked).
    /// </summary>
    private async Task<(Image Shot, Vector2 Centre)> Capture(Vector2 camera)
    {
        screen.CameraRig.Camera.Position = camera;
        screen.CameraRig.Camera.ForceUpdateScroll();
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        return (GetViewport().GetTexture().GetImage(), screen.CameraRig.Camera.GetScreenCenterPosition());
    }

    /// <summary>Clears a patch of plain, seen ground away from every unit and animal and returns where the building goes.</summary>
    private GridPoint ClearArea(GameWorld world)
    {
        FogOfWar fog = world.Fog(screen.ViewTribe);
        for (int y = 0; y < fog.Size.Height; y++)
        {
            for (int x = 0; x < fog.Size.Width; x++)
            {
                fog.SetExplored(new GridPoint(x, y), true);
                fog.SetVisible(new GridPoint(x, y), true);
            }
        }
        GridSize size = world.Map.PlayableSize;
        for (int oy = 10; oy < size.Height - 16; oy++)
        {
            for (int ox = 10; ox < size.Width - 16; ox++)
            {
                if (IsQuiet(world, ox, oy))
                {
                    for (int y = oy - 4; y < oy + 10; y++)
                    {
                        for (int x = ox - 4; x < ox + 10; x++)
                        {
                            world.Map.SetCell(new GridPoint(x, y), MapCell.Empty);
                            world.Map.SetTile(new GridPoint(x, y), 0);
                        }
                    }
                    screen.View.Refresh(world);
                    return new GridPoint(ox, oy);
                }
            }
        }
        throw new InvalidOperationException("no quiet place on the map");
    }

    private static bool IsQuiet(GameWorld world, int ox, int oy)
    {
        for (int y = oy - 8; y < oy + 14; y++)
        {
            for (int x = ox - 10; x < ox + 16; x++)
            {
                var cell = new GridPoint(x, y);
                if (!world.Map.InPlay(cell))
                {
                    return false;
                }
                CellContent content = world.Map.CellAt(cell).Content;
                if (content is CellContent.Unit or CellContent.Animal or CellContent.Building or CellContent.Reserved)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void Place(GameWorld world, int kind, GridPoint origin, BuildingKind definition)
    {
        Building b = world.Building(Slot);
        b.X = origin.X;
        b.Y = origin.Y;
        b.Kind = kind;
        b.Level = kind < 12 ? 2 : 3;
        b.MaxHitPoints = definition.MaxHitPoints[Math.Clamp(b.Level - 1, 0, definition.MaxHitPoints.Count - 1)];
        b.HitPoints = Math.Max(1, b.MaxHitPoints);
        b.Visibility = 4;
        b.Aux = kind >= 12 ? 2 : 0;
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                world.Map.SetCell(new GridPoint(origin.X + x, origin.Y + y), MapCell.BuildingAt(Slot));
            }
        }
        screen.View.Refresh(world);
    }

    private void Remove(GameWorld world, GridPoint origin, BuildingKind definition)
    {
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                world.Map.SetCell(new GridPoint(origin.X + x, origin.Y + y), MapCell.Empty);
            }
        }
        world.Building(Slot).HitPoints = 0;
        screen.View.Refresh(world);
    }

}
