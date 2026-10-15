using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;

namespace Tribes.Graphics.Tests;

/// <summary>One line of the reference file: what was drawn and the summary of the original's pixels.</summary>
internal sealed record ReferenceCase(string Kind, int[] Values, string[] Occupants, string Expected, string Line);

/// <summary>Reads the reference file and composes its cases with the library.</summary>
internal static class ReferenceCases
{
    public const int Width = 400;
    public const int Height = 400;
    public const int OriginX = 140;
    public const int OriginY = 220;
    public const int Background = 0xFF00FF;

    private static readonly Lazy<IReadOnlyList<ReferenceCase>> All = new(Load);

    public static IReadOnlyList<ReferenceCase> Cases => All.Value;

    private static IReadOnlyList<ReferenceCase> Load()
    {
        using var file = File.OpenRead(TestPaths.Reference("reference-cases.txt.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var cases = new List<ReferenceCase>();
        string? header = reader.ReadLine();
        if (header != $"# canvas {Width} {Height} {OriginX} {OriginY} background {Background:x6}")
        {
            throw new InvalidDataException("unexpected reference header: " + header);
        }
        for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
        {
            string[] halves = line.Split(" | ");
            string[] words = halves[0].Split(' ');
            var values = new List<int>();
            var occupants = new List<string>();
            foreach (string word in words.Skip(1))
            {
                if (word.Contains(':', StringComparison.Ordinal))
                {
                    occupants.Add(word);
                }
                else
                {
                    values.Add(int.Parse(word, CultureInfo.InvariantCulture));
                }
            }
            cases.Add(new ReferenceCase(words[0], values.ToArray(), occupants.ToArray(), halves[1], line));
        }
        return cases;
    }

    /// <summary>The library's parts for a case.</summary>
    public static IReadOnlyList<DrawPart> Compose(Composers c, ReferenceCase rc)
    {
        int[] v = rc.Values;
        return rc.Kind switch
        {
            "P" => c.Sprites.ComposePiece(v[0], v[1]).Parts,
            "S" => c.Sprites.ComposeSprite(v[0], v[1]).Parts,
            "E" => c.Sprites.ComposeEffect(v[0], v[1]).Parts,
            "V" => c.Bodies.Compose(Villager(v)).Drawing.Parts,
            "Q" => c.Bodies.Compose(new BodyRequest(BodySubject.Villager, v[0], v[1], v[2])
            {
                Appearance = Look(v, 3) with { InSelectedGroup = v[13] == 1, Hovered = v[14] == 1, InSelectionRectangle = v[15] == 1 },
                MapSelectionMarks = true,
                LungeFacing = v[1],
            }).Drawing.Parts,
            "A" => c.Bodies.Compose(new BodyRequest(BodySubject.Animal, v[0], v[1], v[2]) { LungeFacing = v[1] }).Drawing.Parts,
            "B" => c.Objects.Compose(Building(v, rc.Occupants)).Drawing.Parts,
            "N" => c.Objects.Compose(ObjectRequest.ResourceNode(v[0], v[1], v[2], Pass(v[3])) with
            {
                Tick = v[4], MapRow = v[5], SlotWorkMode = v[6], SlotBuildingKind = v[7],
            }).Drawing.Parts,
            "T" => c.Objects.Compose(ObjectRequest.TerrainObject(v[0], Pass(v[1])) with { Tick = v[2], MapRow = v[3] }).Drawing.Parts,
            "R" => c.Objects.Compose(Trap(v, rc.Occupants)).Drawing.Parts,
            "W" => c.Objects.Compose(ObjectRequest.Preview(v[0]) with { PlayerStock = Stock(v, 1) }).Drawing.Parts,
            "M" => c.Objects.Compose(MenuScene.Request(v[0] == 1 ? ObjectPass.Upper : ObjectPass.Preview, v[1]) with { MapRow = v[2] }).Drawing.Parts,
            _ => throw new InvalidDataException("unknown case kind " + rc.Kind),
        };
    }

    /// <summary>The player's stock: the 11 values from <paramref name="at"/>, the rest 0.</summary>
    private static int[] Stock(int[] v, int at)
    {
        int[] stock = new int[26];
        Array.Copy(v, at, stock, 0, 11);
        return stock;
    }

    private static ObjectPass Pass(int pass) => pass == 0 ? ObjectPass.Ground : ObjectPass.Upper;

    private static VillagerAppearance Look(IReadOnlyList<int> v, int at) => new()
    {
        Tribe = (Tribe)v[at],
        Age = v[at + 1],
        Sex = (Sex)v[at + 2],
        TorsoStyle = v[at + 3],
        HeadStyle = v[at + 4],
        IsLeader = v[at + 5] == 1,
        AssignedTask = v[at + 6],
        Weapon = v[at + 7],
        CarriedItem = v[at + 8],
        CarriedAmount = v[at + 9],
    };

    private static BodyRequest Villager(int[] v) => new(BodySubject.Villager, v[0], v[1], v[2])
    {
        Appearance = Look(v, 3),
        Portrait = v[13] == 1,
        InsideBuildingPose = v[14] == 1,
        PaletteVariant = v[15],
        LungeFacing = v[16],
    };

    private static Occupant ParseOccupant(string text)
    {
        int[] f = text.Split(':').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return new Occupant(f[0], f[1], f[2], f[3], f[4], Look(f, 5));
    }

    private static ObjectRequest Building(int[] v, string[] occupants)
    {
        int slot = v[2];
        int pass = v[3];
        int[] stock = Stock(v, 10);
        return ObjectRequest.Building(v[0], v[1], slot, Pass(pass)) with
        {
            CellIndex = pass == 0 ? v[4] : -1,
            Tick = v[5],
            MapRow = v[6],
            SlotWorkMode = v[7],
            OccupantsVisible = v[8] == 1,
            MissionThreeHeadquarters = v[9] == 1,
            PlayerStock = stock,
            Occupants = occupants.Select(ParseOccupant).ToArray(),
        };
    }

    /// <summary>Grid code to trap kind as <c>GameCanvas.a_IB</c> decodes it.</summary>
    private static int TrapKind(int code)
    {
        const int Ordered = -123;
        if (code == Ordered)
        {
            return 23;
        }
        int kind = 20 - (code + 111);
        while (kind > 22)
        {
            kind -= 3;
        }
        return kind;
    }

    private static ObjectRequest Trap(int[] v, string[] occupants)
    {
        int code = v[0];
        int pass = v[1];
        bool trapped = v[2] == 1;
        int kind = TrapKind(code);
        if (trapped)
        {
            return ObjectRequest.Trap(kind, 2, ObjectPass.Upper) with
            {
                Tick = v[3],
                MapRow = v[4],
                Occupants = new[] { ParseOccupant(occupants[0]) with { Group = 0 } },
            };
        }
        bool computer = code <= -117 && code >= -122;
        int stage = code == -123 ? 0 : 1;
        ObjectPass objectPass = computer ? ObjectPass.Upper : Pass(pass);
        return ObjectRequest.Trap(kind, stage, objectPass) with { Tick = v[3], MapRow = v[4] };
    }

    /// <summary>The library's pixels for a case, summarised like the reference.</summary>
    public static string Render(TestAssets assets, IReadOnlyList<DrawPart> parts)
    {
        var canvas = new RgbaImage(Width, Height);
        canvas.ClearOpaque(Background);
        assets.Rasterizer.Draw(canvas, parts, OriginX, OriginY);
        return Summary(canvas, OriginX, OriginY);
    }

    /// <summary>"x y width height crc" of the pixels that are not the background colour.</summary>
    public static string Summary(RgbaImage canvas, int originX, int originY)
    {
        ReadOnlySpan<int> pixels = canvas.Pixels;
        int width = canvas.Width;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if ((pixels[(y * width) + x] & 0xFFFFFF) != Background)
                {
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
            }
        }
        if (x1 < 0)
        {
            return "0 0 0 0 00000000";
        }
        uint crc = 0;
        byte[] line = new byte[(x1 - x0 + 1) * 3];
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int p = pixels[(y * width) + x];
                int i = (x - x0) * 3;
                line[i] = (byte)(p >> 16);
                line[i + 1] = (byte)(p >> 8);
                line[i + 2] = (byte)p;
            }
            crc = Crc32.Append(crc, line);
        }
        return string.Create(CultureInfo.InvariantCulture, $"{x0 - originX} {y0 - originY} {x1 - x0 + 1} {y1 - y0 + 1} {crc:x8}");
    }
}
