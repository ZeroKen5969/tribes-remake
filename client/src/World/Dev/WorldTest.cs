using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Tribes.Client.Input;
using Tribes.Client.Map;
using Tribes.Client.World.Scene;
using Tribes.Core;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.World.Dev;

/// <summary>
/// Development scene of the world view (scenes/world/world_test.tscn). Shows a saved world or plays
/// a replay. User arguments (after <c>--</c>):
/// <list type="bullet">
/// <item><c>--world=PATH</c>: a <c>.trbw</c> snapshot; with its <c>.json</c> sidecar the view is
/// placed exactly like the original's camera at that frame (zoom 1, map at the original's screen
/// position) and shows what the original's screen kept; at the original's screen size it composes
/// exactly the original's cells.</item>
/// <item><c>--replay=PATH</c>: a <c>.trbr</c> replay, played at the rules' pace in a loop with a free camera.</item>
/// <item><c>--mission=N</c>: a mission's start map without units, all of it explored (a large
/// map for frame-rate checks).</item>
/// <item><c>--match=MAP</c>, <c>--endless=SEED</c> and the other arguments of WorldTest.Sim.cs: a game of its own, with many tribes;
/// <c>--minimap</c> (WorldTest.Minimap.cs): an endless map's minimap at the top right;
/// <c>--lineup</c> and <c>--look=T</c> (WorldTest.Lineup.cs): every tribe's villagers in a row, the camera over a camp;
/// <c>--memory-demo</c> (WorldTest.Memory.cs): buildings shown as last seen.</item>
/// <item><c>--colours=C0,C1,...</c>: tribe colours (0..7) of tribes 0, 1, ...</item>
/// <item><c>--smooth</c>: smooth motion between rule frames. <c>--zoom=Z</c>: camera zoom.</item>
/// <item><c>--free</c>: free camera even when a sidecar gives the original's.</item>
/// <item><c>--hide-status</c>: no status line at the top left (for screenshots).</item>
/// <item><c>--fps-log</c>: prints <c>WORLD_FPS</c> lines every two seconds (frame rate, cells and
/// pictures composed, the slowest terrain update, terrain blocks kept, memory: the engine's, the
/// textures' and the managed heap's, in MB).</item>
/// <item><c>--fill-check</c>: the clear colour turns magenta and every <c>WORLD_FPS</c> line also
/// counts the pixels of the window nothing was drawn on (for rotating a device while it runs).</item>
/// <item><c>--resize-test=WxH,WxH,...</c>: resizes the window to each size in turn and, once the
/// picture has settled, saves it as <c>--resize-out</c>/resize-WxH.png and prints how many pixels were
/// left unfilled (the clear colour is magenta during the test); then quits.</item>
/// <item><c>--tour=CELLS</c>: the camera flies a figure of eight over the whole of
/// <see cref="WorldView.MapBounds"/> at CELLS cells per second (memory and frame rate while panning).</item>
/// <item><c>--capture=DIR</c>: saves <c>--capture-count</c> consecutive pictures (from the
/// <c>--capture-start</c>th) as DIR/frameNNN.png and quits, for checking motion.</item>
/// </list>
/// Paths are res://, user:// or file paths; relative paths are taken from res://.
/// </summary>
public partial class WorldTest : Node2D
{
    private const string DefaultWorld = "res://dev/snapshots/s4-f06000.trbw";
    private const double FpsLogSeconds = 2;

    private WorldView view = null!;
    private MapCamera camera = null!;
    private Label status = null!;
    private IReadOnlyList<ReplayFile.Entry>? replay;
    private GameWorld?[] replayWorlds = Array.Empty<GameWorld?>();
    private int replayIndex;
    private double replayClock, fpsClock, terrainPeak, tourClock, composePeak, drawPeak, framePeak;
    private double composeSum, drawSum, terrainSum;
    private int frames, composeCount;
    private long composedSeen;
    private int[] gcSeen = new int[3];
    private float tourSpeed;
    private bool fpsLog, fillCheck;
    private GameContent content = null!;
    private readonly List<double> fpsSamples = new();
    private string? captureDir;
    private int captureStart = 60, captureCount = 24, shown;
    private SnapshotView? originalPlacement;
    private Vector2I[] resizeSizes = Array.Empty<Vector2I>();
    private string resizeOut = "user://";
    private int resizeStep = -1, resizeWait;
    private const int ResizeSettleFrames = 20;

