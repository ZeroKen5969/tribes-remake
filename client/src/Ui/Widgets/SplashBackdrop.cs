using System;
using Godot;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// The original title picture (l2.png, 240×321, portrait) filling a screen of any shape. The
/// picture stays whole and centred; around it its own background is drawn on in its palette:
/// the stepped sky (deepening upwards), the teal hills with their outline, the bush meadow, the
/// light grass, and the original clouds in the open sky. Repainted on every resize, at the
/// window's whole-number pixel scale.
/// </summary>
public partial class SplashBackdrop : Control
{
    private const string SplashPath = "res://assets/original/l2.png";
    private const string CloudSheetPath = "res://assets/original/pi0_16.png";
    private const int SplashWidth = 240;
    private const int SplashHeight = 321;

    // Colours sampled from the picture's edge columns.
    private static readonly Color[] SkyBands = [new("#a6deff"), new("#b4e8fe"), new("#d2f3fa"), new("#ecffff"), new("#fcfffe")];
    private static readonly Color[] HighSky = [new("#a6deff"), new("#9ad6fd"), new("#8ecefb"), new("#83c6f8")];
    private static readonly Color HillLight = new("#82bea9");
    private static readonly Color Outline = new("#309963");
    private static readonly Color TealLight = new("#8defed");
    private static readonly Color Teal = new("#87e7e3");
    private static readonly Color BushShade = new("#5f972e");
    private static readonly Color Bush = new("#7cc74d");
    private static readonly Color BushDark = new("#75be47");
    private static readonly Color GrassEdge = new("#a2cd3a");
    private static readonly Color GrassLight = new("#b1e242");
    private static readonly Color Grass = new("#c6f74c");
    private static readonly Color GrassDark = new("#beef46");

    // Cloud pieces of sheet pi0_16 (the title screen clouds, sprites.json pieces 1801..1806).
    private static readonly Rect2I[] Clouds = [new(0, 32, 44, 37), new(0, 0, 48, 32), new(44, 32, 31, 23), new(57, 56, 20, 16)];

    /// <summary>Where each layer of the picture starts at one of its side edges (picture rows).</summary>
    /// <param name="SkyBreaks">Rows where the sky changes to its next lighter step.</param>
    /// <param name="Hill">First row of the hill outline.</param>
    /// <param name="BushTop">Row of the bush meadow's outline.</param>
    /// <param name="GrassTop">First row of the grass edge.</param>
    private sealed record EdgeProfile(int[] SkyBreaks, int Hill, int BushTop, int GrassTop);

    private static readonly EdgeProfile LeftEdge = new([30, 45, 66, 75], 76, 161, 254);
    private static readonly EdgeProfile RightEdge = new([28, 40, 61, 77], 86, 147, 248);

    private Image splash = null!;
    private Image cloudSheet = null!;
    private Texture2D? painted;
    private Vector2I paintedFor = -Vector2I.One;
    private int scale = 1;

    /// <summary>The picture's rectangle in this control.</summary>
    public Rect2 SplashRect { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        splash = Load(SplashPath);
        cloudSheet = Load(CloudSheetPath);
        Resized += QueueRedraw;
    }

    private static Image Load(string path)
    {
        Image image = GD.Load<Texture2D>(path).GetImage();
        if (image.IsCompressed())
        {
            image.Decompress();
        }
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (Size.X < 1 || Size.Y < 1)
        {
            return;
        }
        Repaint();
        if (painted is not null)
        {
            DrawTextureRect(painted, new Rect2(Vector2.Zero, painted.GetSize() * scale), false);
        }
    }

    private void Repaint()
    {
        scale = Math.Max(1, (int)Math.Min(Size.X / SplashWidth, Size.Y / SplashHeight));
        var canvas = new Vector2I((int)Math.Ceiling(Size.X / scale), (int)Math.Ceiling(Size.Y / scale));
        if (canvas == paintedFor)
        {
            return;
        }
        paintedFor = canvas;
        int left = (canvas.X - SplashWidth) / 2;
        int top = Math.Max(0, (canvas.Y - SplashHeight) / 2);
        Image image = Paint(canvas, left, top);
        image.BlendRect(splash, new Rect2I(0, 0, SplashWidth, SplashHeight), new Vector2I(left, top));
        painted = ImageTexture.CreateFromImage(image);
        SplashRect = new Rect2(left * scale, top * scale, SplashWidth * scale, SplashHeight * scale);
    }

    /// <summary>The background around the picture placed at (<paramref name="left"/>, <paramref name="top"/>).</summary>
    private Image Paint(Vector2I canvas, int left, int top)
    {
        var pixels = new byte[canvas.X * canvas.Y * 4];
        for (int x = 0; x < canvas.X; x++)
        {
            bool beforePicture = x < left;
            bool afterPicture = x >= left + SplashWidth;
            EdgeProfile edge = afterPicture ? RightEdge : LeftEdge;
            int distance = beforePicture ? left - x : afterPicture ? x - (left + SplashWidth - 1) : 0;
            int side = afterPicture ? 1 : 0;
            // The picture's meadow and grass alternate a lighter and a darker column, lighter on
            // its outermost columns; the pattern carries on outwards from there.
            bool light = distance == 0 ? (x - left) % 2 == 0 : distance % 2 == 0;
            for (int y = 0; y < canvas.Y; y++)
            {
                Put(pixels, canvas.X, x, y, Layer(edge, distance, side, x, y - top, light));
            }
        }
        Image image = Image.CreateFromData(canvas.X, canvas.Y, false, Image.Format.Rgba8, pixels);
        PaintClouds(image, canvas, left, top);
        return image;
    }

