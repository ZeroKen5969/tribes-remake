using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Tribes.Net.Protocol;

/// <summary>FNV-1a, 32 bits: a quick checksum of snapshot bytes.</summary>
internal static class Fnv1a
{
    public const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Hash(ReadOnlySpan<byte> data, uint hash = OffsetBasis)
    {
        foreach (byte value in data)
        {
            hash = (hash ^ value) * Prime;
        }

        return hash;
    }
}

/// <summary>A snapshot ready to send: compressed when that helps, cut into chunks.</summary>
internal sealed class PackedSnapshot
{
    public PackedSnapshot(bool compressed, int rawLength, uint checksum, IReadOnlyList<byte[]> chunks)
    {
        Compressed = compressed;
        RawLength = rawLength;
        Checksum = checksum;
        Chunks = chunks;
    }

    public bool Compressed { get; }

    public int RawLength { get; }

    public uint Checksum { get; }

    public IReadOnlyList<byte[]> Chunks { get; }
}

internal static class SnapshotPacker
{
    private const int CompressFromBytes = 256;

    public static PackedSnapshot Pack(byte[] raw, int chunkBytes)
    {
        byte[] body = raw;
        bool compressed = false;
        if (raw.Length >= CompressFromBytes)
        {
            byte[] smaller = Compress(raw);
            if (smaller.Length < raw.Length)
            {
                body = smaller;
                compressed = true;
            }
        }

        var chunks = new List<byte[]>();
        for (int offset = 0; offset < body.Length; offset += chunkBytes)
        {
            chunks.Add(body.AsSpan(offset, Math.Min(chunkBytes, body.Length - offset)).ToArray());
        }

        if (chunks.Count == 0)
        {
            chunks.Add([]);
        }

        return new PackedSnapshot(compressed, raw.Length, Fnv1a.Hash(raw), chunks);
    }

    public static byte[] Compress(ReadOnlySpan<byte> raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Inflates data that must expand to exactly <paramref name="rawLength"/> bytes. Memory grows
    /// with the data actually inflated, so a damaged length cannot make it allocate gigabytes.
    /// </summary>
    public static byte[] Decompress(byte[] data, int rawLength)
    {
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream((int)Math.Min(rawLength, 4L * data.Length));
            var buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer)) > 0)
            {
                if (output.Length + read > rawLength)
                {
                    throw new ProtocolException("A snapshot is longer than its header says.");
                }

                output.Write(buffer, 0, read);
            }

            if (output.Length != rawLength)
            {
                throw new ProtocolException("A snapshot is shorter than its header says.");
            }

            return output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new ProtocolException("A snapshot is not valid compressed data.", ex);
        }
    }
}

/// <summary>Collects the chunks of one snapshot in order and checks the result.</summary>
internal sealed class SnapshotAssembler
{
    private readonly List<ReadOnlyMemory<byte>> chunks = [];
    private readonly SnapshotBeginMessage header;
    private readonly int maxBytes;
    private long bodyLength;
    private int received;

    public SnapshotAssembler(SnapshotBeginMessage header, int maxBytes)
    {
        if (header.RawLength > maxBytes)
        {
            throw new ProtocolException($"A snapshot of {header.RawLength} bytes exceeds the limit of {maxBytes}.");
        }

        this.header = header;
        this.maxBytes = maxBytes;
    }

    public SnapshotBeginMessage Header => header;

    /// <summary>Adds the next chunk; true when it was the last one.</summary>
    public bool Add(SnapshotChunkMessage chunk)
    {
        if (chunk.Index != received || received >= header.ChunkCount)
        {
            throw new ProtocolException($"Expected snapshot chunk {received} but received {chunk.Index}.");
        }

        if (bodyLength + chunk.Data.Length > maxBytes)
        {
            throw new ProtocolException($"A snapshot exceeds the limit of {maxBytes} bytes.");
        }

        if (chunk.Data.IsEmpty && header.ChunkCount > 1)
        {
            throw new ProtocolException("A snapshot chunk is empty.");
        }

        chunks.Add(chunk.Data);
        bodyLength += chunk.Data.Length;
        received++;
        return received == header.ChunkCount;
    }

    public byte[] Complete()
    {
        var data = new byte[bodyLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> chunk in chunks)
        {
            chunk.Span.CopyTo(data.AsSpan(offset));
            offset += chunk.Length;
        }

        byte[] raw = header.Compressed ? SnapshotPacker.Decompress(data, header.RawLength) : data;
        if (raw.Length != header.RawLength)
        {
            throw new ProtocolException($"A snapshot has {raw.Length} bytes but its header says {header.RawLength}.");
        }

        if (Fnv1a.Hash(raw) != header.Checksum)
        {
            throw new ProtocolException("A snapshot does not match its checksum.");
        }

        return raw;
    }
}
