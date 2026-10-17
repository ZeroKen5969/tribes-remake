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

    /// <summary>
    /// Deflate turns at most 258 bytes into about two bits, so compressed data never expands to more
    /// than this many times its size (with a little room for the zlib header and tiny inputs).
    /// </summary>
    private const int MaxInflateRatio = 1032;

    private const int InflateSlackBytes = 64;

    public static PackedSnapshot Pack(byte[] raw, int chunkBytes)
    {
        ReadOnlyMemory<byte> body = raw;
        bool compressed = false;
        using var output = new MemoryStream();
        if (raw.Length >= CompressFromBytes)
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            if (output.Length < raw.Length)
            {
                // The chunks are cut from the stream's own buffer: a large world is not copied once more.
                body = output.GetBuffer().AsMemory(0, (int)output.Length);
                compressed = true;
            }
        }

        var chunks = new List<byte[]>();
        for (int offset = 0; offset < body.Length; offset += chunkBytes)
        {
            chunks.Add(body.Span.Slice(offset, Math.Min(chunkBytes, body.Length - offset)).ToArray());
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

    /// <summary>Inflates data that must expand to exactly <paramref name="rawLength"/> bytes.</summary>
    public static byte[] Decompress(byte[] data, int rawLength)
    {
        using var input = new MemoryStream(data, writable: false);
        return Inflate(input, data.LongLength, rawLength);
    }

    /// <summary>
    /// Inflates <paramref name="compressedLength"/> bytes of <paramref name="input"/> straight into an
    /// array of <paramref name="rawLength"/> bytes, the only copy of the data made. A length the data
    /// cannot expand to is refused before anything is allocated, so a damaged length cannot make it
    /// allocate gigabytes.
    /// </summary>
    public static byte[] Inflate(Stream input, long compressedLength, int rawLength)
    {
        if (rawLength < 0 || rawLength > (compressedLength * MaxInflateRatio) + InflateSlackBytes)
        {
            throw new ProtocolException("A snapshot is shorter than its header says.");
        }

        try
        {
            using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
            var raw = new byte[rawLength];
            int filled = 0;
            while (filled < rawLength)
            {
                int read = zlib.Read(raw, filled, rawLength - filled);
                if (read == 0)
                {
                    throw new ProtocolException("A snapshot is shorter than its header says.");
                }

                filled += read;
            }

            if (zlib.Read(new byte[1], 0, 1) > 0)
            {
                throw new ProtocolException("A snapshot is longer than its header says.");
            }

            return raw;
        }
        catch (InvalidDataException ex)
        {
            throw new ProtocolException("A snapshot is not valid compressed data.", ex);
        }
    }
}

/// <summary>Reads a list of byte blocks one after another as one stream.</summary>
internal sealed class BlockStream : Stream
{
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> blocks;
    private int block;
    private int offset;

    public BlockStream(IReadOnlyList<ReadOnlyMemory<byte>> blocks) => this.blocks = blocks;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (block < blocks.Count && offset == blocks[block].Length)
        {
            block++;
            offset = 0;
        }

        if (block == blocks.Count)
        {
            return 0;
        }

        int count = Math.Min(buffer.Length, blocks[block].Length - offset);
        blocks[block].Span.Slice(offset, count).CopyTo(buffer);
        offset += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    /// <summary>Chunks received so far.</summary>
    public int Received => received;

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

    /// <summary>The snapshot, checked; the chunks are let go so only one copy of a large world stays.</summary>
    public byte[] Complete()
    {
        byte[] raw;
        if (header.Compressed)
        {
            using var body = new BlockStream(chunks);
            raw = SnapshotPacker.Inflate(body, bodyLength, header.RawLength);
        }
        else
        {
            raw = new byte[bodyLength];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> chunk in chunks)
            {
                chunk.Span.CopyTo(raw.AsSpan(offset));
                offset += chunk.Length;
            }
        }

        chunks.Clear();
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
