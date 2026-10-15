using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Tribes.Net.Protocol;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>
/// The Photon events of the transport. Every event is reliable, in order, on channel 0, targeted
/// at the actors it is for, never cached by Photon, and carries a byte array:
/// <list type="table">
/// <item><term>1 Data</term><description>one session message</description></item>
/// <item><term>2 Ping, 3 Pong</term><description>the sender's clock (i64); the pong echoes it</description></item>
/// <item><term>4 Open</term><description>client to host: transport version (u8) and a connection number (i32)</description></item>
/// <item><term>5 Accept</term><description>host to client: the connection number of the Open it accepts</description></item>
/// <item><term>6 Close</term><description>either side: the connection number it closes (or refuses)</description></item>
/// <item><term>7 Fragment</term><description>a piece of a large message: flag (1 = first piece, then the total length, i32), then bytes</description></item>
/// <item><term>8 Credit</term><description>receiver to sender: the connection number (i32) and how many bytes of Data and Fragment events arrived so far (i64)</description></item>
/// </list>
/// </summary>
internal static class Wire
{
    public const byte Data = 1;
    public const byte Ping = 2;
    public const byte Pong = 3;
    public const byte Open = 4;
    public const byte Accept = 5;
    public const byte Close = 6;
    public const byte Fragment = 7;
    public const byte Credit = 8;

    /// <summary>The version of these events; a host refuses an Open of another version.</summary>
    public const byte Version = 1;

    /// <summary>The room property naming the host's actor (an int).</summary>
    public const string HostProperty = "h";

    /// <summary>The room property with the advertisement (bytes, see <see cref="EncodeAdvertisement"/>), listed in the lobby.</summary>
    public const string AdvertisementProperty = "a";

    public static byte[] EncodeOpen(int nonce)
    {
        byte[] content = new byte[5];
        content[0] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(1), nonce);
        return content;
    }

    public static bool TryDecodeOpen(byte[]? content, out byte version, out int nonce)
    {
        version = 0;
        nonce = 0;
        if (content is not { Length: 5 })
        {
            return false;
        }

        version = content[0];
        nonce = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(1));
        return true;
    }

    public static byte[] EncodeNonce(int nonce)
    {
        byte[] content = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(content, nonce);
        return content;
    }

    public static bool TryDecodeNonce(byte[]? content, out int nonce)
    {
        nonce = 0;
        if (content is not { Length: 4 })
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadInt32LittleEndian(content);
        return true;
    }

    public static byte[] EncodeCredit(int nonce, long received)
    {
        byte[] content = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(content, nonce);
        BinaryPrimitives.WriteInt64LittleEndian(content.AsSpan(4), received);
        return content;
    }

    public static bool TryDecodeCredit(byte[]? content, out int nonce, out long received)
    {
        nonce = 0;
        received = 0;
        if (content is not { Length: 12 })
        {
            return false;
        }

        nonce = BinaryPrimitives.ReadInt32LittleEndian(content);
        received = BinaryPrimitives.ReadInt64LittleEndian(content.AsSpan(4));
        return received >= 0;
    }

    public static byte[] EncodeStamp(TimeSpan now)
    {
        byte[] content = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(content, now.Ticks);
        return content;
    }

    public static bool TryDecodeStamp(byte[]? content, out TimeSpan stamp)
    {
        stamp = TimeSpan.Zero;
        if (content is not { Length: 8 })
        {
            return false;
        }

        stamp = TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(content));
        return true;
    }

    /// <summary>The events that carry <paramref name="message"/>: one Data event, or Fragment events of at most <paramref name="fragmentBytes"/>.</summary>
    public static IEnumerable<(byte Code, byte[] Content)> Split(ReadOnlyMemory<byte> message, int fragmentBytes)
    {
        if (message.Length <= fragmentBytes)
        {
            yield return (Data, message.ToArray());
            yield break;
        }

        for (int offset = 0; offset < message.Length; offset += fragmentBytes)
        {
            bool first = offset == 0;
            int length = Math.Min(fragmentBytes, message.Length - offset);
            byte[] content = new byte[(first ? 5 : 1) + length];
            content[0] = first ? (byte)1 : (byte)0;
            if (first)
            {
                BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(1), message.Length);
            }

            message.Span.Slice(offset, length).CopyTo(content.AsSpan(first ? 5 : 1));
            yield return (Fragment, content);
        }
    }

    /// <summary>The room advertisement as the room property <see cref="AdvertisementProperty"/>: the protocol version (u16), then the advertisement.</summary>
    public static byte[] EncodeAdvertisement(RoomAdvertisement room)
    {
        var writer = new PacketWriter(256);
        writer.WriteUInt16(NetProtocol.Version);
        AdvertisementCodec.Write(writer, room);
        return writer.ToArray();
    }

    /// <summary>Reads the property; null when it is missing or damaged.</summary>
    public static (int ProtocolVersion, RoomAdvertisement Room)? DecodeAdvertisement(object? property)
    {
        if (property is not byte[] data)
        {
            return null;
        }

        try
        {
            var reader = new PacketReader(data);
            int version = reader.ReadUInt16();
            return (version, AdvertisementCodec.Read(reader));
        }
        catch (ProtocolException)
        {
            return null;
        }
    }
}

/// <summary>Puts the fragments of one sender back together.</summary>
internal sealed class FragmentAssembler
{
    private readonly int maxMessageBytes;
    private byte[]? buffer;
    private int filled;

    public FragmentAssembler(int maxMessageBytes)
    {
        this.maxMessageBytes = maxMessageBytes;
    }

    /// <summary>Whether a message is half received.</summary>
    public bool IsAssembling => buffer is not null;

    /// <summary>
    /// Adds a fragment; returns the whole message when this was its last piece, otherwise null.
    /// </summary>
    /// <exception cref="ProtocolException">The fragment does not continue a message, or the message is too large.</exception>
    public byte[]? Add(byte[]? content)
    {
        if (content is not { Length: > 0 } || content[0] > 1)
        {
            throw new ProtocolException("A fragment is damaged.");
        }

        int offset = 1;
        if (content[0] == 1)
        {
            if (buffer is not null || content.Length < 5)
            {
                throw new ProtocolException("A message started before the previous one was complete.");
            }

            int total = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(1));
            if (total <= 0 || total > maxMessageBytes)
            {
                throw new ProtocolException($"A message of {total} bytes is larger than allowed.");
            }

            buffer = new byte[total];
            filled = 0;
            offset = 5;
        }
        else if (buffer is null)
        {
            throw new ProtocolException("A fragment arrived without the start of its message.");
        }

        int length = content.Length - offset;
        if (length > buffer.Length - filled)
        {
            throw new ProtocolException("A message is longer than it said.");
        }

        content.AsSpan(offset).CopyTo(buffer.AsSpan(filled));
        filled += length;
        if (filled < buffer.Length)
        {
            return null;
        }

        byte[] message = buffer;
        buffer = null;
        filled = 0;
        return message;
    }

    /// <summary>Forgets a half-received message.</summary>
    public void Reset()
    {
        buffer = null;
        filled = 0;
    }
}
