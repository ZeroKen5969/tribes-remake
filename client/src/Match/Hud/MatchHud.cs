using System;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The match interface over the map: the top bar, the players, messages, minimap, the panel of
/// the selection, the big buttons, the placement bar, short notes, and the layers for panels,
/// story scenes and questions. Lays everything out for the window (landscape or portrait, finger
/// or mouse) on every resize, and tells the camera how much of the screen it covers.
/// </summary>
public partial class MatchHud : CanvasLayer
{
    private readonly IMatchActions actions;
    private Control root = null!;
    private PanelContainer minimapFrame = null!;
    private Control notes = null!;
    private readonly BoxMark selectionBox = new() { Name = "SelectionBox", MouseFilter = Control.MouseFilterEnum.Ignore };
    private HudPopup? popup;
    private bool minimapShown = true;
    private Vector2 laidOutFor;
    private int laidOutButton;
    private bool laidOutPortrait, laidOutTouch;

    /// <summary>The interface of the match <paramref name="actions"/> plays.</summary>
    public MatchHud(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The top bar.</summary>
    public TopBar Top { get; private set; } = null!;

    /// <summary>The players of a network match.</summary>
    public PlayersStrip Players { get; private set; } = null!;

    /// <summary>The message list.</summary>
    public MessageFeed Messages { get; private set; } = null!;

    /// <summary>The minimap.</summary>
    public MinimapView Minimap { get; private set; } = null!;

    /// <summary>The panel of the selection.</summary>
    public ContextPanel Context { get; private set; } = null!;

    /// <summary>The strip of head counts under the top bar (gatherers of each resource, idle villagers).</summary>
    public WorkforceBar Workers { get; private set; } = null!;

    /// <summary>The big buttons.</summary>
    public CommandBar Commands { get; private set; } = null!;

    /// <summary>The bar shown while placing a building or picking a target.</summary>
    public PlacementBar Placement { get; private set; } = null!;

    /// <summary>The story scene.</summary>
    public SceneDialog Scene { get; private set; } = null!;

    /// <summary>The banner of a player who only watches.</summary>
    public Label Banner { get; private set; } = null!;

    /// <summary>Where questions with buttons go (over everything).</summary>
    public Control Dialogs { get; private set; } = null!;

    /// <summary>The panel open now, if any.</summary>
    public HudPopup? Popup => popup != null && IsInstanceValid(popup) && popup.IsInsideTree() ? popup : null;

    /// <summary>The screen space the interface covers (for the camera).</summary>
    public ScreenInsets Insets { get; private set; }

    /// <summary>The part of the screen not covered by bars (where panels are centred).</summary>
    public Rect2 FreeArea { get; private set; }

    /// <summary>Whether the minimap is shown.</summary>
    public bool MinimapShown
    {
        get => minimapShown;
        set
        {
            minimapShown = value;
            minimapFrame.Visible = value && !Scene.Visible;
            // The player list moves up into its place.
            Relayout();
            if (value)
            {
                // It is not painted while hidden.
                Minimap.Refresh(actions.Runner.Simulation.Tick, force: true);
            }
            Commands.SetToggled(HudCommand.Minimap, value);
        }
    }

    /// <summary>The interface was laid out anew (the camera's limits follow).</summary>
    public event Action? LaidOut;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "Hud";
        Layer = 10;
        root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore, Theme = PixelTheme.Current };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(selectionBox);
        selectionBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Messages = new MessageFeed();
        root.AddChild(Messages);
        Players = new PlayersStrip(actions);
        root.AddChild(Players);
        minimapFrame = new PanelContainer { Name = "MinimapFrame", ThemeTypeVariation = PixelTheme.Variations.Inset, MouseFilter = Control.MouseFilterEnum.Stop };
        Minimap = new MinimapView(actions);
        minimapFrame.AddChild(Minimap);
        root.AddChild(minimapFrame);
        Context = new ContextPanel(actions);
        Context.ContentChanged += () => CallDeferred(MethodName.Relayout);
        root.AddChild(Context);
        Top = new TopBar(actions);
        root.AddChild(Top);
        Workers = new WorkforceBar(actions);
        // Its width follows the counts: the message list under it moves with its height.
        Workers.MinimumSizeChanged += () => CallDeferred(MethodName.Relayout);
        root.AddChild(Workers);
        Commands = new CommandBar(actions);
        root.AddChild(Commands);
        Placement = new PlacementBar(actions);
        root.AddChild(Placement);
        Banner = HudWidgets.Value("", PixelTheme.Variations.OnDark);
        Banner.Name = "Banner";
        Banner.HorizontalAlignment = HorizontalAlignment.Center;
        Banner.AddThemeColorOverride("font_shadow_color", UiPalette.Ink);
        Banner.AddThemeConstantOverride("shadow_offset_x", 1);
        Banner.AddThemeConstantOverride("shadow_offset_y", 1);
        root.AddChild(Banner);
        notes = new Control { Name = "Notes", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(notes);
        var popups = new Control { Name = "Popups", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(popups);
        popups.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Scene = new SceneDialog(actions);
        root.AddChild(Scene);
        Dialogs = new Control { Name = "Dialogs", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(Dialogs);
        Dialogs.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Resized += Relayout;
        Relayout();
    }

    /// <summary>The layer panels are added to.</summary>
    private Control PopupLayer => root.GetNode<Control>("Popups");

    /// <summary>Opens <paramref name="panel"/> (closing the one open before).</summary>
    public void Open(HudPopup panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (Popup is ResultsPanel)
        {
            // The result stays until the player picks what comes next.
            panel.QueueFree();
            return;
        }
        CloseRawPopup();
        popup = panel;
        if (panel.Hold is { } reason)
        {
            actions.Hold(reason, true);
            panel.Closed += () => actions.Hold(reason, false);
        }
        panel.Closed += () =>
        {
            if (popup == panel)
            {
                popup = null;
            }
        };
        PopupLayer.AddChild(panel);
        panel.Place(FreeArea);
        panel.CallDeferred(HudPopup.MethodName.Place, FreeArea);
    }

    /// <summary>Closes the open panel, if any; false when none was open.</summary>
    public bool ClosePopup()
    {
        HudPopup? open = Popup;
        if (open is null)
        {
            return false;
        }
        open.Close();
        return true;
    }

    /// <summary>Draws the selection box being dragged (screen pixels), or none.</summary>
    public void QueueRedrawBox(Rect2? rect)
    {
        selectionBox.Box = rect;
        selectionBox.QueueRedraw();
    }

    /// <summary>Shows a short note under the top bar.</summary>
    public void Note(string text) => Toast.Show(notes, text);

    /// <summary>Shows or hides everything but the story scene (while a scene plays).</summary>
    public void SetSceneMode(bool scene)
    {
        Top.Visible = !scene;
        Workers.Visible = !scene;
        Commands.Visible = !scene;
        Messages.Visible = !scene;
        minimapFrame.Visible = !scene && minimapShown;
        Context.Suppressed = scene;
        Placement.Visible = !scene && Placement.Active;
        Players.Visible = !scene && actions.Runner.IsNetwork;
    }

    /// <summary>Lays everything out again when the window or the interface sizes changed.</summary>
    public void Relayout()
    {
        if (root is null || !root.IsInsideTree())
        {
            return;
        }
        HudMetrics m = actions.Metrics;
        Vector2 size = root.Size;
        if (size != laidOutFor || m.Button != laidOutButton || m.Portrait != laidOutPortrait || m.Touch != laidOutTouch)
        {
            laidOutFor = size;
            laidOutButton = m.Button;
            laidOutPortrait = m.Portrait;
            laidOutTouch = m.Touch;
            Top.Arrange(m);
            Workers.Arrange(m);
            Commands.Arrange(m, vertical: !m.Portrait);
            Commands.SetToggled(HudCommand.Minimap, minimapShown);
        }
        if (m.Portrait)
        {
            LayOutPortrait(m, size);
        }
        else
        {
            LayOutLandscape(m, size);
        }
        Popup?.Place(FreeArea);
        Scene.Size = size;
        Scene.Arrange();
        LaidOut?.Invoke();
    }

    private void LayOutLandscape(HudMetrics m, Vector2 size)
    {
        int gap = m.Gap;
        float strip = m.Button + (2 * gap);
        float top = m.TopBar;
        Place(Top, new Rect2(0, 0, size.X - (m.Touch ? strip : 0), top));
        Place(Commands, new Rect2(size.X - strip + gap, m.Touch ? gap : top + gap, m.Button, size.Y - (m.Touch ? 0 : top) - (2 * gap)));
        float right = size.X - strip;
        float mapWidth = right;
        float miniWidth = Mathf.Clamp(mapWidth * 0.24f, 96, 170);
        float miniHeight = Mathf.Round(miniWidth * 0.75f);
        Place(minimapFrame, new Rect2(right - miniWidth - gap, top + gap, miniWidth, miniHeight));
        // Wide enough for a villager's orders on one line beside the cross, also with big touch buttons.
        float panelWidth = Mathf.Min(Mathf.Max(Mathf.Clamp(mapWidth * 0.42f, 200, 320), Context.OrdersWidth(m)), mapWidth - (2 * gap));
        if (Context.Folded)
        {
            // The folded tab is only as wide as its face, name and buttons.
            panelWidth = Mathf.Min(panelWidth, Context.FoldedWidth);
        }
        float panelHeightMax = size.Y - top - (3 * gap);
        Context.CustomMinimumSize = new Vector2(panelWidth, 0);
        Context.Size = new Vector2(panelWidth, 0);
        float panelHeight = Mathf.Min(panelHeightMax, Context.WantedHeight);
        Place(Context, new Rect2(gap, size.Y - panelHeight - gap, panelWidth, panelHeight));
        float feedWidth = Mathf.Max(120, right - miniWidth - (4 * gap));
        float feedTop = PlaceWorkers(gap, top, right - miniWidth - (3 * gap));
        Place(Messages, new Rect2(gap * 2, feedTop, Mathf.Min(feedWidth, 360), 0));
        PlacePlayers(right - gap, top + gap, miniWidth, miniHeight + gap);
        Place(Placement, LandscapePlacementBar(m, mapWidth, size.Y, panelWidth, panelHeight));
        Place(Banner, new Rect2(0, top + 2, mapWidth, 14));
        Place(notes, new Rect2(0, top, mapWidth, size.Y - top));
        Insets = new ScreenInsets(0, top, strip, Context.Visible ? panelHeight + gap : 0);
        FreeArea = new Rect2(0, top, mapWidth, size.Y - top);
    }

    /// <summary>
    /// Puts the head counts under the top bar's left end, in rows that fit in
    /// <paramref name="room"/> (left of the minimap); returns where the message list starts below them.
    /// </summary>
    private float PlaceWorkers(int gap, float top, float room)
    {
        Workers.FitWidth(room);
        Vector2 wanted = Workers.WantedSize;
        Place(Workers, new Rect2(gap, top + gap, wanted.X, wanted.Y));
        return Workers.Visible ? top + gap + wanted.Y + gap : top + gap;
    }

    /// <summary>
    /// Where the placing bar goes in landscape: centred at the bottom of the map, beside the
    /// selection panel when that is open, or above it when there is no room beside it.
    /// </summary>
    private Rect2 LandscapePlacementBar(HudMetrics m, float mapWidth, float height, float panelWidth, float panelHeight)
    {
        const float Widest = 340, Narrowest = 200;
        int gap = m.Gap;
        float wanted = Placement.WantedHeight;
        float left = Context.Visible ? panelWidth + (2 * gap) : gap;
        float room = mapWidth - left - gap;
        if (room >= Narrowest)
        {
            float width = Mathf.Min(room, Widest);
            float x = Mathf.Max(left, (mapWidth - width) / 2);
            return new Rect2(x, height - wanted - gap, width, wanted);
        }
        return new Rect2(gap, height - panelHeight - wanted - (2 * gap), Mathf.Min(panelWidth, Widest), wanted);
    }

    private void LayOutPortrait(HudMetrics m, Vector2 size)
    {
        int gap = m.Gap;
        float top = m.TopBar;
        float bar = m.Button + (2 * gap);
        Place(Top, new Rect2(0, 0, size.X, top));
        Place(Commands, new Rect2(gap, size.Y - bar + gap, size.X - (2 * gap), m.Button));
        float panelWidth = Context.Folded ? Mathf.Min(size.X - (2 * gap), Context.FoldedWidth) : size.X - (2 * gap);
        Context.CustomMinimumSize = new Vector2(panelWidth, 0);
        Context.Size = new Vector2(panelWidth, 0);
        float panelHeight = Mathf.Min(size.Y * 0.42f, Context.WantedHeight);
        Place(Context, new Rect2(gap, size.Y - bar - panelHeight, panelWidth, panelHeight));
        float miniWidth = Mathf.Clamp(size.X * 0.36f, 96, 170);
        float miniHeight = Mathf.Round(miniWidth * 0.75f);
        Place(minimapFrame, new Rect2(size.X - miniWidth - gap, top + gap, miniWidth, miniHeight));
        Place(Messages, new Rect2(gap * 2, PlaceWorkers(gap, top, size.X - miniWidth - (3 * gap)), size.X - miniWidth - (5 * gap), 0));
        PlacePlayers(size.X - gap, top + gap, miniWidth, miniHeight + gap);
        float above = bar + (Context.Visible ? panelHeight : 0);
        Place(Placement, new Rect2(gap, size.Y - above - Placement.WantedHeight - gap, size.X - (2 * gap), Placement.WantedHeight));
        Place(Banner, new Rect2(0, top + 2, size.X, 14));
        Place(notes, new Rect2(0, top, size.X, size.Y - top - bar));
        Insets = new ScreenInsets(0, top, 0, above);
        FreeArea = new Rect2(0, top, size.X, size.Y - top - bar);
    }

    /// <summary>
    /// The player list in the corner: right-aligned to <paramref name="right"/>, under the minimap
    /// (<paramref name="minimapHeight"/> with its gap, from <paramref name="top"/>) or in its place when it is hidden.
    /// </summary>
    private void PlacePlayers(float right, float top, float width, float minimapHeight)
    {
        float y = minimapFrame.Visible ? top + minimapHeight : top;
        Place(Players, new Rect2(right - width, y, width, 0));
    }

    private static void Place(Control control, Rect2 rect)
    {
        control.Position = rect.Position.Floor();
        control.Size = new Vector2(Mathf.Max(0, rect.Size.X), Mathf.Max(0, rect.Size.Y)).Floor();
    }

    private void CloseRawPopup()
    {
        if (Popup is { } open)
        {
            open.Close();
        }
        popup = null;
    }
}

/// <summary>The selection box being dragged, in the original's green with a black shadow.</summary>
public partial class BoxMark : Control
{
    private static readonly Color Line = new("#10dd64");

    /// <summary>The box in screen pixels, or null.</summary>
    public Rect2? Box { get; set; }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (Box is not { } box)
        {
            return;
        }
        Rect2 r = new(box.Position.Floor(), box.Size.Floor());
        DrawRect(new Rect2(r.Position + Vector2.One, r.Size), Colors.Black, filled: false, width: 1);
        DrawRect(r, Line, filled: false, width: 1);
    }
}
