using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Tribes.Session.Tests;

public sealed class ThumbnailTests
{
    [Fact]
    public void Halve_AveragesEachBlock()
    {
        // 4x2 RGB: left block 0/100/200/40 -> 85, right block all 255.
        byte[] data =
        [
            0, 0, 0, 100, 100, 100, 255, 255, 255, 255, 255, 255,
            200, 200, 200, 40, 40, 40, 255, 255, 255, 255, 255, 255,
        ];
        Pixels half = Thumbnails.Halve(new Pixels(4, 2, 3, data));
        Assert.Equal((2, 1, 3), (half.Width, half.Height, half.Channels));
        Assert.Equal(new byte[] { 85, 85, 85, 255, 255, 255 }, half.Data);

        Pixels odd = Thumbnails.Halve(new Pixels(3, 3, 4, new byte[3 * 3 * 4]));
        Assert.Equal((1, 1), (odd.Width, odd.Height));
        Pixels tiny = Thumbnails.Halve(new Pixels(1, 1, 4, [1, 2, 3, 4]));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, tiny.Data);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3)]
    public void EncodePng_WritesAValidFile(int channels)
    {
        int width = 5, height = 3;
        byte[] data = new byte[width * height * channels];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }
        byte[] png = Thumbnails.EncodePng(new Pixels(width, height, channels, data));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        int at = 8;
        byte[]? idat = null;
        string? last = null;
        while (at < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = Encoding.ASCII.GetString(png, at + 4, 4);
            byte[] body = png[(at + 8)..(at + 8 + length)];
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            Assert.Equal(Thumbnails.Crc(png.AsSpan(at + 4, 4 + length)), crc);
            if (type == "IHDR")
            {
                Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(body));
                Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4)));
                Assert.Equal((byte)8, body[8]);
                Assert.Equal((byte)(channels == 4 ? 6 : 2), body[9]);
            }
            if (type == "IDAT")
            {
                idat = body;
            }
            last = type;
            at += 12 + length;
        }
        Assert.Equal("IEND", last);
        // The well-known CRC of an empty IEND chunk.
        Assert.Equal(0xAE426082u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(png.Length - 4)));

        using var zlib = new ZLibStream(new MemoryStream(idat!), CompressionMode.Decompress);
        using var rows = new MemoryStream();
        zlib.CopyTo(rows);
        byte[] raw = rows.ToArray();
        int stride = width * channels;
        Assert.Equal(height * (stride + 1), raw.Length);
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * (stride + 1)]);
            Assert.Equal(data.AsSpan(y * stride, stride).ToArray(), raw.AsSpan((y * (stride + 1)) + 1, stride).ToArray());
        }
    }

    [Fact]
    public void BadPixels_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => Thumbnails.EncodePng(new Pixels(2, 2, 4, new byte[3])));
        Assert.Throws<ArgumentException>(() => Thumbnails.Halve(new Pixels(2, 2, 2, new byte[8])));
        Assert.Throws<ArgumentException>(() => new Pixels(0, 2, 3, []).Check());
        Assert.Throws<ArgumentNullException>(() => Thumbnails.EncodePng(null!));
        Assert.Throws<ArgumentNullException>(() => Thumbnails.Halve(null!));
    }
}
