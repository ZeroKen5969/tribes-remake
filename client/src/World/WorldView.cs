using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.World.Scene;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World;

/// <summary>
/// Shows a <see cref="GameWorld"/> as the original's map screen draws it: the terrain layer
/// (rasterised once per block of cells and redrawn only where the terrain changes) and above it
/// the object layer of the original (objects, units, animals, projectiles, fog, effects) composed
/// for the cells on screen. Map pixels are this node's local pixels: cell (x, y) starts at
/// (22x, 16y). The view only reads the world; call <see cref="OnRuleFrame"/> after each rule
/// frame with that frame's events. It works out the cells on screen every frame, so it fills any
/// window size; screens move their camera on <see cref="ViewResized"/>. An endless map grows while
/// it is shown: new land is drawn as it appears, tribes joining get their looks, and
/// <see cref="MapBounds"/> follows the land the viewer has explored.
/// </summary>
public partial class WorldView : Node2D
{
    /// <summary>Width of a map cell in pixels.</summary>
    public const int CellWidth = 22;

    /// <summary>Height of a map cell in pixels.</summary>
    public const int CellHeight = 16;

    private readonly SceneList scene = new();
    private readonly MotionTracker motion = new();
    private WorldArt art = null!;
    private ObjectLayer objects = null!;
    private Node2D objectCanvas = null!;
    private ViewRect composedView;
    private bool sceneDirty;
    private int pendingRuleFrames;
    private double sinceRuleFrame;
    private long drawnOffsetsKey;
    private Vector2 lastViewSize;
    private Rect2 lastBounds;
    private Rect2 mapBounds = new(0, 0, 1, 1);
    private WorldMarks marks = WorldMarks.None;
    private ViewerSight? sight;

    /// <summary>
    /// The view was resized (raised at the first frame with the new logical size): screens move
    /// their camera here; the cells on screen are worked out again for the next picture.
    /// </summary>
    [Signal]
    public delegate void ViewResizedEventHandler();

    /// <summary>The part of the map the camera may show changed (another world, or more of an endless map explored).</summary>
    [Signal]
    public delegate void MapBoundsChangedEventHandler(Rect2 bounds);

    /// <summary>Draw units, animals and projectiles moving smoothly between rule frames.</summary>
    [Export] public bool SmoothMotion { get; set; }

    /// <summary>
    /// Draw every building whose picture reaches onto the screen, also when its footprint lies
    /// above the rows the original scans (the original then shows it cut off at the top edge).
    /// Off by default, so the view stays pixel-identical to the original.
    /// </summary>
    [Export] public bool WholeBuildings { get; set; }

    /// <summary>
    /// Cells composed beyond each side of the screen when the camera follows (not with
    /// <see cref="ViewOverride"/>): while the camera pans within them the composition is reused, so
    /// it is made again at the rule frames rather than at every cell the camera crosses.
    /// </summary>
    [Export] public int ComposePadding { get; set; } = 3;

    /// <summary>Seconds between rule frames (the original's 120 ms).</summary>
    [Export] public float RuleFrameSeconds { get; set; } = 0.12f;

    /// <summary>The world shown, or null.</summary>
    public GameWorld? World { get; private set; }

    /// <summary>What only the screen keeps (effects, floating numbers, clocks).</summary>
    public ScenePresentation Presentation { get; private set; } = new();

    /// <summary>Tribe outfits and colours, and the viewing tribe (the home tribe of <see cref="Viewpoint"/>).</summary>
    public TribeLooks Looks { get; private set; } = new(0);

    /// <summary>Whose eyes the view uses (see <see cref="SetViewpoint"/>).</summary>
    public Viewpoint Viewpoint => sight?.Viewpoint ?? Viewpoint.Player(Looks.Viewer);

    /// <summary>
    /// What the view shows of the world: the fog (a player's own, which holds its allies' sight; a
    /// spectator's every human player's combined) and whose traps and markers are known. Null before
    /// <see cref="Show(GameWorld, Viewpoint, ScenePresentation)"/>. Screens read fog through it (minimap, picking).
    /// </summary>
    public ViewerSight? Sight => sight;

    /// <summary>Selection marks drawn with the units.</summary>
    public WorldMarks Marks
    {
        get => marks;
        set
        {
            marks = value ?? WorldMarks.None;
            sceneDirty = true;
        }
    }

    /// <summary>
    /// Cells to compose instead of the ones on screen (the dev scene reproduces the original's view
    /// with it); null follows the camera. The terrain always covers the whole window.
    /// </summary>
    public ViewRect? ViewOverride { get; set; }