    /// <inheritdoc/>
    public override void _Ready()
    {
        view = GetNode<WorldView>("World");
        camera = GetNode<MapCamera>("Camera");
        status = GetNode<Label>("Hud/Status");
        var gestures = GetNode<PointerGestures>("Gestures");
        gestures.Panned += camera.PanBy;
        gestures.Zoomed += camera.ZoomBy;
        content = App.GameApp.Instance.Content;
        Dictionary<string, string> args = Arguments();
        fpsLog = args.ContainsKey("fps-log");
        fillCheck = args.ContainsKey("fill-check");
        status.Visible = !args.ContainsKey("hide-status");
        if (fillCheck)
        {
            RenderingServer.SetDefaultClearColor(Colors.Magenta);
        }
        captureDir = args.TryGetValue("capture", out string? dir) ? dir : null;
        captureStart = args.TryGetValue("capture-start", out string? cs) ? int.Parse(cs, CultureInfo.InvariantCulture) : captureStart;
        captureCount = args.TryGetValue("capture-count", out string? cc) ? int.Parse(cc, CultureInfo.InvariantCulture) : captureCount;
        view.SmoothMotion = args.ContainsKey("smooth");
        view.ViewResized += OnViewResized;
        view.MapBoundsChanged += bounds => camera.Bounds = bounds;
        if (args.TryGetValue("resize-test", out string? sizes))
        {
            resizeSizes = sizes.Split(',').Select(ParseSize).ToArray();
            resizeOut = args.TryGetValue("resize-out", out string? ro) ? ro : resizeOut;
            RenderingServer.SetDefaultClearColor(Colors.Magenta);
        }
        float zoom = args.TryGetValue("zoom", out string? z) ? float.Parse(z, CultureInfo.InvariantCulture) : 2f;
        tourSpeed = args.TryGetValue("tour", out string? tour) ? float.Parse(tour, CultureInfo.InvariantCulture) : 0f;
        if (StartSimulation(args, zoom))
        {
            // The game it started is on screen.
        }
        else if (args.TryGetValue("replay", out string? replayPath))
        {
            replay = ReplayFile.Read(ReadBytes(replayPath));
            // The worlds are read once, so playing measures the view rather than loading.
            replayWorlds = new GameWorld?[replay.Count];
            for (int i = 0; i < replay.Count; i++)
            {
                replayWorlds[i] = replay[i].Load(content);
            }
            ShowReplayFrame(0);
            PlaceFree(zoom, replay[0].View);
        }
        else if (args.TryGetValue("mission", out string? mission))
        {
            int id = int.Parse(mission, CultureInfo.InvariantCulture);
            WorldMap map = MapBuilder.Build(id, content, new GameRandom(1));
            var world = new GameWorld(content, map, WorldLayout.Original, new GameRandom(1));
            FogOfWar fog = world.Fog(0);
            for (int y = 0; y < fog.Size.Height; y++)
            {
                for (int x = 0; x < fog.Size.Width; x++)
                {
                    fog.SetExplored(new GridPoint(x, y), true);
                    fog.SetVisible(new GridPoint(x, y), true);
                }
            }
            view.Show(world);
            PlaceFree(zoom, null);
        }
        else
        {
            string path = args.TryGetValue("world", out string? w) ? w : DefaultWorld;
            GameWorld world = LoadSnapshot(path);
            SnapshotView? sidecar = TryReadSidecar(path);
            view.Show(world, 0, sidecar?.Presentation());
            if (sidecar != null)
            {
                view.Marks = sidecar.Marks;
            }
            if (sidecar != null && !args.ContainsKey("free"))
            {
                PlaceLikeOriginal(sidecar);
            }
            else
            {
                PlaceFree(zoom, sidecar);
            }
        }
        if (args.TryGetValue("colours", out string? colours))
        {
            int[] picks = colours.Split(',').Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToArray();
            bool done = view.SetTribeColours(picks);
            GD.Print($"WORLD_TEST colours {colours}: {(done ? "set" : "refused (a colour repeats or is not 0..7)")}");
        }
        ReadyMinimap(args);
        GD.Print($"WORLD_TEST_READY world={(simulation != null ? "game" : replay != null ? "replay " + replay.Count + " frames" : "snapshot")} smooth={view.SmoothMotion}");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        StepResizeTest();
        if (captureDir != null && ++shown > captureStart)
        {
            int index = shown - captureStart - 1;
            GetViewport().GetTexture().GetImage().SavePng($"{captureDir}/frame{index:D3}.png");
            if (index + 1 >= captureCount)
            {
                GD.Print($"WORLD_TEST captured {captureCount} pictures in {captureDir}");
                GetTree().Quit();
            }
        }
        if (replay != null)
        {
            replayClock += delta;
            if (replayClock >= view.RuleFrameSeconds)
            {
                replayClock -= view.RuleFrameSeconds;
                ShowReplayFrame((replayIndex + 1) % replay.Count);
            }
        }
        Tour(delta);
        StepSimulation(delta);
        fpsClock += delta;
        terrainPeak = Math.Max(terrainPeak, view.TerrainMilliseconds);
        composePeak = Math.Max(composePeak, view.ComposeMilliseconds);
        drawPeak = Math.Max(drawPeak, view.DrawMilliseconds);
        framePeak = Math.Max(framePeak, delta * 1000);
        if (view.Compositions != composedSeen)
        {
            composedSeen = view.Compositions;
            composeSum += view.ComposeMilliseconds;
            composeCount++;
        }
        drawSum += view.DrawMilliseconds;
        terrainSum += view.TerrainMilliseconds;
        frames++;
        if (fpsClock >= FpsLogSeconds)
        {
            fpsClock = 0;
            double fps = Engine.GetFramesPerSecond();
            fpsSamples.Add(fps);
            if (fpsLog)
            {
                Vector2 size = GetViewportRect().Size;
                string unfilled = fillCheck ? " unfilled " + Unfilled(GetViewport().GetTexture().GetImage()) : string.Empty;
                GD.Print(string.Create(CultureInfo.InvariantCulture,
                    $"WORLD_FPS{unfilled} {fps:0} view {size.X:0}x{size.Y:0} cells {view.ComposedView.Columns}x{view.ComposedView.Rows} items {view.SceneItems.Count} zoom {camera.Zoom.X:0.##} compose {view.ComposeMilliseconds:0.0}ms terrain {view.TerrainRedraws} peak {terrainPeak:0.0}ms per-frame avg/max compose {composeSum / Math.Max(1, frames):0.0}/{composePeak:0.0} ({composeCount} compositions) draw {drawSum / Math.Max(1, frames):0.0}/{drawPeak:0.0} terrain {terrainSum / Math.Max(1, frames):0.0} slowest-frame {framePeak:0}ms blocks {view.TerrainBlocksKept} sim {simulationMilliseconds:0.0}/{simulationPeak:0.0}ms gc {GC.CollectionCount(0) - gcSeen[0]}/{GC.CollectionCount(1) - gcSeen[1]}/{GC.CollectionCount(2) - gcSeen[2]} tribes {view.World?.Tribes.Count} mem {Megabytes((long)OS.GetStaticMemoryUsage()):0} tex {Megabytes((long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed)):0} heap {Megabytes(GC.GetTotalMemory(false)):0} min {fpsSamples.Min():0} avg {fpsSamples.Average():0.0}"));
            }
            if (fpsLog && SimulationSummary() is { } steps)
            {
                GD.Print(steps);
            }
            terrainPeak = composePeak = drawPeak = framePeak = composeSum = drawSum = terrainSum = simulationPeak = 0;
            gcSeen = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            frames = composeCount = 0;
        }
        if (status.Visible)
        {
            status.Text = string.Create(CultureInfo.InvariantCulture,
                $"{Engine.GetFramesPerSecond():0} fps  zoom {camera.Zoom.X:0.##}  frame {view.World?.Frame}  items {view.SceneItems.Count}");
        }
    }

