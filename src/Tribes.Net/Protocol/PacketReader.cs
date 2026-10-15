using System;
using System.Buffers.Binary;
using System.Text;

namespace Tribes.Net.Protocol;

/// <summary>
/// Reads the format <see cref="PacketWriter"/> writes. Every read checks the remaining length and
/// the caller's limit, and throws <see cref="ProtocolException"/> instead of reading past the end
/// or allocating what a hostile peer asks for.
/// </summary>
internal sealed class PacketReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlyMemory<byte> data;
    private int position;

    public PacketReader(ReadOnlyMemory<byte> data)
    {
        this.data = data;
    }

    public int Remaining => data.Length - position;

    public byte ReadByte()
    {
        Require(1);
        return data.Span[position++];
    }

    public bool ReadBool()
    {
        byte value = ReadByte();
        if (value > 1)
        {
            throw new ProtocolException($"Expected a boolean but found {value}.");
        }

        return value == 1;
    }

    public ushort ReadUInt16()
    {
        Require(2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[position..]);
        position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Require(4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[position..]);
        position += 4;
        return value;
    }

    public long ReadInt64()
    {
        Require(8);
        long value = BinaryPrimitives.ReadInt64LittleEndian(data.Span[position..]);
        position += 8;
        return value;
    }

    public ulong ReadVarUInt()
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte part = ReadByte();
            if (shift == 63 && part > 1)
            {
                throw new ProtocolException("A variable-length integer overflows 64 bits.");
            }

            result |= (ulong)(part & 0x7F) << shift;
            if ((part & 0x80) == 0)
            {
                return result;
            }
        }

        throw new ProtocolException("A variable-length integer is longer than 10 bytes.");
    }

    public long ReadVarInt()
    {
        ulong raw = ReadVarUInt();
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    /// <summary>A signed value that must lie within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public long ReadVarInt(long min, long max, string what)
    {
        long value = ReadVarInt();
        if (value < min || value > max)
        {
            throw new ProtocolException($"{what} {value} is outside {min}..{max}.");
        }

        return value;
    }

    /// <summary>An int that must lie within [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public int ReadInt(int min, int max, string what) => (int)ReadVarInt(min, max, what);

    public int ReadCount(int max, string what)
    {
        ulong value = ReadVarUInt();
        if (value > (ulong)max)
        {
            throw new ProtocolException($"{what} {value} exceeds the limit of {max}.");
        }

        return (int)value;
    }

    public Guid ReadGuid()
    {
        Require(16);
        var value = new Guid(data.Span.Slice(position, 16));
        position += 16;
        return value;
    }

    public string ReadString(int maxBytes = ProtocolLimits.MaxStringBytes)
    {
        int byteCount = ReadCount(maxBytes, "String length");
        Require(byteCount);
        string value;
        try
        {
            value = StrictUtf8.GetString(data.Span.Slice(position, byteCount));
        }
        catch (DecoderFallbackException ex)
        {
            throw new ProtocolException("A string is not valid UTF-8.", ex);
        }

        position += byteCount;
        return value;
    }

    /// <summary>A length-prefixed byte array, as a slice of the message (no copy).</summary>
    public ReadOnlyMemory<byte> ReadBytes(int maxBytes)
    {
        int byteCount = ReadCount(maxBytes, "Byte array length");
        Require(byteCount);
        ReadOnlyMemory<byte> value = data.Slice(position, byteCount);
        position += byteCount;
        return value;
    }

    public TEnum ReadEnum<TEnum>(string what)
        where TEnum : struct, Enum
    {
        int raw = ReadByte();
        var value = (TEnum)Enum.ToObject(typeof(TEnum), raw);
        if (!Enum.IsDefined(value))
        {
            throw new ProtocolException($"{what} {raw} is not defined.");
        }

        return value;
    }

    public void ExpectEnd()
    {
        if (Remaining != 0)
        {
            throw new ProtocolException($"{Remaining} unexpected bytes after the end of the message.");
        }
    }

    private void Require(int count)
    {
        if (Remaining < count)
        {
            throw new ProtocolException("The message is truncated.");
        }
    }
}
