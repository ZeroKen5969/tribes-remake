using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Graphics.Tests;

/// <summary>A BMFont text file and its page, as Godot would read them.</summary>
internal sealed class BmFont
{
    public BmFont(string fntPath)
    {
        string directory = Path.GetDirectoryName(fntPath)!;
        foreach (string line in File.ReadAllLines(fntPath))
        {
            Dictionary<string, string> fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(f => f.Split('=', 2)).ToDictionary(f => f[0], f => f[1].Trim('"'), StringComparer.Ordinal);
            if (line.StartsWith("page ", StringComparison.Ordinal))
            {
                Page = Png.Decode(File.ReadAllBytes(Path.Combine(directory, fields["file"])));
            }
            else if (line.StartsWith("char ", StringComparison.Ordinal))
            {
                int Get(string name) => int.Parse(fields[name], CultureInfo.InvariantCulture);
                Chars[Get("id")] = new BmChar(Get("x"), Get("y"), Get("width"), Get("height"), Get("xoffset"), Get("yoffset"), Get("xadvance"));
            }
            else if (line.StartsWith("common ", StringComparison.Ordinal))
            {
                LineHeight = int.Parse(fields["lineHeight"], CultureInfo.InvariantCulture);
            }
        }
        if (Page == null)
        {
            throw new InvalidDataException(fntPath + " has no page");
        }
    }

    public RgbaImage? Page { get; }

    public int LineHeight { get; }

    public Dictionary<int, BmChar> Chars { get; } = new();

    /// <summary>Draws one character with its pen at (x, y); returns the pen's advance.</summary>
    public int Draw(RgbaImage target, int codePoint, int x, int y)
    {
        BmChar c = Chars[codePoint];
        for (int dy = 0; dy < c.Height; dy++)
        {
            for (int dx = 0; dx < c.Width; dx++)
            {
                int pixel = Page![c.X + dx, c.Y + dy];
                int tx = x + c.XOffset + dx;
                int ty = y + c.YOffset + dy;
                if ((pixel >>> 24) == 255 && target.Contains(tx, ty))
                {
                    target[tx, ty] = pixel;
                }
            }
        }
        return c.Advance;
    }
}

internal readonly record struct BmChar(int X, int Y, int Width, int Height, int XOffset, int YOffset, int Advance);

/// <summary>
/// The bitmap fonts of the original TextRenderer as exported (fonts.json and the BMFont files); the
/// comparison with the labels the original draws is in Original/TextRenderingTests.Original.cs.
/// </summary>
public sealed partial class TextRenderingTests
{
    private static BitmapFontCatalog Fonts { get; } = CatalogJson.Read<BitmapFontCatalog>(TestPaths.Data("fonts.json"));

    [Fact]
    public void SmallFontColourVersionsAreTheSheetBands()
    {
        string[] colours = { "yellow", "red", "cyan" };
        RgbaImage sheet = TestAssets.Instance.Sheets["pi0_26"];
        for (int band = 0; band < colours.Length; band++)
        {
            var font = new BmFont(TestPaths.Assets("fonts", $"original-small-{colours[band]}.fnt"));
            BitmapFont model = Fonts.Fonts[1];
            foreach (char c in "0123456789-+")
            {
                Glyph glyph = model.Glyphs.First(g => g.Code == Fonts.GlyphByByte[c]);
                BmChar ch = font.Chars[c];
                Assert.Equal(glyph.Width, ch.Width);
                for (int y = 0; y < ch.Height; y++)
                {
                    for (int x = 0; x < ch.Width; x++)
                    {
                        int original = sheet[glyph.X + x, (band * model.Rows[0].Height) + y];
                        int exported = font.Page![ch.X + x, ch.Y + y];
                        Assert.Equal(original >>> 24 == 0 ? 0 : original, exported >>> 24 == 0 ? 0 : exported);
                    }
                }
            }
        }
    }
}