    /// <summary>Flies the camera along a figure of eight over the map (<c>--tour</c>).</summary>
    private void Tour(double delta)
    {
        if (tourSpeed <= 0)
        {
            return;
        }
        Rect2 bounds = view.MapBounds;
        Vector2 half = bounds.Size / 2;
        // About the length of the figure, in cells, so the camera keeps the speed asked for.
        float length = 2.5f * ((half.X / WorldView.CellWidth) + (half.Y / WorldView.CellHeight));
        tourClock += delta * tourSpeed / Math.Max(1f, length) * Math.PI * 2;
        camera.Bounds = bounds;
        camera.CentreOn(bounds.GetCenter() + new Vector2(half.X * 0.9f * (float)Math.Sin(tourClock), half.Y * 0.9f * (float)Math.Sin(2 * tourClock)));
    }

    private void ShowReplayFrame(int index)
    {
        replayIndex = index;
        ReplayFile.Entry entry = replay![index];
        GameWorld world = replayWorlds[index] ??= entry.Load(content);
        if (view.World == null)
        {
            view.Show(world, 0, entry.View.Presentation());
        }
        else
        {
            view.Refresh(world, entry.View.Presentation());
        }
        view.Marks = entry.View.Marks;
    }

    /// <summary>After a resize the camera keeps its place: the original's view stays at the top-left, a free view stays over the map.</summary>
    private void OnViewResized()
    {
        if (originalPlacement != null)
        {
            PlaceLikeOriginal(originalPlacement);
        }
        else
        {
            Vector2 centre = camera.GetScreenCenterPosition();
            camera.Bounds = view.MapBounds;
            camera.CentreOn(centre);
        }
    }

