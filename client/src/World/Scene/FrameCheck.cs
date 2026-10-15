using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Tribes.Core.World;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World.Scene;

/// <summary>The result of comparing a picture of the world view with the original's frame.</summary>
/// <param name="Compared">Map-area pixels compared (the interface the original drew over the map is left out).</param>
/// <param name="Same">Pixels equal to the original's.</param>
/// <param name="TerrainHistory">
/// Differing pixels the original's terrain buffer kept from earlier pictures: in a cell DiffHarness
/// found stale or on the edge of the original's view (its edge cells hold parts of cells outside
/// the view drawn while scrolling), and not covered by anything of the object layer.
/// </param>
/// <param name="Diff">Equal pixels dimmed, different ones red (yellow for terrain history), skipped ones blue.</param>
public sealed record FrameCheckResult(long Compared, long Same, long TerrainHistory, RgbaImage Diff)
{
    /// <summary>Differing pixels that are not terrain history.</summary>
    public long Unexplained => Compared - Same - TerrainHistory;
}

/// <summary>
/// Draws dev snapshots with the scene code (no game engine), and compares pictures with the
/// original's frames: the map area below the top bar, without the rectangles the sidecar lists as
/// the original's interface.
/// </summary>
public static class FrameCheck
{
    private const int CellWidth = 22, CellHeight = 16;
    private const int Red = unchecked((int)0xFFFF0000), Yellow = unchecked((int)0xFFFFFF00);

    /// <summary>The original's screen of a snapshot as the scene code draws it (magenta where nothing is drawn).</summary>
    public static RgbaImage DrawScreen(GameWorld world, SnapshotView view, SceneAssets assets, Func<string, RgbaImage> sheets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(view);
        var screen = new RgbaImage(view.ScreenWidth, view.ScreenHeight);
        screen.ClearOpaque(0xFF00FF);
        var rasterizer = new PartRasterizer(sheets);
        ViewRect rect = view.View;
        new TerrainLayer(assets).Draw(world, rasterizer, screen, 0, view.MapTop, rect.Column, rect.Row,
            rect.Column + rect.Columns, rect.Row + rect.Rows);
        var list = new SceneList();
        new ObjectLayer(assets).Compose(new SceneRequest(world, rect, view.Presentation(), view.Marks,
            new TribeLooks(0, world.Layout.Tribes), true), list);
        foreach (SceneItem item in list.Items)
        {
            rasterizer.Draw(screen, item.Part, 0, view.MapTop);
        }
        return screen;
    }

    /// <summary>
    /// The screen pixels the object layer of a snapshot draws on (anything of it, opaque): a
    /// difference there cannot come from the terrain buffer.
    /// </summary>
    public static bool[,] ObjectCoverage(GameWorld world, SnapshotView view, SceneAssets assets, Func<string, RgbaImage> sheets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(view);
        var layer = new RgbaImage(view.ScreenWidth, view.ScreenHeight);
        var rasterizer = new PartRasterizer(sheets);
        var list = new SceneList();
        new ObjectLayer(assets).Compose(new SceneRequest(world, view.View, view.Presentation(), view.Marks,
            new TribeLooks(0, world.Layout.Tribes), true), list);
        foreach (SceneItem item in list.Items)
        {
            rasterizer.Draw(layer, item.Part, 0, view.MapTop);
        }
        var covered = new bool[layer.Width, layer.Height];
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                covered[x, y] = (layer[x, y] >>> 24) != 0;
            }
        }
        return covered;
    }

    /// <summary>
    /// Compares <paramref name="actual"/> with the original's <paramref name="expected"/> frame;
    /// <paramref name="covered"/> (from <see cref="ObjectCoverage"/>) tells which differences may be terrain history.
    /// </summary>
    public static FrameCheckResult Measure(SnapshotView view, RgbaImage expected, RgbaImage actual,
        IReadOnlyList<(int Row, int Column)> staleCells, bool[,] covered)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(staleCells);
        ArgumentNullException.ThrowIfNull(covered);
        int width = Math.Min(view.ScreenWidth, expected.Width);
        int height = Math.Min(view.ScreenHeight, expected.Height);
        var skip = new bool[width, height];
        foreach (int[] r in view.Exclude)
        {
            for (int y = Math.Max(0, r[1]); y < Math.Min(height, r[1] + r[3]); y++)
            {
                for (int x = Math.Max(0, r[0]); x < Math.Min(width, r[0] + r[2]); x++)
                {
                    skip[x, y] = true;
                }
            }
        }
        var diff = new RgbaImage(width, height);
        long compared = 0, same = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int e = expected[x, y] & 0xFFFFFF;
                if (skip[x, y] || y < view.MapTop)
                {
                    diff[x, y] = unchecked((int)0xFF000000) | ((e >> 2) & 0x3F3F3F) | 0x000080;
                    continue;
                }
                compared++;
                int a = x < actual.Width && y < actual.Height ? actual[x, y] & 0xFFFFFF : -1;
                if (a == e)
                {
                    same++;
                    diff[x, y] = unchecked((int)0xFF000000) | ((e >> 1) & 0x7F7F7F);
                }
                else
                {
                    diff[x, y] = Red;
                }
            }
        }
        var historyCells = new HashSet<(int Row, int Column)>(staleCells);
        for (int column = 0; column < view.Columns; column++)
        {
            historyCells.Add((0, column));
            historyCells.Add((view.Rows - 1, column));
        }
        for (int row = 0; row < view.Rows; row++)
        {
            historyCells.Add((row, 0));
            historyCells.Add((row, view.Columns - 1));
        }
        long history = 0;
        foreach ((int row, int column) in historyCells)
        {
            for (int y = Math.Max(0, view.MapTop + (row * CellHeight)); y < view.MapTop + ((row + 1) * CellHeight) && y < height; y++)
            {
                for (int x = Math.Max(0, column * CellWidth); x < (column + 1) * CellWidth && x < width; x++)
                {
                    bool underObject = x < covered.GetLength(0) && y < covered.GetLength(1) && covered[x, y];
                    if (diff[x, y] == Red && !underObject)
                    {
                        diff[x, y] = Yellow;
                        history++;
                    }
                }
            }
        }
        return new FrameCheckResult(compared, same, history, diff);
    }

    /// <summary>
    /// The view cells (row, column) whose terrain the original's buffer held stale at
    /// <paramref name="frame"/>, from DiffHarness --check-terrain reports (the last one up to the frame).
    /// </summary>
    public static IReadOnlyList<(int Row, int Column)> StaleCells(string terrainReport, int frame)
    {
        var cells = new List<(int, int)>();
        if (!File.Exists(terrainReport))
        {
            return cells;
        }
        string? last = null;
        foreach (string line in File.ReadLines(terrainReport))
        {
            string[] w = line.Split(' ');
            if (w.Length > 2 && int.TryParse(w[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int at) && at <= frame)
            {
                last = line;
            }
        }
        if (last != null)
        {
            foreach (Match m in Regex.Matches(last, @"\((\d+),(\d+)\)"))
            {
                cells.Add((int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
            }
        }
        return cells;
    }
}
