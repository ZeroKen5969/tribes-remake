using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Graphics.Tests;

/// <summary>
/// Labels drawn by the original TextRenderer with its bitmap fonts, in all five original languages,
/// redrawn from the exported strings, fonts.json and the BMFont files: the pixels must be identical
/// (original/reference/reference-cases.txt.gz; built only when the original repository is in original/).
/// </summary>
public sealed partial class TextRenderingTests
{
    private static readonly string[] Numbers = { "0123456789", "-+0", "+17", "-5", "1000", "42" };
    private static readonly string[] Languages = { "en", "fr", "it", "de", "es" };
    private readonly ITestOutputHelper output;

    public TextRenderingTests(ITestOutputHelper output)
    {
        this.output = output;
    }

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
}
