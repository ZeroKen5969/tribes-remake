using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Tribes.Session;

/// <summary>Raw pixels: rows top to bottom, 3 (RGB) or 4 (RGBA) bytes per pixel.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Channels">3 or 4.</param>
/// <param name="Data">The pixel bytes.</param>
public sealed record Pixels(int Width, int Height, int Channels, byte[] Data)
{
    /// <summary>Throws when the size and the data do not match.</summary>
    public void Check()
    {
        if (Width <= 0 || Height <= 0 || Channels is not (3 or 4) || Data is null || Data.Length != Width * Height * Channels)
        {
            throw new ArgumentException($"{Width}x{Height}x{Channels} pixels do not fit {Data?.Length ?? 0} bytes.");
        }
    }
}

/// <summary>
/// Save pictures made without the engine, so they can be shrunk and encoded on a background
/// thread: halving with a box filter and a plain PNG encoder (8 bits per channel, no interlace).
/// </summary>
public static class Thumbnails
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>The picture at half the size, each pixel the average of a 2x2 block (odd edges dropped).</summary>
    public static Pixels Halve(Pixels source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Check();
        int width = Math.Max(1, source.Width / 2), height = Math.Max(1, source.Height / 2), c = source.Channels;
        byte[] data = new byte[width * height * c];
        int stride = source.Width * c;
        for (int y = 0; y < height; y++)
        {
            int top = Math.Min(2 * y, source.Height - 1) * stride;
            int bottom = Math.Min((2 * y) + 1, source.Height - 1) * stride;
            for (int x = 0; x < width; x++)
            {
                int left = Math.Min(2 * x, source.Width - 1) * c;
                int right = Math.Min((2 * x) + 1, source.Width - 1) * c;
                for (int k = 0; k < c; k++)
                {
                    int sum = source.Data[top + left + k] + source.Data[top + right + k] + source.Data[bottom + left + k] + source.Data[bottom + right + k];
                    data[(((y * width) + x) * c) + k] = (byte)((sum + 2) / 4);
                }
            }
        }
        return new Pixels(width, height, c, data);
    }

    /// <summary>The picture as a PNG file.</summary>
    public static byte[] EncodePng(Pixels image)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.Check();
        using var file = new MemoryStream();
        file.Write(Signature);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bits per channel
        header[9] = (byte)(image.Channels == 4 ? 6 : 2); // RGBA or RGB
        WriteChunk(file, "IHDR", header);
        using (var packed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true))
            {
                int stride = image.Width * image.Channels;
                for (int y = 0; y < image.Height; y++)
                {
                    zlib.WriteByte(0); // no filter
                    zlib.Write(image.Data, y * stride, stride);
                }
            }
            WriteChunk(file, "IDAT", packed.ToArray());
        }
        WriteChunk(file, "IEND", []);
        return file.ToArray();
    }

    /// <summary>The CRC-32 of a PNG chunk (type and data).</summary>
    internal static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static void WriteChunk(Stream file, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        byte[] typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        file.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        file.Write(number);
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