    /// <summary>
    /// The colour at picture row <paramref name="row"/>, <paramref name="distance"/> columns out from
    /// the picture's side edge (0 above and below the picture). Layer edges wander away from the
    /// picture's own edge rows so the land rolls on.
    /// </summary>
    private static Color Layer(EdgeProfile edge, int distance, int side, int x, int row, bool light)
    {
        if (distance == 0 && row >= SplashHeight)
        {
            return GrassFill(x, row, light);
        }
        float rolling = Wave(distance, 140, 7, side * 1.7f) + Wave(distance, 53, 3, side * 0.6f + 1);
        int hill = edge.Hill + (int)MathF.Round(rolling);
        int bush = edge.BushTop + (int)MathF.Round(Wave(distance, 37, 4, side * 2.3f) - Crowns(distance, side));
        int grass = edge.GrassTop + (int)MathF.Round(Wave(distance, 90, 3, side * 0.9f + 2));
        if (distance == 0 || row < hill)
        {
            return SkyAt(edge, x, row, distance);
        }
        if (row == hill)
        {
            return HillLight;
        }
        if (row == hill + 1)
        {
            return Outline;
        }
        if (row == hill + 2 || row == bush - 1)
        {
            return TealLight;
        }
        if (row < bush)
        {
            return Teal;
        }
        if (row == bush)
        {
            return Outline;
        }
        if (row == bush + 1)
        {
            return BushShade;
        }
        if (row < grass)
        {
            return light ? Bush : BushDark;
        }
        if (row == grass)
        {
            return GrassEdge;
        }
        if (row == grass + 1)
        {
            return GrassLight;
        }
        return GrassFill(x, row, light);
    }

    /// <summary>The picture's sky steps, deepening above the picture; step edges dithered like the original's.</summary>
    private static Color SkyAt(EdgeProfile edge, int x, int row, int distance)
    {
        if (row < 0)
        {
            int step = Math.Min(HighSky.Length - 1, (-row + Jitter(x, 3)) / 70);
            return HighSky[step];
        }
        int band = 0;
        for (int i = 0; i < edge.SkyBreaks.Length; i++)
        {
            int at = edge.SkyBreaks[i] + (distance == 0 ? 0 : (int)MathF.Round(Wave(distance, 61 + 17 * i, 2, i)) + Jitter(x + i * 31, 3) - 1);
            if (row >= at)
            {
                band = i + 1;
            }
        }
        return SkyBands[band];
    }

    /// <summary>
    /// Light grass with the picture's darker streaks and a few tufts. The picture's last rows are
    /// the darker shade; just below it the lighter one comes back through a dither.
    /// </summary>
    private static Color GrassFill(int x, int row, bool light)
    {
        int below = row - SplashHeight;
        if (below >= 0 && below < 6 && (x + row) % (below / 2 + 2) != 0)
        {
            return GrassDark;
        }
        uint hash = Hash(x / 3, row / 5);
        if (hash % 23 == 0 && row % 5 < 2 && (below < 0 || below > 8))
        {
            return Bush;
        }
        return light ? Grass : GrassDark;
    }

    /// <summary>Rounded bush tops: a bump every so often, none at the picture's edge.</summary>
    private static float Crowns(int distance, int side)
    {
        const int width = 18;
        int index = (distance + side * 7) / width;
        float within = (distance + side * 7) % width / (float)width;
        float height = 3 + Hash(index, side) % 6;
        float bump = MathF.Sin(within * MathF.PI) * height;
        float fadeIn = Math.Clamp(distance / 12f, 0, 1);
        return bump * fadeIn;
    }

    /// <summary>A wave that is 0 at the picture's edge (distance 0).</summary>
    private static float Wave(int distance, float period, float amplitude, float phase) =>
        amplitude * (MathF.Sin(distance * MathF.Tau / period + phase) - MathF.Sin(phase));

    private static int Jitter(int x, int range) => (int)(Hash(x, 7) % (uint)range);

    private static uint Hash(int a, int b)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        return h ^ (h >> 15);
    }

    private static void Put(byte[] pixels, int width, int x, int y, Color colour)
    {
        int i = (y * width + x) * 4;
        pixels[i] = (byte)(colour.R * 255);
        pixels[i + 1] = (byte)(colour.G * 255);
        pixels[i + 2] = (byte)(colour.B * 255);
        pixels[i + 3] = 255;
    }

    /// <summary>The original clouds in the open sky beside and above the picture (never over it).</summary>
    private void PaintClouds(Image image, Vector2I canvas, int left, int top)
    {
        var picture = new Rect2I(left - 4, top - 4, SplashWidth + 8, SplashHeight + 8);
        int skyBottom = top + Math.Min(LeftEdge.Hill, RightEdge.Hill) - 6;
        var random = new Random(5);
        int count = Math.Clamp(canvas.X * Math.Max(0, skyBottom) / 6000, 0, 30);
        for (int i = 0; i < count * 4 && count > 0; i++)
        {
            Rect2I piece = Clouds[random.Next(Clouds.Length)];
            var at = new Vector2I(random.Next(-20, canvas.X), random.Next(-8, Math.Max(-7, skyBottom - piece.Size.Y)));
            var place = new Rect2I(at, piece.Size);
            if (place.Intersects(picture) || at.Y + piece.Size.Y > skyBottom)
            {
                continue;
            }
            image.BlendRect(cloudSheet, piece, at);
            if (--count == 0)
            {
                break;
            }
        }
    }
}
