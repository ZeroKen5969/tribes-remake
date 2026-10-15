using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NVorbis;
using Tribes.AssetExport.Export;
using Tribes.AssetExport.Original;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Graphics.Tests;

/// <summary>The exported files against the original resources.</summary>
public sealed class ExportedAssetTests
{
    private readonly ITestOutputHelper output;

    public ExportedAssetTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static GameResources Resources { get; } = GameResources.Load(Path.Combine(TestPaths.RepoRoot(), "original", "game", "res"));

    [Theory]
    [InlineData("sprites.json", typeof(SpriteCatalog))]
    [InlineData("terrain-tiles.json", typeof(TerrainTileCatalog))]
    [InlineData("ui.json", typeof(UiCatalog))]
    [InlineData("fonts.json", typeof(BitmapFontCatalog))]
    [InlineData("music.json", typeof(MusicCatalog))]
    [InlineData("strings/en.json", typeof(StringTables))]
    [InlineData("strings/fr.json", typeof(StringTables))]
    [InlineData("strings/it.json", typeof(StringTables))]
    [InlineData("strings/de.json", typeof(StringTables))]
    [InlineData("strings/es.json", typeof(StringTables))]
    [InlineData("strings/zh-TW.json", typeof(StringTables))]
    public void JsonRoundTripsUnchanged(string file, Type type)
    {
        string text = File.ReadAllText(TestPaths.Data(file.Split('/')));
        object value = JsonSerializer.Deserialize(text, type, CatalogJson.Options)!;
        Assert.Equal(text, JsonSerializer.Serialize(value, type, CatalogJson.Options) + "\n");
    }

    [Fact]
    public void ExportedCatalogEqualsAFreshBuild()
    {
        SpriteCatalog fresh = SpriteCatalogBuilder.Build(Resources);
        Assert.Equal(File.ReadAllText(TestPaths.Data("sprites.json")), CatalogJson.Serialize(fresh));
    }

    [Fact]
    public void ImagesKeepTheOriginalPixels()
    {
        var packs = new Dictionary<SheetPack, IReadOnlyList<byte[]>>
        {
            [SheetPack.Sprites] = Resources.SpritePack,
            [SheetPack.Variants] = Resources.VariantPack,
            [SheetPack.Illustrations] = Resources.IllustrationPack,
        };
        UiCatalog ui = CatalogJson.Read<UiCatalog>(TestPaths.Data("ui.json"));
        IEnumerable<SheetInfo> sheets = TestAssets.Instance.Catalog.Sheets.Concat(ui.Images);
        int count = 0;
        foreach (SheetInfo sheet in sheets)
        {
            byte[] exported = File.ReadAllBytes(TestPaths.Assets(sheet.File));
            byte[] original = packs.TryGetValue(sheet.Pack, out IReadOnlyList<byte[]>? pack)
                ? pack[sheet.Index]
                : Resources.ReadFile(sheet.Pack == SheetPack.Icon ? "icon.png" : sheet.Id);
            Assert.DoesNotContain(Png.ReadChunks(exported), c => c.Type is "gAMA" or "tEXt");
            Assert.Equal(Png.Decode(original).ToArray(), Png.Decode(exported).ToArray());
            (int width, int height) = Png.ReadSize(exported);
            Assert.Equal((sheet.Width, sheet.Height), (width, height));
            count++;
        }
        Assert.Equal(27 + 6 + 12 + 4 + 1, count);
    }

    [Fact]
    public void MidiFilesAreTheOriginalSongs()
    {
        for (int i = 0; i < Resources.Songs.Count; i++)
        {
            byte[] midi = File.ReadAllBytes(TestPaths.Assets("music", "midi", $"song-{i}.mid"));
            Assert.Equal(Resources.Songs[i], midi);
            Assert.Equal("MThd", System.Text.Encoding.ASCII.GetString(midi, 0, 4));
        }
    }

