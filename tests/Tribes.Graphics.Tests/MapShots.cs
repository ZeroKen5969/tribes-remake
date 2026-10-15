using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tribes.Graphics.Imaging;

namespace Tribes.Graphics.Tests;

/// <summary>One captured map frame (tools/asset-ref MapShot): the grid and state when the screenshot was taken.</summary>
internal sealed class MapShot
{
    public const int MarginX = 11;
    public const int MarginY = 8;

    private MapShot(string directory, string frame)
    {
        Name = Path.GetFileName(directory) + "/" + frame;
        Screen = Png.Decode(File.ReadAllBytes(Path.Combine(directory, $"screen-{frame}.png")));
        Terrain = Png.Decode(File.ReadAllBytes(Path.Combine(directory, $"terrain-{frame}.png")));
        Ground = Png.Decode(File.ReadAllBytes(Path.Combine(directory, $"ground-{frame}.png")));
        var tiles = new List<byte[]>();
        var codes = new List<sbyte[]>();
        var fog = new Dictionary<int, int[]>();
        foreach (string line in File.ReadAllLines(Path.Combine(directory, $"map-{frame}.txt")))
        {
            string[] w = line.Split(' ');
            switch (w[0])
            {
                case "size": (Width, Height) = (Int(w[1]), Int(w[2])); break;
                case "camera": (CameraX, CameraY) = (Int(w[1]), Int(w[2])); break;
                case "view": (Columns, Rows) = (Int(w[1]), Int(w[2])); break;
                case "scroll": (ScrollX, ScrollY) = (Int(w[1]), Int(w[2])); break;
                case "bars": (TopBar, BarSlide) = (Int(w[1]), Int(w[2])); break;
                case "cursor": (CursorX, CursorY) = (Int(w[1]), Int(w[2])); break;
                case "panels": Panels = w.Skip(1).Select(v => v == "1").ToArray(); break;
                case "tiles": tiles.Add(Convert.FromHexString(w[2])); break;
                case "codes": codes.Add(Convert.FromHexString(w[2]).Select(b => unchecked((sbyte)b)).ToArray()); break;
                case "fog": fog[Int(w[1])] = w.Skip(2).Select(Int).ToArray(); break;
                case "state": State = Convert.FromHexString(w[1]).Select(b => unchecked((sbyte)b)).ToArray(); break;
                default: break;
            }
        }
        Tiles = tiles;
        Codes = codes;
        FogRows = fog;
    }

    public string Name { get; }

    public RgbaImage Screen { get; }

    /// <summary>The game's o_() redraw with the object codes cleared (264 x 304 map buffer).</summary>
    public RgbaImage Terrain { get; }

    /// <summary>The game's o_() redraw with the objects (their ground parts).</summary>
    public RgbaImage Ground { get; }

    public int Width { get; }

    public int Height { get; }

    public int CameraX { get; }

    public int CameraY { get; }

    public int Columns { get; }

    public int Rows { get; }

    public int ScrollX { get; }

    public int ScrollY { get; }

    public int TopBar { get; }

    public int BarSlide { get; }

    public int CursorX { get; }

    public int CursorY { get; }

    public bool[] Panels { get; } = Array.Empty<bool>();

    public IReadOnlyList<byte[]> Tiles { get; }

    public IReadOnlyList<sbyte[]> Codes { get; }

    public IReadOnlyDictionary<int, int[]> FogRows { get; }

    public sbyte[] State { get; } = Array.Empty<sbyte>();

    public int TileAt(int x, int y) => Tiles[y][x];

    public int CodeAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Codes[y][x] : -126;

    /// <summary>Fog state (0 explored, 1 half, 2 unexplored) of a map cell near the view.</summary>
    public int FogAt(int x, int y) =>
        FogRows.TryGetValue(y, out int[]? row) && x - CameraX + 1 >= 0 && x - CameraX + 1 < row.Length ? row[x - CameraX + 1] : 2;

    public static IReadOnlyList<MapShot> All()
    {
        var shots = new List<MapShot>();
        foreach (string directory in Directory.GetDirectories(TestPaths.Reference("maps")).OrderBy(d => d, StringComparer.Ordinal))
        {
            foreach (string file in Directory.GetFiles(directory, "map-*.txt").OrderBy(f => f, StringComparer.Ordinal))
            {
                string frame = Path.GetFileNameWithoutExtension(file)["map-".Length..];
                shots.Add(new MapShot(directory, frame));
            }
        }
        return shots;
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
