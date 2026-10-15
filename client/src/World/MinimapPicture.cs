using System;
using Godot;
using Tribes.Client.World.Scene;
using Tribes.Core.World;

namespace Tribes.Client.World;

/// <summary>
/// The picture of an endless map's minimap (see <see cref="MinimapLayout"/> and
/// <see cref="MinimapPainter"/>): the one shown and the next one, painted a band of rows at a time
/// so the cost per frame depends on the minimap's size only. Any control can show it
/// (<see cref="Draw"/>) and turn a tap on it into a map point (<see cref="MapPointAt"/>).
/// </summary>
public sealed class MinimapPicture
{
    private readonly MinimapPainter painter;
    private GameWorld? world;
    private ViewerSight? sight;
    private MinimapLayout painting;
    private byte[] pixels = Array.Empty<byte>();
    private int nextRow;
    private Image? image;

    /// <summary>A picture giving other tribes the colours <paramref name="colourOf"/> picks for them (0..7).</summary>
    public MinimapPicture(Func<int, int> colourOf)
    {
        ArgumentNullException.ThrowIfNull(colourOf);
        painter = new MinimapPainter(tribe => Swatch(colourOf(tribe)));
    }

    /// <summary>A picture is being painted.</summary>
    public bool Painting { get; private set; }

    /// <summary>The layout of the picture shown, or null before the first one.</summary>
    public MinimapLayout? Shown { get; private set; }

    /// <summary>The picture shown.</summary>
    public ImageTexture? Texture { get; private set; }

    /// <summary>The swatch of tribe colour <paramref name="colour"/> as 0xRRGGBB.</summary>
    public static uint Swatch(int colour)
    {
        if (swatches == null)
        {
            var colours = WorldView.Colours.Colours;
            swatches = new uint[colours.Count];
            for (int i = 0; i < colours.Count; i++)
            {
                var swatch = new Color(colours[i].Swatch);
                swatches[i] = ((uint)swatch.R8 << 16) | ((uint)swatch.G8 << 8) | (uint)swatch.B8;
            }
        }
        return swatches[Math.Clamp(colour, 0, swatches.Length - 1)];
    }

    private static uint[]? swatches;

    /// <summary>
    /// Starts the next picture of <paramref name="from"/> as the player of <paramref name="tribe"/>
    /// sees it (see <see cref="Start(GameWorld, ViewerSight, Vector2, Rect2)"/>).
    /// </summary>
    public void Start(GameWorld from, int tribe, Vector2 size, Rect2 viewArea)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (sight == null || sight.World != from || sight.Viewpoint != Viewpoint.Player(tribe))
        {
            sight = ViewerSight.Of(from, tribe);
        }
        sight.Refresh();
        Start(from, sight, size, viewArea);
    }

    /// <summary>
    /// Starts the next picture of <paramref name="from"/> as <paramref name="viewer"/> sees it (a
    /// world view's <see cref="WorldView.Sight"/>), for a minimap of <paramref name="size"/> with
    /// <paramref name="viewArea"/> (map pixels) on screen.
    /// </summary>
    public void Start(GameWorld from, ViewerSight viewer, Vector2 size, Rect2 viewArea)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(viewer);
        world = from;
        sight = viewer;
        Vector2 centre = viewArea.GetCenter();
        painting = MinimapLayout.Fit(MapArea.CameraArea(from, viewer), Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y),
            Mathf.FloorToInt(centre.X / WorldView.CellWidth), Mathf.FloorToInt(centre.Y / WorldView.CellHeight));
        int bytes = painting.PictureWidth * painting.PictureHeight * 4;
        if (pixels.Length != bytes)
        {
            pixels = new byte[bytes];
        }
        nextRow = 0;
        Painting = true;
    }

    /// <summary>Paints the whole picture now (the first one, a forced refresh).</summary>
    public void PaintAll(Func<Unit, bool> isInside)
    {
        while (Painting)
        {
            PaintBand(1, isInside);
        }
    }

    /// <summary>
    /// Paints the next of <paramref name="bands"/> bands of rows; after the last one the buildings
    /// and units go on (units <paramref name="isInside"/> a building are left out) and the picture
    /// is shown. True when it was shown.
    /// </summary>
    public bool PaintBand(int bands, Func<Unit, bool> isInside)
    {
        ArgumentNullException.ThrowIfNull(isInside);
        if (!Painting || world == null)
        {
            return false;
        }
        int rows = Math.Max(1, (painting.PictureHeight + bands - 1) / bands);
        painter.PaintLand(world, sight!, painting, pixels, nextRow, nextRow + rows);
        nextRow += rows;
        if (nextRow < painting.PictureHeight)
        {
            return false;
        }
        painter.PaintMarkers(world, sight!, painting, pixels, isInside);
        Show();
        return true;
    }

    /// <summary>Draws the picture shown on <paramref name="canvas"/> with its frame and the part on screen framed.</summary>
    public void Draw(CanvasItem canvas, Rect2 viewArea, Color frameColour, Color viewColour)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (Texture is not { } texture || Shown is not { } layout)
        {
            return;
        }
        var rect = new Rect2(layout.DrawX, layout.DrawY, layout.DrawWidth, layout.DrawHeight);
        canvas.DrawTextureRect(texture, rect, tile: false);
        canvas.DrawRect(rect.Grow(1), frameColour, filled: false, width: 1);
        (double x0, double y0) = layout.PointOf(viewArea.Position.X / WorldView.CellWidth, viewArea.Position.Y / WorldView.CellHeight);
        (double x1, double y1) = layout.PointOf(viewArea.End.X / WorldView.CellWidth, viewArea.End.Y / WorldView.CellHeight);
        var frame = new Rect2((float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0)).Intersection(rect.Grow(1));
        if (frame.Size.X > 0 && frame.Size.Y > 0)
        {
            canvas.DrawRect(frame, viewColour, filled: false, width: 1);
        }
    }

    /// <summary>The middle of the cell under minimap point <paramref name="local"/>, in map pixels; null before the first picture.</summary>
    public Vector2? MapPointAt(Vector2 local)
    {
        if (Shown is not { } layout)
        {
            return null;
        }
        (int x, int y) = layout.CellAt(local.X, local.Y);
        return new Vector2((x + 0.5f) * WorldView.CellWidth, (y + 0.5f) * WorldView.CellHeight);
    }

    private void Show()
    {
        int width = painting.PictureWidth, height = painting.PictureHeight;
        if (image is null || Texture is null || image.GetWidth() != width || image.GetHeight() != height)
        {
            image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
            Texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            image.SetData(width, height, false, Image.Format.Rgba8, pixels);
            Texture.Update(image);
        }
        Shown = painting;
        Painting = false;
    }
}
