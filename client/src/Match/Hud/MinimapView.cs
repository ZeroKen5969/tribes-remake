using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.World;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The minimap in the original's colours: one pixel per cell by terrain class (darker where the
/// tribe has seen the land but does not see it now, earth brown where it has never been), the
/// tribe's own units yellow and buildings orange, other tribes' in red (single player) or their
/// tribe colour; the part of the map on screen is framed. Tapping or dragging moves the view. An
/// endless map's minimap shows the explored land instead (see MinimapView.Endless.cs).
/// </summary>
public partial class MinimapView : Control
{
    private const int RefreshTicks = 4;

    private static readonly Color Unexplored = Rgb(MinimapPainter.Unexplored);
    private static readonly Color[] Terrain = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(MinimapPainter.Terrain, Rgb));
    private static readonly Color[] TerrainDim = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(MinimapPainter.TerrainDim, Rgb));
    private static readonly Color OwnUnit = Rgb(MinimapPainter.OwnUnit), OwnBuilding = Rgb(MinimapPainter.OwnBuilding);
    private static readonly Color EnemyUnit = new("#db0100"), EnemyBuilding = new("#710500"), EnemyBuildingDim = Rgb(MinimapPainter.OtherBuildingDim);

    private readonly IMatchActions actions;
    private Image? image;
    private byte[] pixels = System.Array.Empty<byte>();
    private int pictureWidth;
    private ImageTexture? texture;
    private long drawnTick = -100;
    private bool dragging;

    /// <summary>A minimap of the match <paramref name="actions"/> plays.</summary>
    public MinimapView(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The map rectangle on screen, in map pixels (set by the screen each frame).</summary>
    public Rect2 ViewArea { get; set; }

    /// <summary>A map point was tapped or dragged to (map pixels).</summary>
    public event Action<Vector2>? Pointed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "Minimap";
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    /// <summary>Redraws the map picture when the rules have moved on enough since the last one.</summary>
    public void Refresh(long tick, bool force = false)
    {
        if (MapArea.IsChunked(actions.World.Map))
        {
            RefreshEndless(tick, force);
            return;
        }
        endless = null;
        if (!force && tick - drawnTick < RefreshTicks && tick >= drawnTick)
        {
            QueueRedraw();
            return;
        }
        drawnTick = tick;
        Paint();
        QueueRedraw();
    }

    private void Paint()
    {
        GameWorld world = actions.World;
        GridSize size = world.Map.PlayableSize;
        if (image is null || image.GetWidth() != size.Width || image.GetHeight() != size.Height)
        {
            image = Image.CreateEmpty(size.Width, size.Height, false, Image.Format.Rgba8);
            pixels = new byte[size.Width * size.Height * 4];
            pictureWidth = size.Width;
            texture = null;
        }
        ViewerSight sight = actions.Sight;
        IReadOnlyList<int> classes = world.Content.Rules.Interface.MinimapColors;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                var cell = new GridPoint(x, y);
                Color colour = Unexplored;
                if (sight.IsExplored(cell))
                {
                    int tile = world.Map.TileAt(cell);
                    int terrain = tile < classes.Count ? Math.Clamp(classes[tile], 0, Terrain.Length - 1) : 0;
                    colour = sight.IsVisible(cell) ? Terrain[terrain] : TerrainDim[terrain];
                }
                Put(x, y, colour);
            }
        }
        PaintBuildings(world, sight);
        PaintUnits(world, sight);
        // One upload of the whole picture: setting pixels one by one is slow on phones.
        image.SetData(size.Width, size.Height, false, Image.Format.Rgba8, pixels);
        if (texture is null)
        {
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            texture.Update(image);
        }
    }

    private void PaintBuildings(GameWorld world, ViewerSight sight)
    {
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            Building b = world.Building(id);
            if (!b.Stands || b.Kind < 0 || b.Kind >= world.Content.Rules.Buildings.Kinds.Count)
            {
                continue;
            }
            var kind = world.Content.Rules.Buildings.Kinds[b.Kind];
            bool own = sight.IsOwn(b.Tribe);
            if (!own && !sight.ShowsAsItIs(b))
            {
                // Out of sight in a match: the remembered look stands in for it (below).
                continue;
            }
            for (int dy = 0; dy < kind.Height; dy++)
            {
                for (int dx = 0; dx < kind.Width; dx++)
                {
                    var cell = new GridPoint(b.X + dx, b.Y + dy);
                    if (!world.Map.InPlay(cell) || (!own && !sight.IsExplored(cell)))
                    {
                        continue;
                    }
                    Color colour = own ? OwnBuilding : !sight.IsVisible(cell) ? EnemyBuildingDim : OtherColour(b.Tribe, building: true);
                    Put(cell.X, cell.Y, colour);
                }
            }
        }
        if (sight.RemembersBuildings)
        {
            PaintRemembered(world, sight);
        }
    }

    /// <summary>The other tribes' buildings out of sight as the viewer last saw them (a match), darkened.</summary>
    private void PaintRemembered(GameWorld world, ViewerSight sight)
    {
        foreach (RememberedBuilding seen in sight.Buildings.Buildings)
        {
            GridRect cells = seen.Footprint(world.Content);
            if (seen.HitPoints <= 0 || sight.IsOwn(seen.Tribe) || sight.AnyVisible(cells))
            {
                continue;
            }
            for (int y = cells.Top; y < cells.Bottom; y++)
            {
                for (int x = cells.Left; x < cells.Right; x++)
                {
                    var cell = new GridPoint(x, y);
                    if (world.Map.InPlay(cell) && sight.IsExplored(cell))
                    {
                        Put(x, y, EnemyBuildingDim);
                    }
                }
            }
        }
    }

    private void PaintUnits(GameWorld world, ViewerSight sight)
    {
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit u = world.Unit(id);
            var cell = new GridPoint(u.X, u.Y);
            if (u.HitPoints <= 0 || !world.Map.InPlay(cell) || Logic.BuildingOffers.BuildingInside(world, u) != 0)
            {
                continue;
            }
            bool own = sight.IsOwn(u.Tribe);
            // Hidden units show only to the tribes that know of them (their own and allied eyes).
            if (!own && (!sight.IsVisible(cell) || ((u.Status & UnitStatus.Hidden) != 0 && !sight.Includes(u.Tribe))))
            {
                continue;
            }
            Put(cell.X, cell.Y, own ? OwnUnit : OtherColour(u.Tribe, building: false));
        }
    }

    private Color OtherColour(int tribe, bool building)
    {
        if (!actions.Runner.IsNetwork)
        {
            return building ? EnemyBuilding : EnemyUnit;
        }
        Color swatch = new(WorldView.Colours.Colours[Math.Clamp(actions.ColourOf(tribe), 0, 7)].Swatch);
        return building ? swatch.Darkened(0.4f) : swatch;
    }

    private void Put(int x, int y, Color colour)
    {
        int i = ((y * pictureWidth) + x) * 4;
        pixels[i] = (byte)colour.R8;
        pixels[i + 1] = (byte)colour.G8;
        pixels[i + 2] = (byte)colour.B8;
        pixels[i + 3] = 255;
    }

    /// <summary>Where the map picture is drawn inside the control (whole map, aspect kept).</summary>
    private Rect2 MapRect()
    {
        if (image is null)
        {
            return new Rect2(Vector2.Zero, Size);
        }
        var map = new Vector2(image.GetWidth(), image.GetHeight());
        float scale = Mathf.Min((Size.X - 2) / map.X, (Size.Y - 2) / map.Y);
        if (scale >= 1)
        {
            scale = Mathf.Floor(scale);
        }
        Vector2 drawn = map * scale;
        return new Rect2(((Size - drawn) / 2).Floor(), drawn);
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Dark);
        if (endless is not null)
        {
            DrawEndless();
            return;
        }
        if (texture is null || image is null)
        {
            return;
        }
        Rect2 rect = MapRect();
        DrawTextureRect(texture, rect, tile: false);
        DrawRect(rect.Grow(1), UiPalette.Ink, filled: false, width: 1);
        // The part on screen, in map pixels → minimap pixels.
        var perPixel = new Vector2(rect.Size.X / (image.GetWidth() * WorldView.CellWidth), rect.Size.Y / (image.GetHeight() * WorldView.CellHeight));
        var frame = new Rect2(rect.Position + (ViewArea.Position * perPixel), ViewArea.Size * perPixel).Intersection(rect.Grow(1));
        if (frame.Size.X > 0 && frame.Size.Y > 0)
        {
            DrawRect(frame, UiPalette.Paper, filled: false, width: 1);
        }
    }

    /// <inheritdoc/>
    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                dragging = button.Pressed;
                if (button.Pressed)
                {
                    Point(button.Position);
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when dragging:
                Point(motion.Position);
                AcceptEvent();
                break;
            case InputEventScreenTouch touch:
                dragging = touch.Pressed;
                if (touch.Pressed)
                {
                    Point(touch.Position);
                }
                AcceptEvent();
                break;
            case InputEventScreenDrag drag:
                Point(drag.Position);
                AcceptEvent();
                break;
        }
    }

    private void Point(Vector2 local)
    {
        if (endless is not null)
        {
            PointEndless(local);
            return;
        }
        if (image is null)
        {
            return;
        }
        Rect2 rect = MapRect();
        Vector2 relative = (local - rect.Position) / rect.Size;
        relative = relative.Clamp(Vector2.Zero, Vector2.One);
        Pointed?.Invoke(new Vector2(relative.X * image.GetWidth() * WorldView.CellWidth, relative.Y * image.GetHeight() * WorldView.CellHeight));
    }
}