    [Fact]
    public void OggRenderingsDecodeAndHaveSound()
    {
        MusicCatalog music = CatalogJson.Read<MusicCatalog>(TestPaths.Data("music.json"));
        Assert.Equal(5, music.Songs.Count);
        foreach (Song song in music.Songs)
        {
            using var reader = new VorbisReader(TestPaths.Assets(song.Ogg.Split('/')));
            Assert.Equal(2, reader.Channels);
            Assert.Equal(44100, reader.SampleRate);
            Assert.InRange(reader.TotalTime.TotalSeconds, song.Seconds - 0.05, song.Seconds + 0.05);
            float[] buffer = new float[44100 * 2];
            double sum = 0;
            long samples = 0;
            int read;
            while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    sum += buffer[i] * buffer[i];
                }
                samples += read;
            }
            double rms = Math.Sqrt(sum / samples);
            output.WriteLine($"song {song.Id}: {song.Seconds:0.0} s, RMS {rms:0.000}");
            Assert.InRange(rms, 0.01, 0.7);
        }
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, music.Moods.SelectMany(m => m.Songs).Distinct().OrderBy(s => s));
    }

    [Fact]
    public void TerrainFlagsAgreeWithTheRulesData()
    {
        TerrainTileCatalog tiles = CatalogJson.Read<TerrainTileCatalog>(TestPaths.Data("terrain-tiles.json"));
        using JsonDocument rules = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.RepoRoot(), "data", "terrain.json")));
        foreach (JsonElement tile in rules.RootElement.GetProperty("tiles").EnumerateArray())
        {
            TerrainTile t = tiles.Tiles[tile.GetProperty("id").GetInt32()];
            Assert.Equal(tile.GetProperty("blocksMovement").GetBoolean(), t.BlocksMovement);
            Assert.Equal(tile.GetProperty("isRock").GetBoolean(), t.IsRock);
        }
        Assert.Equal(256, tiles.Tiles.Count);
        Assert.All(tiles.Tiles, t => Assert.Equal(t.Id < 175 ? TileDrawing.Sprite : TileDrawing.ObjectGraphic, t.Drawing));
        Assert.Equal(236 + 6, tiles.Tiles.Count(t => t.UsedInMaps));
        Assert.All(tiles.Tiles.Where(t => t.Overlay.Count > 0 || t.OverlayAnimations.Count > 0),
            t => Assert.True(Composition.TerrainComposer.HasOverlay(t.Id)));
    }

    [Fact]
    public void StringTablesHaveEveryEntryInEveryLanguage()
    {
        string[] languages = { "en", "fr", "it", "de", "es", "zh-TW" };
        StringTables[] tables = languages.Select(l => CatalogJson.Read<StringTables>(TestPaths.Data("strings", l + ".json"))).ToArray();
        foreach (StringTables table in tables)
        {
            Assert.Equal(228, table.S.Count);
            Assert.Equal(71, table.T.Count);
            Assert.Equal(235, table.D.Count);
            Assert.Equal(StringTableBuilder.FirstStoryIndex, table.D[0].Id);
            Assert.Equal(Enumerable.Range(0, 228), table.S.Select(s => s.Id));
        }
        Assert.Equal("sportstätte", tables[3].S[1].Text);
        Assert.Equal("sportsta^tte", tables[3].S[1].Raw);
        Assert.Equal("cabaña", tables[4].S[2].Text);
        Assert.Equal("réserve", tables[1].S[6].Text);
        Assert.Equal("軍械庫", tables[5].S[0].Text);
        Assert.Contains(tables[0].D, s => s.Text.Contains("{name}", StringComparison.Ordinal) && s.Text.Contains('\n', StringComparison.Ordinal));
    }

    [Fact]
    public void ControlMarkupMatchesTheTranslationSources()
    {
        // The zh-TW tables are built from game/i18n with the same {N} markup as the English originals.
        StringTables en = CatalogJson.Read<StringTables>(TestPaths.Data("strings", "en.json"));
        StringTables zh = CatalogJson.Read<StringTables>(TestPaths.Data("strings", "zh-TW.json"));
        IEnumerable<(GameString En, GameString Zh)> pairs = en.S.Zip(zh.S).Concat(en.T.Zip(zh.T)).Concat(en.D.Zip(zh.D));
        foreach ((GameString e, GameString z) in pairs)
        {
            Assert.Equal(Controls(e.Raw), Controls(z.Raw));
        }
    }

    private static string Controls(string raw) => string.Concat(
        System.Text.RegularExpressions.Regex.Matches(raw, @"\{(\d+|name)\}").Select(m => m.Value));

    [Fact]
    public void PixelFontIsTheVerifiedReleaseAndCoversEveryString()
    {
        string font = TestPaths.Assets("fonts", "cubic-11", CjkFont.FontFile);
        Assert.Equal(CjkFont.FontSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(font))).ToLowerInvariant());
        Assert.True(File.Exists(TestPaths.Assets("fonts", "cubic-11", CjkFont.LicenseFile)));
        ISet<int> covered = TrueTypeCmap.Characters(File.ReadAllBytes(font));
        var missing = new SortedSet<int>();
        foreach (string language in new[] { "en", "fr", "it", "de", "es", "zh-TW" })
        {
            StringTables table = CatalogJson.Read<StringTables>(TestPaths.Data("strings", language + ".json"));
            foreach (GameString s in table.S.Concat(table.T).Concat(table.D))
            {
                string text = System.Text.RegularExpressions.Regex.Replace(s.Text, @"\{(\d+|name)\}", string.Empty);
                foreach (char c in text.Where(c => c != '\n'))
                {
                    if (!covered.Contains(c))
                    {
                        missing.Add(c);
                    }
                }
            }
        }
        output.WriteLine($"{covered.Count} characters in the font; missing: {string.Join(" ", missing.Select(c => ((char)c).ToString()))}");
        Assert.Empty(missing);
    }

    [Fact]
    public void BmFontsListEveryPrintableCharacter()
    {
        var large = new BmFont(TestPaths.Assets("fonts", "original-large.fnt"));
        foreach (char c in "ABCXYZabcxyz0123456789.,:;!?'-+/@ ÉéÀàÇçÄäÖöÜüÑñÂâ¡¿ß©")
        {
            Assert.True(large.Chars.ContainsKey(c), $"'{c}' missing");
        }
        Assert.Equal(17, large.LineHeight);
    }
}

