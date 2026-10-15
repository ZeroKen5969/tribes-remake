using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Tribes.Graphics.Imaging;

/// <summary>One chunk of a PNG file.</summary>
/// <param name="Type">Four-letter chunk type.</param>
/// <param name="Data">Chunk contents.</param>
public sealed record PngChunk(string Type, byte[] Data);

/// <summary>
/// Reads and writes PNG files: non-interlaced greyscale, RGB, palette (with tRNS), grey-alpha and
/// RGBA at 8 bits per channel, and palette or greyscale at 1, 2, 4 and 8 bits per pixel.
/// </summary>
public static class Png
{
    private const int SignatureLength = 8;
    private const int ChunkOverhead = 12;
    private const int HeaderLength = 13;
    private const int BitsPerByte = 8;
    private const byte FilterNone = 0;
    private const byte FilterSub = 1;
    private const byte FilterUp = 2;
    private const byte FilterAverage = 3;
    private const byte FilterPaeth = 4;
    private const byte ColorGray = 0;
    private const byte ColorRgb = 2;
    private const byte ColorPalette = 3;
    private const byte ColorGrayAlpha = 4;
    private const byte ColorRgba = 6;
    private const int Opaque = 255;
    private const int RgbChannels = 3;
    private const int RgbaChannels = 4;

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Splits a PNG file into its chunks (checking the signature and the chunk CRCs).</summary>
    public static IReadOnlyList<PngChunk> ReadChunks(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < SignatureLength || !png.AsSpan(0, SignatureLength).SequenceEqual(Signature))
        {
            throw new InvalidDataException("not a PNG file");
        }
        var chunks = new List<PngChunk>();
        int pos = SignatureLength;
        while (pos + ChunkOverhead <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            if (length < 0 || pos + ChunkOverhead + length > png.Length)
            {
                throw new InvalidDataException("truncated PNG chunk");
            }
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            byte[] data = png.AsSpan(pos + BitsPerByte, length).ToArray();
            uint stored = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + BitsPerByte + length));
            if (stored != Crc32.Compute(png.AsSpan(pos + 4, 4 + length)))
            {
                throw new InvalidDataException($"bad CRC in PNG chunk {type}");
            }
            chunks.Add(new PngChunk(type, data));
            pos += ChunkOverhead + length;
            if (type == "IEND")
            {
                break;
            }
        }
        return chunks;
    }

    /// <summary>Joins chunks into a PNG file.</summary>
    public static byte[] WriteChunks(IEnumerable<PngChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> word = stackalloc byte[4];
        foreach (PngChunk chunk in chunks)
        {
            byte[] type = Encoding.ASCII.GetBytes(chunk.Type);
            BinaryPrimitives.WriteInt32BigEndian(word, chunk.Data.Length);
            output.Write(word);
            output.Write(type);
            output.Write(chunk.Data);
            byte[] crcInput = new byte[type.Length + chunk.Data.Length];
            type.CopyTo(crcInput, 0);
            chunk.Data.CopyTo(crcInput, type.Length);
            BinaryPrimitives.WriteUInt32BigEndian(word, Crc32.Compute(crcInput));
            output.Write(word);
        }
        return output.ToArray();
    }

    /// <summary>
    /// The same image without ancillary chunks other than transparency (gamma, text): every
    /// decoder then shows the stored pixel values unchanged.
    /// </summary>
    public static byte[] KeepPixelChunks(byte[] png)
    {
        var kept = new List<PngChunk>();
        foreach (PngChunk chunk in ReadChunks(png))
        {
            if (char.IsUpper(chunk.Type[0]) || chunk.Type == "tRNS")
            {
                kept.Add(chunk);
            }
        }
        return WriteChunks(kept);
    }

    /// <summary>Width and height from the header.</summary>
    public static (int Width, int Height) ReadSize(byte[] png)
    {
        PngChunk header = ReadChunks(png)[0];
        if (header.Type != "IHDR" || header.Data.Length != HeaderLength)
        {
            throw new InvalidDataException("PNG header missing");
        }
        return (BinaryPrimitives.ReadInt32BigEndian(header.Data), BinaryPrimitives.ReadInt32BigEndian(header.Data.AsSpan(4)));
    }

    /// <summary>Decodes a PNG file.</summary>
    public static RgbaImage Decode(byte[] png)
    {
        IReadOnlyList<PngChunk> chunks = ReadChunks(png);
        PngChunk header = chunks[0];
        if (header.Type != "IHDR" || header.Data.Length != HeaderLength)
        {
            throw new InvalidDataException("PNG header missing");
        }
        int width = BinaryPrimitives.ReadInt32BigEndian(header.Data);
        int height = BinaryPrimitives.ReadInt32BigEndian(header.Data.AsSpan(4));
        int depth = header.Data[8];
        byte colorType = header.Data[9];
        if (header.Data[12] != 0)
        {
            throw new NotSupportedException("interlaced PNG files are not supported");
        }
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        foreach (PngChunk chunk in chunks)
        {
            switch (chunk.Type)
            {
                case "PLTE":
                    palette = chunk.Data;
                    break;
                case "tRNS":
                    transparency = chunk.Data;
                    break;
                case "IDAT":
                    compressed.Write(chunk.Data);
                    break;
                default:
                    break;
            }
        }
        int channels = Channels(colorType, depth);
        int bitsPerPixel = channels * depth;
        int stride = ((width * bitsPerPixel) + BitsPerByte - 1) / BitsPerByte;
        int bytesPerPixel = Math.Max(1, bitsPerPixel / BitsPerByte);
        byte[] raw = Inflate(compressed.ToArray(), (stride + 1) * height);
        byte[] previous = new byte[stride];
        byte[] line = new byte[stride];
        var image = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int start = y * (stride + 1);
            Array.Copy(raw, start + 1, line, 0, stride);
            Unfilter(raw[start], line, previous, bytesPerPixel);
            for (int x = 0; x < width; x++)
            {
                image[x, y] = Pixel(line, x, colorType, depth, palette, transparency);
            }
            (previous, line) = (line, previous);
        }
        return image;
    }

    /// <summary>Encodes an image as an 8-bit RGBA PNG file.</summary>
    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int stride = image.Width * RgbaChannels;
        byte[] previous = new byte[stride];
        byte[] line = new byte[stride];
        using var raw = new MemoryStream();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int argb = image[x, y];
                line[x * RgbaChannels] = (byte)(argb >> 16);
                line[(x * RgbaChannels) + 1] = (byte)(argb >> BitsPerByte);
                line[(x * RgbaChannels) + 2] = (byte)argb;
                line[(x * RgbaChannels) + 3] = (byte)(argb >> 24);
            }
            WriteFilteredLine(raw, line, previous, RgbaChannels);
            (previous, line) = (line, previous);
        }
        byte[] header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = BitsPerByte;
        header[9] = ColorRgba;
        return WriteChunks(new[]
        {
            new PngChunk("IHDR", header),
            new PngChunk("IDAT", Deflate(raw.ToArray())),
            new PngChunk("IEND", Array.Empty<byte>()),
        });
    }

    private static int Channels(byte colorType, int depth)
    {
        bool lowDepth = depth is 1 or 2 or 4;
        return colorType switch
        {
            ColorGray when lowDepth || depth == BitsPerByte => 1,
            ColorPalette when lowDepth || depth == BitsPerByte => 1,
            ColorRgb when depth == BitsPerByte => RgbChannels,
            ColorGrayAlpha when depth == BitsPerByte => 2,
            ColorRgba when depth == BitsPerByte => RgbaChannels,
            _ => throw new NotSupportedException($"PNG colour type {colorType} at {depth} bits is not supported"),
        };
    }

    private static int Pixel(byte[] line, int x, byte colorType, int depth, byte[]? palette, byte[]? transparency)
    {
        switch (colorType)
        {
            case ColorPalette:
                {
                    int index = Sample(line, x, depth);
                    if (palette == null || (index * RgbChannels) + 2 >= palette.Length)
                    {
                        throw new InvalidDataException("PNG palette index out of range");
                    }
                    int alpha = transparency != null && index < transparency.Length ? transparency[index] : Opaque;
                    return Argb(alpha, palette[index * RgbChannels], palette[(index * RgbChannels) + 1], palette[(index * RgbChannels) + 2]);
                }
            case ColorGray:
                {
                    int level = Sample(line, x, depth) * (Opaque / ((1 << depth) - 1));
                    return Argb(Opaque, level, level, level);
                }
            case ColorRgb:
                return Argb(Opaque, line[x * RgbChannels], line[(x * RgbChannels) + 1], line[(x * RgbChannels) + 2]);
            case ColorGrayAlpha:
                return Argb(line[(x * 2) + 1], line[x * 2], line[x * 2], line[x * 2]);
            default:
                return Argb(line[(x * RgbaChannels) + 3], line[x * RgbaChannels], line[(x * RgbaChannels) + 1], line[(x * RgbaChannels) + 2]);
        }
    }

    private static int Sample(byte[] line, int x, int depth)
    {
        if (depth == BitsPerByte)
        {
            return line[x];
        }
        int perByte = BitsPerByte / depth;
        int shift = BitsPerByte - (depth * ((x % perByte) + 1));
        return (line[x / perByte] >> shift) & ((1 << depth) - 1);
    }

    private static int Argb(int a, int r, int g, int b) => (a << 24) | (r << 16) | (g << BitsPerByte) | b;

    private static void Unfilter(byte filter, byte[] line, byte[] previous, int bpp)
    {
        for (int i = 0; i < line.Length; i++)
        {
            int left = i >= bpp ? line[i - bpp] : 0;
            int up = previous[i];
            int upLeft = i >= bpp ? previous[i - bpp] : 0;
            int predictor = filter switch
            {
                FilterNone => 0,
                FilterSub => left,
                FilterUp => up,
                FilterAverage => (left + up) >> 1,
                FilterPaeth => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"unknown PNG filter {filter}"),
            };
            line[i] = (byte)(line[i] + predictor);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }
        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static void WriteFilteredLine(Stream output, byte[] line, byte[] previous, int bpp)
    {
        byte bestFilter = FilterNone;
        long bestScore = long.MaxValue;
        byte[] candidate = new byte[line.Length];
        byte[] best = new byte[line.Length];
        for (byte filter = FilterNone; filter <= FilterPaeth; filter++)
        {
            long score = 0;
            for (int i = 0; i < line.Length; i++)
            {
                int left = i >= bpp ? line[i - bpp] : 0;
                int up = previous[i];
                int upLeft = i >= bpp ? previous[i - bpp] : 0;
                int predictor = filter switch
                {
                    FilterSub => left,
                    FilterUp => up,
                    FilterAverage => (left + up) >> 1,
                    FilterPaeth => Paeth(left, up, upLeft),
                    _ => 0,
                };
                candidate[i] = (byte)(line[i] - predictor);
                score += Math.Abs((int)(sbyte)candidate[i]);
            }
            if (score < bestScore)
            {
                bestScore = score;
                bestFilter = filter;
                (best, candidate) = (candidate, best);
            }
        }
        output.WriteByte(bestFilter);
        output.Write(best);
    }

    private static byte[] Inflate(byte[] data, int expected)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        byte[] raw = new byte[expected];
        int read = 0;
        while (read < expected)
        {
            int n = zlib.Read(raw, read, expected - read);
            if (n == 0)
            {
                throw new InvalidDataException("PNG image data is truncated");
            }
            read += n;
        }
        return raw;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return output.ToArray();
    }
}

/// <summary>The CRC-32 used by PNG and ZIP (polynomial 0xEDB88320).</summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320;
    private const int TableSize = 256;
    private const int BitsPerByte = 8;
    private static readonly uint[] Table = BuildTable();

    /// <summary>The CRC-32 of <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Append(0, data);

    /// <summary>Continues a CRC-32 over more data (start from 0).</summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        uint value = ~crc;
        foreach (byte b in data)
        {
            value = Table[(value ^ b) & byte.MaxValue] ^ (value >> BitsPerByte);
        }
        return ~value;
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[TableSize];
        for (uint n = 0; n < TableSize; n++)
        {
            uint c = n;
            for (int k = 0; k < BitsPerByte; k++)
            {
                c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
