using Godot;
using Tribes.Client.Input;
using Tribes.Core;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Client.Map;

/// <summary>
/// Shows a mission's start map and lets the player look around it: the base of the in-game view.
/// Cells are drawn as flat colours by what fills them until the original tile pictures are wired in.
/// </summary>
public partial class MapScreen : Node2D
{
    /// <summary>Width of a map cell in pixels.</summary>
    public const int CellWidth = 22;

    /// <summary>Height of a map cell in pixels.</summary>
    public const int CellHeight = 16;

    /// <summary>The mission whose map is shown.</summary>
    [Export] public int Mission { get; set; } = 1;

    private WorldMap? map;
    private MapCamera camera = null!;
    private Label status = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        camera = GetNode<MapCamera>("Camera");
        status = GetNode<Label>("Hud/Status");
        var gestures = GetNode<PointerGestures>("Gestures");
        gestures.Panned += camera.PanBy;
        gestures.Zoomed += camera.ZoomBy;
        gestures.Tapped += OnTapped;
        GetViewport().SizeChanged += camera.KeepInBounds;

        GameContent content = App.GameApp.Instance.Content;
        map = MapBuilder.Build(Mission, content, new GameRandom(1));
        MissionDefinition mission = content.Missions[Mission];
        camera.Bounds = new Rect2(0, 0, map.PlayableSize.Width * CellWidth, map.PlayableSize.Height * CellHeight);
        camera.Zoom = new Vector2(2, 2);
        camera.CentreOn(new Vector2((mission.StartCenter.X + 0.5f) * CellWidth, (mission.StartCenter.Y + 0.5f) * CellHeight));
        GD.Print($"MAP_READY mission={Mission} size={map.Size.Width}x{map.Size.Height}");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        QueueRedraw();
        Vector2 size = GetViewportRect().Size;
        status.Text = $"zoom {camera.Zoom.X:0.00}  view {size.X:0}x{size.Y:0}  {Engine.GetFramesPerSecond():0} fps";
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (map == null)
        {
            return;
        }
        Rect2 view = VisibleWorldRect();
        int x0 = Mathf.Max(0, (int)(view.Position.X / CellWidth));
        int y0 = Mathf.Max(0, (int)(view.Position.Y / CellHeight));
        int x1 = Mathf.Min(map.PlayableSize.Width - 1, (int)(view.End.X / CellWidth));
        int y1 = Mathf.Min(map.PlayableSize.Height - 1, (int)(view.End.Y / CellHeight));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var cell = new GridPoint(x, y);
                DrawRect(new Rect2(x * CellWidth, y * CellHeight, CellWidth, CellHeight), CellColour(map.CellAt(cell), map.TileAt(cell)));
            }
        }
    }

    private Rect2 VisibleWorldRect()
    {
        Vector2 topLeft = camera.ScreenToWorld(Vector2.Zero);
        Vector2 bottomRight = camera.ScreenToWorld(GetViewportRect().Size);
        return new Rect2(topLeft, bottomRight - topLeft);
    }

    private void OnTapped(Vector2 position)
    {
        if (map == null)
        {
            return;
        }
        Vector2 world = camera.ScreenToWorld(position);
        var cell = new GridPoint((int)(world.X / CellWidth), (int)(world.Y / CellHeight));
        if (map.InPlay(cell))
        {
            GD.Print($"TAP cell={cell.X},{cell.Y} content={map.CellAt(cell).Content} tile={map.TileAt(cell)}");
        }
    }

    private static Color CellColour(MapCell cell, int tile) => cell.Content switch
    {
        CellContent.Impassable => new Color(0.18f, 0.2f, 0.16f),
        CellContent.Rocks => new Color(0.5f, 0.5f, 0.48f),
        CellContent.Thicket => new Color(0.1f, 0.35f, 0.12f),
        CellContent.Shore => new Color(0.3f, 0.45f, 0.7f),
        CellContent.ResourceNode => new Color(0.85f, 0.7f, 0.2f),
        _ => new Color(0.35f + (tile % 7 * 0.02f), 0.55f, 0.25f),
    };
}