    /// <summary>
    /// The part of the map the camera may show, in local pixels: the part in play of a campaign or
    /// custom map; the explored land of an endless map and a few cells around it (see <see cref="MapArea"/>).
    /// </summary>
    public Rect2 MapBounds => mapBounds;

    /// <summary>The eight tribe colours (names and swatches for menus).</summary>
    public static TribeColours Colours => WorldArt.Shared.Colours;

    /// <summary>The object-layer items of the last composition (for tests and tools).</summary>
    public IReadOnlyList<SceneItem> SceneItems => scene.Items;

    /// <summary>How far a unit or animal is drawn from its rule position right now (smooth motion), in map pixels.</summary>
    public (int X, int Y) MoverOffset(MoverKind mover, int id) =>
        SmoothMotion ? motion.Offset(mover, id, RuleFrameSeconds > 0 ? (float)(sinceRuleFrame / RuleFrameSeconds) : 1f) : (0, 0);

    /// <summary>The cells the last composition covered.</summary>
    public ViewRect ComposedView => composedView;

    /// <summary>Milliseconds the last composition of the object layer took.</summary>
    public double ComposeMilliseconds { get; private set; }

    /// <summary>Compositions of the object layer made so far.</summary>
    public long Compositions { get; private set; }

    /// <summary>Milliseconds the object layer's last drawing commands took (every frame while things move smoothly).</summary>
    public double DrawMilliseconds { get; private set; }

    /// <summary>Terrain blocks rasterised so far.</summary>
    public int TerrainRedraws { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        art = WorldArt.Shared;
        objects = new ObjectLayer(art.Assets);
        ReadyTerrain();
        objectCanvas = new Node2D { Name = "Objects" };
        AddChild(objectCanvas);
    }

    /// <summary>
    /// Shows <paramref name="world"/> as seen by the player of tribe <paramref name="viewer"/>, with
    /// <paramref name="presentation"/> (for example the original's screen state of a snapshot) or a
    /// fresh one. Tribe colours chosen before are kept.
    /// </summary>
    public void Show(GameWorld world, int viewer = 0, ScenePresentation? presentation = null) =>
        Show(world, Viewpoint.Player(viewer), presentation);