/// <summary>Reads the characters a TrueType font maps (cmap formats 4 and 12).</summary>
internal static class TrueTypeCmap
{
    public static ISet<int> Characters(byte[] font)
    {
        int U16(int at) => (font[at] << 8) | font[at + 1];
        int I16(int at) => (short)U16(at);
        long U32(int at) => ((long)U16(at) << 16) | (uint)U16(at + 2);
        int tables = U16(4);
        int cmap = -1;
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + (16 * i);
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == "cmap")
            {
                cmap = (int)U32(record + 8);
            }
        }
        var chars = new HashSet<int>();
        int subtables = U16(cmap + 2);
        for (int i = 0; i < subtables; i++)
        {
            int at = cmap + (int)U32(cmap + 4 + (8 * i) + 4);
            int format = U16(at);
            if (format == 4)
            {
                int segments = U16(at + 6) / 2;
                int ends = at + 14;
                int starts = ends + (2 * segments) + 2;
                int deltas = starts + (2 * segments);
                int offsets = deltas + (2 * segments);
                for (int s = 0; s < segments; s++)
                {
                    for (int c = U16(starts + (2 * s)); c <= U16(ends + (2 * s)) && c != 0xFFFF; c++)
                    {
                        int rangeOffset = U16(offsets + (2 * s));
                        int glyph = rangeOffset == 0
                            ? (c + I16(deltas + (2 * s))) & 0xFFFF
                            : U16(offsets + (2 * s) + rangeOffset + (2 * (c - U16(starts + (2 * s)))));
                        if (glyph != 0)
                        {
                            chars.Add(c);
                        }
                    }
                }
            }
            else if (format == 12)
            {
                long groups = U32(at + 12);
                for (int g = 0; g < groups; g++)
                {
                    int group = at + 16 + (12 * g);
                    for (long c = U32(group); c <= U32(group + 4); c++)
                    {
                        chars.Add((int)c);
                    }
                }
            }
        }
        return chars;
    }
}
