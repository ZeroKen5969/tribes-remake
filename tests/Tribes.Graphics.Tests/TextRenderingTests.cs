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
/// Labels drawn by the original TextRenderer with its bitmap fonts, in all five original languages,
/// redrawn from the exported strings, fonts.json and the BMFont files: the pixels must be identical.
/// </summary>
public sealed class TextRenderingTests
{
    private static readonly string[] Numbers = { "0123456789", "-+0", "+17", "-5", "1000", "42" };
    private static readonly string[] Languages = { "en", "fr", "it", "de", "es" };
    private readonly ITestOutputHelper output;

    public TextRenderingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static BitmapFontCatalog Fonts { get; } = CatalogJson.Read<BitmapFontCatalog>(TestPaths.Data("fonts.json"));

    /// <summary>Draws stored text (the raw spelling) the way the original lays out a label.</summary>
    private static void DrawRaw(RgbaImage canvas, BmFont font, BitmapFont model, string raw, int x, int y)
    {
        var composed = Fonts.Spellings.Where(s => s.Bytes.Length == 2).ToDictionary(s => s.Bytes, s => s.Text, StringComparer.Ordinal);
        var substitutes = Fonts.Spellings.Where(s => s.Bytes.Length == 1).ToDictionary(s => s.Bytes[0], s => s.Text);
        int pen = x;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (Fonts.GlyphByByte[c] == Fonts.NarrowBackspaceCode)
            {
                pen -= model.Spacing + 2;
                continue;
            }
            string text = c.ToString();
            if (i + 1 < raw.Length && composed.TryGetValue(raw.Substring(i, 2), out string? accented))
            {
                text = accented;
                i++;
            }
            else if (substitutes.TryGetValue(c, out string? substitute))
            {
                text = substitute;
            }
            pen += font.Draw(canvas, char.ConvertToUtf32(text, 0), pen, y);
        }
    }

    [Fact]
    public void LabelsInEveryLanguageMatchTheOriginal()
    {
        var font = new BmFont(TestPaths.Assets("fonts", "original-large.fnt"));
        var tables = Languages.Select(l => CatalogJson.Read<StringTables>(TestPaths.Data("strings", l + ".json"))).ToArray();
        List<ReferenceCase> cases = ReferenceCases.Cases.Where(c => c.Kind == "X").ToList();
        Assert.True(cases.Count > 1000);
        foreach (ReferenceCase rc in cases)
        {
            string raw = tables[rc.Values[0]].S[rc.Values[1]].Raw;
            var canvas = new RgbaImage(ReferenceCases.Width, ReferenceCases.Height);
            canvas.ClearOpaque(ReferenceCases.Background);
            DrawRaw(canvas, font, Fonts.Fonts[0], raw, ReferenceCases.OriginX, ReferenceCases.OriginY);
            Assert.True(ReferenceCases.Summary(canvas, ReferenceCases.OriginX, ReferenceCases.OriginY) == rc.Expected,
                $"{Languages[rc.Values[0]]} s{rc.Values[1]} \"{raw}\" differs");
        }
        output.WriteLine($"{cases.Count} labels in 5 languages: identical");
    }

    [Fact]
    public void NumbersMatchTheOriginal()
    {
        var large = new BmFont(TestPaths.Assets("fonts", "original-large.fnt"));
        var small = new BmFont(TestPaths.Assets("fonts", "original-small-yellow.fnt"));
        foreach (ReferenceCase rc in ReferenceCases.Cases.Where(c => c.Kind == "Y"))
        {
            int fontId = rc.Values[0];
            var canvas = new RgbaImage(ReferenceCases.Width, ReferenceCases.Height);
            canvas.ClearOpaque(ReferenceCases.Background);
            DrawRaw(canvas, fontId == 0 ? large : small, Fonts.Fonts[fontId], Numbers[rc.Values[1]], ReferenceCases.OriginX, ReferenceCases.OriginY);
            Assert.Equal(rc.Expected, ReferenceCases.Summary(canvas, ReferenceCases.OriginX, ReferenceCases.OriginY));
        }
    }

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
