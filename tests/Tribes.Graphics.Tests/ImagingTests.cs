using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Xunit;

namespace Tribes.Graphics.Tests;

public sealed class ImagingTests
{
    private const int Red = unchecked((int)0xFFFF0000);
    private const int Blue = unchecked((int)0xFF0000FF);

    [Fact]
    public void Crc32MatchesTheStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(Crc32.Compute(Encoding.ASCII.GetBytes("123456789")),
            Crc32.Append(Crc32.Compute(Encoding.ASCII.GetBytes("1234")), Encoding.ASCII.GetBytes("56789")));
    }

    [Fact]
    public void EncodeThenDecodeKeepsEveryPixel()
    {
        var image = new RgbaImage(7, 5);
        var random = new Random(3);
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 7; x++)
            {
                image[x, y] = random.Next();
            }
        }
        RgbaImage decoded = Png.Decode(Png.Encode(image));
        Assert.Equal(image.ToArray(), decoded.ToArray());
        Assert.Equal((7, 5), Png.ReadSize(Png.Encode(image)));
    }

    /// <summary>A PNG with the given header fields, palette, transparency and raw (filter byte + row) data.</summary>
    private static byte[] Build(int width, int height, byte depth, byte colorType, byte[] raw, byte[]? palette = null,
        byte[]? transparency = null, byte interlace = 0)
    {
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = colorType;
        header[12] = interlace;
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(raw);
        }
        var chunks = new System.Collections.Generic.List<PngChunk> { new("IHDR", header) };
        if (palette != null)
        {
            chunks.Add(new PngChunk("PLTE", palette));
        }
        if (transparency != null)
        {
            chunks.Add(new PngChunk("tRNS", transparency));
        }
        chunks.Add(new PngChunk("gAMA", new byte[] { 0, 0, 0xAF, 0xC8 }));
        chunks.Add(new PngChunk("IDAT", compressed.ToArray()));
        chunks.Add(new PngChunk("IEND", Array.Empty<byte>()));
        return Png.WriteChunks(chunks);
    }

    [Fact]
    public void DecodesPaletteImagesWithTransparencyAtLowDepths()
    {
        byte[] palette = { 255, 0, 0, 0, 0, 255 };
        byte[] png = Build(4, 1, 2, 3, new byte[] { 0, 0b00_01_01_00 }, palette, new byte[] { 0, 255 });
        RgbaImage image = Png.Decode(png);
        Assert.Equal(new[] { 0x00FF0000, Blue, Blue, 0x00FF0000 }, image.ToArray());
    }

    [Fact]
    public void DecodesGreyRgbAndGreyAlphaWithEveryFilter()
    {
        RgbaImage grey = Png.Decode(Build(8, 1, 1, 0, new byte[] { 0, 0b1010_0000 }));
        Assert.Equal(unchecked((int)0xFFFFFFFF), grey[0, 0]);
        Assert.Equal(unchecked((int)0xFF000000), grey[1, 0]);

        // Two RGB rows: Sub then Paeth filters.
        byte[] rgb = { 1, 10, 20, 30, 5, 5, 5, 4, 1, 1, 1, 2, 2, 2 };
        RgbaImage colour = Png.Decode(Build(2, 2, 8, 2, rgb));
        Assert.Equal(unchecked((int)0xFF0A141E), colour[0, 0]);
        Assert.Equal(unchecked((int)0xFF0F1923), colour[1, 0]);

        // Grey-alpha: Up then Average.
        byte[] ga = { 2, 100, 200, 3, 10, 0 };
        RgbaImage greyAlpha = Png.Decode(Build(1, 2, 8, 4, ga));
        Assert.Equal(unchecked((int)0xC8646464), greyAlpha[0, 0]);
        Assert.Equal(unchecked((int)0x643C3C3C), greyAlpha[0, 1]);
    }

    [Fact]
    public void RejectsBrokenAndUnsupportedFiles()
    {
        Assert.Throws<InvalidDataException>(() => Png.ReadChunks(new byte[] { 1, 2, 3 }));
        byte[] good = Png.Encode(new RgbaImage(1, 1));
        byte[] badCrc = (byte[])good.Clone();
        badCrc[^5] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Png.ReadChunks(badCrc));
        Assert.Throws<NotSupportedException>(() => Png.Decode(Build(1, 1, 8, 2, new byte[] { 0, 1, 2, 3 }, interlace: 1)));
        Assert.Throws<NotSupportedException>(() => Png.Decode(Build(1, 1, 16, 2, new byte[7])));
        Assert.Throws<InvalidDataException>(() => Png.Decode(Build(1, 1, 8, 2, new byte[] { 9, 1, 2, 3 })));
        Assert.Throws<InvalidDataException>(() => Png.Decode(Build(2, 2, 8, 2, new byte[3])));
        Assert.Throws<InvalidDataException>(() => Png.Decode(Build(1, 1, 8, 3, new byte[] { 0, 5 }, new byte[] { 0, 0, 0 })));
        Assert.Throws<InvalidDataException>(() => Png.ReadSize(Png.WriteChunks(new[] { new PngChunk("IEND", Array.Empty<byte>()) })));
        Assert.Throws<InvalidDataException>(() => Png.Decode(Png.WriteChunks(new[] { new PngChunk("IEND", Array.Empty<byte>()) })));
        byte[] truncated = good[..20];
        Assert.Throws<InvalidDataException>(() => Png.ReadChunks(truncated.Concat(new byte[] { 0, 0, 0, 0 }).ToArray()));
    }

    [Fact]
    public void KeepPixelChunksDropsGammaButKeepsTransparency()
    {
        byte[] png = Build(1, 1, 8, 3, new byte[] { 0, 0 }, new byte[] { 1, 2, 3 }, new byte[] { 0 });
        byte[] clean = Png.KeepPixelChunks(png);
        Assert.Equal(new[] { "IHDR", "PLTE", "tRNS", "IDAT", "IEND" }, Png.ReadChunks(clean).Select(c => c.Type));
        Assert.Equal(Png.Decode(png).ToArray(), Png.Decode(clean).ToArray());
    }

    [Fact]
    public void RgbaImageChecksItsBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(0, 3));
        var image = new RgbaImage(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => image[2, 0]);
        Assert.False(image.Contains(-1, 0));
        image.ClearOpaque(0x123456);
        Assert.All(image.ToArray(), p => Assert.Equal(unchecked((int)0xFF123456), p));
    }

    [Fact]
    public void PrimitivesFollowTheMidpRules()
    {
        var image = new RgbaImage(10, 10);
        PartRasterizer.FillRect(image, 8, 8, 5, 5, Red);
        Assert.Equal(4, image.ToArray().Count(p => p == Red));
        PartRasterizer.FillRect(image, 0, 0, 0, 5, Blue);
        PartRasterizer.Outline(image, 0, 0, -1, 3, Blue);
        Assert.DoesNotContain(Blue, image.ToArray());
        PartRasterizer.Outline(image, 0, 0, 3, 2, Blue);
        Assert.Equal(10, image.ToArray().Count(p => p == Blue));
        var line = new RgbaImage(10, 10);
        PartRasterizer.Outline(line, 1, 1, 0, 4, Red);
        Assert.Equal(5, line.ToArray().Count(p => p == Red));
        PartRasterizer.Line(line, 0, 0, 3, 0, Blue);
        PartRasterizer.Line(line, 9, 9, 6, 6, Blue);
        Assert.Equal(4 + 4, line.ToArray().Count(p => p == Blue));
        Assert.Equal(Blue, line[7, 7]);
    }

    [Fact]
    public void ImagesBlendFlipAndClip()
    {
        var sheet = new RgbaImage(2, 1);
        sheet[0, 0] = Red;
        sheet[1, 0] = unchecked((int)0x800000FF);
        var rasterizer = new PartRasterizer(_ => sheet);
        var target = new RgbaImage(3, 1);
        target.ClearOpaque(0);
        rasterizer.Draw(target, new[] { DrawPart.Image("s", 0, 0, 2, 1, -1, 0, 1) }, 1, 0);
        Assert.Equal(Red, target[0, 0]);
        Assert.Equal(unchecked((int)0xFF000080), target[1, 0]);
        rasterizer.Draw(target, DrawPart.Image("s", 0, 0, 2, 1, 1, 0, 1) with { FlipX = true }, 0, 0);
        Assert.Equal(Red, target[2, 0]);
        rasterizer.Draw(target, DrawPart.Fill(0, 0, 1, 1, new RgbColor(0x00FF00), -1), 0, 0);
        rasterizer.Draw(target, DrawPart.Line(1, 0, 1, 0, new RgbColor(0x0000FF), -1), 0, 0);
        rasterizer.Draw(target, DrawPart.Outline(2, 0, 0, 0, new RgbColor(0xFFFFFF), -1), 0, 0);
        Assert.Equal(new[] { unchecked((int)0xFF00FF00), Blue, unchecked((int)0xFFFFFFFF) }, target.ToArray());
        Assert.Throws<ArgumentException>(() => rasterizer.Draw(target, DrawPart.Image("s", 0, 0, 1, 1, 0, 0, 1) with { Sheet = null }, 0, 0));
    }

    [Fact]
    public void ColoursParseAndFormat()
    {
        RgbColor c = RgbColor.Parse("#c2b651");
        Assert.Equal((0xC2, 0xB6, 0x51), (c.R, c.G, c.B));
        Assert.Equal("#c2b651", c.ToString());
        Assert.Equal(c, RgbColor.FromChannels(0xC2, 0xB6, 0x51));
        Assert.Throws<FormatException>(() => RgbColor.Parse("c2b651"));
        Assert.Throws<FormatException>(() => RgbColor.Parse("#zzzzzz"));
    }
}
