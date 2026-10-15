using System;

namespace Tribes.Client.World.Scene;

/// <summary>
/// Where an endless map's minimap shows what: the cells in the picture (the explored land, or on a
/// very large one the part around the camera), the picture's size in pixels and where it sits in
/// the minimap. A picture pixel shows one cell, scaled up by a whole number when there is room, or
/// one cell out of each <see cref="CellsPerPixel"/> x <see cref="CellsPerPixel"/> when the land is
/// larger than the minimap, so the cost of a picture depends on the minimap's size, not the map's.
/// </summary>
public readonly record struct MinimapLayout
{
    /// <summary>At most this many cells per picture pixel along each side; more land than that shows the part around the camera.</summary>
    public const int MaxCellsPerPixel = 2;

    /// <summary>Space left around the picture inside the minimap (its frame), in pixels per side.</summary>
    public const int Border = 1;

    /// <summary>The cells the picture shows.</summary>
    public CellRect Window { get; init; }

    /// <summary>Cells per picture pixel along each side (1 while the window fits the minimap).</summary>
    public int CellsPerPixel { get; init; }

    /// <summary>Minimap pixels per picture pixel (a whole number, 1 when cells are skipped).</summary>
    public int Scale { get; init; }

    /// <summary>Picture width in pixels.</summary>
    public int PictureWidth { get; init; }

    /// <summary>Picture height in pixels.</summary>
    public int PictureHeight { get; init; }

    /// <summary>Left of the picture in the minimap.</summary>
    public int DrawX { get; init; }

    /// <summary>Top of the picture in the minimap.</summary>
    public int DrawY { get; init; }

    /// <summary>Width of the picture as drawn.</summary>
    public int DrawWidth => PictureWidth * Scale;

    /// <summary>Height of the picture as drawn.</summary>
    public int DrawHeight => PictureHeight * Scale;

    /// <summary>Minimap pixels per cell.</summary>
    public double PixelsPerCell => (double)Scale / CellsPerPixel;

    /// <summary>
    /// The layout for land <paramref name="area"/> in a minimap of <paramref name="width"/> x
    /// <paramref name="height"/> pixels with the camera over cell (<paramref name="centreX"/>,
    /// <paramref name="centreY"/>).
    /// </summary>
    public static MinimapLayout Fit(CellRect area, int width, int height, int centreX, int centreY)
    {
        int room = 2 * Border;
        int roomX = Math.Max(1, width - room), roomY = Math.Max(1, height - room);
        if (area.IsEmpty)
        {
            area = new CellRect(centreX, centreY, 1, 1);
        }
        int windowWidth = Math.Min(area.Width, roomX * MaxCellsPerPixel);
        int windowHeight = Math.Min(area.Height, roomY * MaxCellsPerPixel);
        int left = Math.Clamp(centreX - (windowWidth / 2), area.Left, area.Right - windowWidth);
        int top = Math.Clamp(centreY - (windowHeight / 2), area.Top, area.Bottom - windowHeight);
        var window = new CellRect(left, top, windowWidth, windowHeight);
        int scale = Math.Min(roomX / windowWidth, roomY / windowHeight);
        int cellsPerPixel = 1;
        if (scale < 1)
        {
            scale = 1;
            cellsPerPixel = Math.Max(Ceiling(windowWidth, roomX), Ceiling(windowHeight, roomY));
        }
        int pictureWidth = Ceiling(windowWidth, cellsPerPixel), pictureHeight = Ceiling(windowHeight, cellsPerPixel);
        return new MinimapLayout
        {
            Window = window,
            CellsPerPixel = cellsPerPixel,
            Scale = scale,
            PictureWidth = pictureWidth,
            PictureHeight = pictureHeight,
            DrawX = (width - (pictureWidth * scale)) / 2,
            DrawY = (height - (pictureHeight * scale)) / 2,
        };
    }

    /// <summary>The cell picture pixel (<paramref name="px"/>, <paramref name="py"/>) shows (the middle one of its block).</summary>
    public (int X, int Y) CellOfPixel(int px, int py) =>
        (Math.Min(Window.Left + (px * CellsPerPixel) + (CellsPerPixel / 2), Window.Right - 1),
            Math.Min(Window.Top + (py * CellsPerPixel) + (CellsPerPixel / 2), Window.Bottom - 1));

    /// <summary>The picture pixel showing cell (<paramref name="x"/>, <paramref name="y"/>), or null outside the window.</summary>
    public (int X, int Y)? PixelOfCell(int x, int y)
    {
        if (!Window.Contains(x, y))
        {
            return null;
        }
        return ((x - Window.Left) / CellsPerPixel, (y - Window.Top) / CellsPerPixel);
    }

    /// <summary>The cell under minimap point (<paramref name="localX"/>, <paramref name="localY"/>), kept inside the window.</summary>
    public (int X, int Y) CellAt(double localX, double localY)
    {
        int x = Window.Left + (int)Math.Floor((localX - DrawX) / PixelsPerCell);
        int y = Window.Top + (int)Math.Floor((localY - DrawY) / PixelsPerCell);
        return (Math.Clamp(x, Window.Left, Window.Right - 1), Math.Clamp(y, Window.Top, Window.Bottom - 1));
    }

    /// <summary>The minimap point of the top-left corner of cell (<paramref name="x"/>, <paramref name="y"/>) (fractions of cells allowed).</summary>
    public (double X, double Y) PointOf(double x, double y) =>
        (DrawX + ((x - Window.Left) * PixelsPerCell), DrawY + ((y - Window.Top) * PixelsPerCell));

    private static int Ceiling(int value, int divisor) => (value + divisor - 1) / divisor;
}