    /// <summary>
    /// Shows <paramref name="world"/> through <paramref name="viewpoint"/> (a player's, with its
    /// allies' sight, or a spectator's, with every human player's), with <paramref name="presentation"/>
    /// or a fresh one. Tribe colours chosen before are kept.
    /// </summary>
    public void Show(GameWorld world, Viewpoint viewpoint, ScenePresentation? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        int viewer = viewpoint.Tribe;
        if (viewer < 0 || viewer >= world.Tribes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(viewpoint), viewer, $"the world has tribes 0..{world.Tribes.Count - 1}");
        }
        var looks = new TribeLooks(viewer, world.Tribes.Count);
        // The colours chosen stay; tribes the last world did not have get free ones (see TribeLooks.Grow).
        TribeLooks carried = Looks.Grow(world.Tribes.Count);
        var colours = new int[world.Tribes.Count];
        for (int t = 0; t < colours.Length; t++)
        {
            colours[t] = carried.ColourOf(t);
        }
        if (!looks.TrySetColours(colours))
        {
            // Fewer tribes than before can leave a colour free while another repeats: start from the defaults.
            looks = new TribeLooks(viewer, world.Tribes.Count);
        }
        Looks = looks;
        World = world;
        sight = new ViewerSight(world, viewpoint);
        Presentation = presentation ?? new ScenePresentation();
        ClearTerrain();
        motion.Reset();
        motion.OnRuleFrame(world, Presentation);
        PrepareColours();
        WorldChanged();
    }

    /// <summary>
    /// Looks through <paramref name="viewpoint"/> from now on, keeping the world, the screen state,
    /// the colours and the terrain drawn (blocks are drawn again only where a trap shows differently):
    /// the fog, the traps and markers known and <see cref="MapBounds"/> follow it from the next picture.
    /// Nothing happens when it is the viewpoint already.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The home tribe is not one of the world's.</exception>
    /// <exception cref="InvalidOperationException">No world is shown.</exception>
    public void SetViewpoint(Viewpoint viewpoint)
    {
        if (World == null || sight == null)
        {
            throw new InvalidOperationException("show a world first");
        }
        if (viewpoint == sight.Viewpoint)
        {
            return;
        }
        sight = new ViewerSight(World, viewpoint);
        GrowLooks(World);
        Looks = Looks.WithViewer(viewpoint.Tribe);
        sceneDirty = true;
        pendingRuleFrames = Math.Max(1, pendingRuleFrames);
        terrainVersion++;
        UpdateBounds();
    }

    /// <summary>
    /// One rule frame has run: the screen state moves on with its events, and the next picture
    /// shows the new state (effects advance by one frame).
    /// </summary>
    public void OnRuleFrame(IReadOnlyList<GameEvent> events)
    {
        if (World == null)
        {
            return;
        }
        Presentation.OnRuleFrame(World, events ?? Array.Empty<GameEvent>());
        motion.OnRuleFrame(World, Presentation);
        pendingRuleFrames++;
        sinceRuleFrame = 0;
        sceneDirty = true;
        terrainVersion++;
        GrowLooks(World);
        sight?.Refresh();
        UpdateBounds();
    }

    /// <summary>
    /// The world was replaced or changed outside rule frames (a replay step, a world edited by a
    /// tool): redraw everything from it, keeping the tribe looks, the viewpoint and, unless one is
    /// given, the screen state.
    /// </summary>
    public void Refresh(GameWorld world, ScenePresentation? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        Viewpoint viewpoint = Viewpoint;
        if (World == null || world.Tribes.Count < Looks.Tribes || viewpoint.Tribe >= world.Tribes.Count)
        {
            Show(world, viewpoint.Tribe < world.Tribes.Count ? viewpoint : Viewpoint.Player(0), presentation);
            return;
        }
        GrowLooks(world);
        if (!ReferenceEquals(world, World))
        {
            sight = new ViewerSight(world, viewpoint);
        }
        World = world;
        sight!.Refresh();
        if (presentation != null)
        {
            Presentation = presentation;
        }
        motion.OnRuleFrame(world, Presentation);
        WorldChanged();
    }

    private void WorldChanged()
    {
        pendingRuleFrames = Math.Max(1, pendingRuleFrames);
        sinceRuleFrame = 0;
        sceneDirty = true;
        terrainVersion++;
        UpdateBounds();
    }

    /// <summary>Works out <see cref="MapBounds"/> again and tells when it changed.</summary>
    private void UpdateBounds()
    {
        if (World == null || sight == null)
        {
            return;
        }
        CellRect area = MapArea.CameraArea(World, sight);
        mapBounds = area.IsEmpty
            ? new Rect2(0, 0, 1, 1)
            : new Rect2(area.Left * CellWidth, area.Top * CellHeight, area.Width * CellWidth, area.Height * CellHeight);
        if (mapBounds != lastBounds)
        {
            lastBounds = mapBounds;
            EmitSignal(SignalName.MapBoundsChanged, lastBounds);
        }
    }

    /// <summary>Tribes that joined <paramref name="world"/> since it was shown get their looks (the others keep theirs).</summary>
    private void GrowLooks(GameWorld world)
    {
        if (world.Layout.Tribes > Looks.Tribes)
        {
            Looks = Looks.Grow(world.Layout.Tribes);
            PrepareColours();
        }
    }

    /// <summary>
    /// Gives <paramref name="tribe"/> colour <paramref name="colourIndex"/> (0..7, see
    /// <see cref="Colours"/>); false when another tribe has it already.
    /// </summary>
    public bool SetTribeColour(int tribe, int colourIndex)
    {
        bool done = Looks.TrySetColour(tribe, colourIndex);
        if (done)
        {
            PrepareColours();
            RedrawRuns();
        }
        return done;
    }

    /// <summary>
    /// Gives every tribe its colour at once (index = tribe), so colours can be swapped; false when
    /// a colour repeats.
    /// </summary>
    public bool SetTribeColours(IReadOnlyList<int> colourByTribe)
    {
        bool done = Looks.TrySetColours(colourByTribe);
        if (done)
        {
            PrepareColours();
            RedrawRuns();
        }
        return done;
    }

    /// <summary>The colour index of <paramref name="tribe"/>.</summary>
    public int TribeColour(int tribe) => Looks.ColourOf(tribe);

    /// <summary>The map cell under local point <paramref name="point"/>.</summary>
    public static Vector2I CellAt(Vector2 point) => new(Mathf.FloorToInt(point.X / CellWidth), Mathf.FloorToInt(point.Y / CellHeight));

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        CheckViewSize();
        if (World == null)
        {
            return;
        }
        sinceRuleFrame += delta;
        ViewRect visible = ViewOverride ?? VisibleView();
        float progress = RuleFrameSeconds > 0 ? (float)(sinceRuleFrame / RuleFrameSeconds) : 1f;
        bool covered = ViewOverride != null ? visible == composedView : Covers(composedView, visible);
        if (sceneDirty || !covered)
        {
            ViewRect view = ViewOverride ?? Padded(visible, Math.Max(0, ComposePadding));
            if (pendingRuleFrames > 1)
            {
                // Rule frames that were never drawn: their effects have moved on all the same.
                Presentation.SkipEffectFrames(pendingRuleFrames - 1, art.Assets.Catalog);
            }
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            objects.ExtraRowsAbove = WholeBuildings ? ObjectLayer.RowsForWholeBuildings : 0;
            objects.Compose(new SceneRequest(World, view, Presentation, Marks, Looks, pendingRuleFrames > 0, sight), scene);
            TellFault(objects.LastFault);
            ComposeMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Compositions++;
            pendingRuleFrames = 0;
            sceneDirty = false;
            composedView = view;
            PrepareRegions();
            drawnOffsetsKey = SmoothMotion ? motion.OffsetsKey(progress) : 0;
            BuildRuns();
        }
        else if (AtlasReplaced)
        {
            BuildRuns();
        }
        else if (SmoothMotion)
        {
            long key = motion.OffsetsKey(progress);
            if (key != drawnOffsetsKey)
            {
                drawnOffsetsKey = key;
                MoveRuns();
            }
        }
        // The terrain covers the whole window even when the objects follow a fixed view.
        UpdateTerrain(ViewOverride == null ? visible : VisibleView());
    }

    /// <summary>Whether composition <paramref name="outer"/> holds every cell of <paramref name="inner"/>.</summary>
    private static bool Covers(ViewRect outer, ViewRect inner) =>
        inner.Column >= outer.Column && inner.Row >= outer.Row
        && inner.Column + inner.Columns <= outer.Column + outer.Columns && inner.Row + inner.Rows <= outer.Row + outer.Rows;

    private static ViewRect Padded(ViewRect view, int cells) =>
        new(view.Column - cells, view.Row - cells, view.Columns + (2 * cells), view.Rows + (2 * cells));

    /// <summary>
    /// Raises <see cref="ViewResized"/> once a new size is final: the application rescales the
    /// logical screen during the resize, so the size is compared every frame.
    /// </summary>
    private void CheckViewSize()
    {
        Vector2 size = GetViewportRect().Size;
        if (size != lastViewSize)
        {
            lastViewSize = size;
            sceneDirty = true;
            terrainCanvas?.QueueRedraw();
            EmitSignal(SignalName.ViewResized);
        }
    }

    /// <summary>The cells the camera shows now.</summary>
    private ViewRect VisibleView()
    {
        Transform2D toLocal = GetGlobalTransformWithCanvas().AffineInverse();
        Rect2 screen = GetViewportRect();
        Vector2 a = toLocal * screen.Position;
        Vector2 b = toLocal * screen.End;
        int x0 = Mathf.FloorToInt(Mathf.Min(a.X, b.X) / CellWidth);
        int y0 = Mathf.FloorToInt(Mathf.Min(a.Y, b.Y) / CellHeight);
        int x1 = Mathf.CeilToInt(Mathf.Max(a.X, b.X) / CellWidth);
        int y1 = Mathf.CeilToInt(Mathf.Max(a.Y, b.Y) / CellHeight);
        return new ViewRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>Puts the sheets of every tribe's colour in the atlas at once (one upload instead of one per sheet).</summary>
    private void PrepareColours()
    {
        if (art == null)
        {
            return;
        }
        for (int t = 0; t < Looks.Tribes; t++)
        {
            art.Prepare(Looks.ColourOf(t), TribeLooks.OutfitOf(t));
        }
    }

    /// <summary>Makes sure every recoloured sheet the composition uses is in the atlas before it is drawn.</summary>
    private void PrepareRegions()
    {
        for (int i = 0; i < scene.Count; i++)
        {
            SceneItem item = scene[i];
            if (item.SkinTribe >= 0 && item.Part.Sheet != null)
            {
                art.Region(item.Part.Sheet, Looks.ColourOf(item.SkinTribe), TribeLooks.OutfitOf(item.SkinTribe));
            }
        }
    }

    private string? toldFault;

    /// <summary>
    /// Writes a warning the first time a kind of failure stops the object layer early in a world of
    /// the remake's own (endless, many tribes), where the original's slips cannot be the cause.
    /// </summary>
    private void TellFault(Exception? fault)
    {
        if (fault == null || World == null || (!MapArea.IsChunked(World.Map) && World.Layout.Tribes <= 2))
        {
            return;
        }
        string kind = fault.GetType().Name + ": " + fault.Message;
        if (kind != toldFault)
        {
            toldFault = kind;
            GD.PushWarning($"the object layer stopped early: {kind}");
        }
    }

    /// <summary>The runs show the colours chosen now (after the view is in the tree).</summary>
    private void RedrawRuns()
    {
        if (objectCanvas != null && runsUsed > 0)
        {
            BuildRuns();
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => FreeRuns();
}