    /// <summary>One step of the resize test: resize, let the picture settle, save it and count unfilled pixels.</summary>
    private void StepResizeTest()
    {
        if (resizeSizes.Length == 0)
        {
            return;
        }
        if (resizeStep < 0 || ++resizeWait >= ResizeSettleFrames)
        {
            if (resizeStep >= 0)
            {
                Image image = GetViewport().GetTexture().GetImage();
                int unfilled = Unfilled(image);
                Vector2I window = DisplayServer.WindowGetSize();
                Vector2 logical = GetViewportRect().Size;
                string file = $"{resizeOut}/resize-{window.X}x{window.Y}.png";
                image.SavePng(file);
                GD.Print(string.Create(CultureInfo.InvariantCulture,
                    $"RESIZE_TEST window {window.X}x{window.Y} logical {logical.X:0}x{logical.Y:0} picture {image.GetWidth()}x{image.GetHeight()} unfilled {unfilled} cells {view.ComposedView.Columns}x{view.ComposedView.Rows} saved {file}"));
            }
            resizeStep++;
            resizeWait = 0;
            if (resizeStep >= resizeSizes.Length)
            {
                resizeSizes = Array.Empty<Vector2I>();
                GetTree().Quit();
                return;
            }
            DisplayServer.WindowSetSize(resizeSizes[resizeStep]);
        }
    }

    /// <summary>Pixels of <paramref name="image"/> left in the (magenta) clear colour.</summary>
    private static int Unfilled(Image image)
    {
        byte[] data = image.GetData();
        int stride = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        int count = 0;
        for (int i = 0; i + 2 < data.Length; i += stride)
        {
            count += data[i] == 255 && data[i + 1] == 0 && data[i + 2] == 255 ? 1 : 0;
        }
        return count;
    }

    private static double Megabytes(long bytes) => bytes / (1024.0 * 1024.0);

    private static Vector2I ParseSize(string text)
    {
        string[] wh = text.Split('x');
        return new Vector2I(int.Parse(wh[0], CultureInfo.InvariantCulture), int.Parse(wh[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Zoom 1, the view's top-left cell where the original showed it, the original's cells composed.</summary>
    private void PlaceLikeOriginal(SnapshotView sidecar)
    {
        originalPlacement = sidecar;
        status.Visible = false;
        // At the original's screen size the original's own cells are composed (its order and slips
        // depend on them); a larger window shows everything on it from the same camera.
        Vector2 logical = GetViewportRect().Size;
        bool originalSize = (int)logical.X == sidecar.ScreenWidth && (int)logical.Y == sidecar.ScreenHeight;
        view.ViewOverride = originalSize ? sidecar.View : null;
        camera.Zoom = Vector2.One;
        Vector2 size = GetViewportRect().Size;
        camera.Position = new Vector2(sidecar.CameraColumn * WorldView.CellWidth, (sidecar.CameraRow * WorldView.CellHeight) - sidecar.MapTop)
            + (size / 2).Floor();
    }

    private void PlaceFree(float zoom, SnapshotView? sidecar)
    {
        camera.Bounds = view.MapBounds;
        camera.Zoom = new Vector2(zoom, zoom);
        Vector2 centre = sidecar != null
            ? new Vector2((sidecar.CameraColumn + (sidecar.Columns / 2f)) * WorldView.CellWidth, (sidecar.CameraRow + (sidecar.Rows / 2f)) * WorldView.CellHeight)
            : camera.Bounds.GetCenter();
        camera.CentreOn(centre);
    }

    /// <summary>A saved world; a clear message when it was written in another world format.</summary>
    private GameWorld LoadSnapshot(string path)
    {
        try
        {
            return WorldSnapshot.Load(new MemoryStream(ReadBytes(path)), content);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
        {
            GD.PushError($"WORLD_TEST cannot load {path} ({e.Message}); snapshots must be written again after the world "
                + "format changes: dotnet run --project tools/Tribes.DevSnapshots -- capture ...");
            throw;
        }
    }

    private static SnapshotView? TryReadSidecar(string worldPath)
    {
        string json = Path.ChangeExtension(Resolve(worldPath), ".json");
        return Godot.FileAccess.FileExists(json) ? SnapshotView.Parse(Godot.FileAccess.GetFileAsString(json)) : null;
    }

    private static byte[] ReadBytes(string path)
    {
        string resolved = Resolve(path);
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(resolved);
        if (bytes.Length == 0)
        {
            throw new FileNotFoundException($"cannot read {resolved}: {Godot.FileAccess.GetOpenError()}");
        }
        return bytes;
    }

    private static string Resolve(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal) || Path.IsPathRooted(path)
            ? path
            : "res://" + path.Replace('\\', '/');

    private static Dictionary<string, string> Arguments()
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            int eq = arg.IndexOf('=', StringComparison.Ordinal);
            args[eq < 0 ? arg[2..] : arg[2..eq]] = eq < 0 ? "true" : arg[(eq + 1)..];
        }
        return args;
    }
}
