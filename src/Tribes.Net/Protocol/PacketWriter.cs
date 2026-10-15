using System;
using System.Buffers.Binary;
using System.Text;

namespace Tribes.Net.Protocol;

/// <summary>Size limits the decoder enforces on everything it reads from the network.</summary>
/// <remarks>
/// Every session message fits in <see cref="NetProtocol.MaxMessageBytes"/>: a batch holds at most
/// <see cref="MaxBatchPayloadBytes"/> of game payload and <see cref="MaxSystemCommandsPerBatch"/>
/// system commands, a snapshot chunk at most <see cref="MaxChunkBytes"/>, a match start at most
/// <see cref="MaxSetupBytes"/> of game setup.
/// </remarks>
internal static class ProtocolLimits
{
    public const int MaxStringBytes = 256;
    public const int MaxCommandBytes = 64 * 1024;
    public const int MaxCommandsPerBatch = 4096;
    public const int MaxSystemCommandsPerBatch = 64;
    public const int MaxBatchPayloadBytes = 512 * 1024;
    public const int MaxChunkBytes = 512 * 1024;
    public const int MaxSetupBytes = NetProtocol.MaxGameSetupBytes;
    public const int MaxRoomPlayers = RoomLimits.MaxPlayers + RoomLimits.MaxSpectators;
    public const int MaxBannedPlayers = 4096;
    public const int MaxSavedCommands = 1 << 20;
}

/// <summary>
/// Builds a message in the network format: little-endian integers, LEB128 variable-length
/// integers (zigzag for signed values), and UTF-8 strings and byte arrays prefixed with their
/// length.
/// </summary>
internal sealed class PacketWriter
{
    private byte[] buffer;
    private int length;

    public PacketWriter(int capacity = 64)
    {
        buffer = new byte[Math.Max(capacity, 16)];
    }

    public int Length => length;

    public void WriteByte(byte value)
    {
        Reserve(1);
        buffer[length++] = value;
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Reserve(2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(length), value);
        length += 2;
    }

    public void WriteUInt32(uint value)
    {
        Reserve(4);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(length), value);
        length += 4;
    }

    public void WriteInt64(long value)
    {
        Reserve(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(length), value);
        length += 8;
    }

    public void WriteVarUInt(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

    public void WriteGuid(Guid value)
    {
        Reserve(16);
        value.TryWriteBytes(buffer.AsSpan(length, 16));
        length += 16;
    }

    public void WriteString(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarUInt((ulong)byteCount);
        Reserve(byteCount);
        length += Encoding.UTF8.GetBytes(value, buffer.AsSpan(length));
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteVarUInt((ulong)value.Length);
        WriteRaw(value);
    }

    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        Reserve(value.Length);
        value.CopyTo(buffer.AsSpan(length));
        length += value.Length;
    }

    public byte[] ToArray() => buffer.AsSpan(0, length).ToArray();

    private void Reserve(int extra)
    {
        if (length + extra > buffer.Length)
        {
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + extra));
        }
    }
}
