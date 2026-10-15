using System.Collections.Generic;
using Tribes.Net.Transport;

namespace Tribes.Net.Protocol;

/// <summary>
/// The binary form of a <see cref="RoomAdvertisement"/>, shared by the transports that publish
/// rooms (LAN discovery replies, Photon room properties). The fields up to
/// <see cref="RoomAdvertisement.JoinableInProgress"/> keep this layout in every protocol version.
/// An optional extension may follow: a version byte, whether a spectator place is free, the
/// members (count, then public id and presence each) and, from version 2, the host's rules
/// fingerprint (empty when it has none). Readers take what they know and ignore the rest, so
/// older and newer games read each other's rooms.
/// </summary>
internal static class AdvertisementCodec
{
    private const byte MembersExtension = 1;
    private const byte FingerprintExtension = 2;

    public static void Write(PacketWriter writer, RoomAdvertisement room)
    {
        writer.WriteGuid(room.RoomId);
        writer.WriteString(room.Name);
        writer.WriteString(room.HostName);
        writer.WriteByte((byte)room.Mode);
        writer.WriteVarInt(room.Players);
        writer.WriteVarInt(room.MaxPlayers);
        writer.WriteVarInt(room.Spectators);
        writer.WriteString(room.GameVersion);
        writer.WriteString(room.ContentVersion);
        writer.WriteBool(room.InGame);
        writer.WriteBool(room.IsOpen);
        writer.WriteBool(room.JoinableInProgress);
        if (room.Members is null && room.SpectatorsWelcome is null && room.RulesFingerprint is null)
        {
            return;
        }

        writer.WriteByte(FingerprintExtension);
        writer.WriteBool(room.SpectatorsWelcome ?? false);
        IReadOnlyList<RoomMember> members = room.Members ?? [];
        writer.WriteVarUInt((ulong)members.Count);
        foreach (RoomMember member in members)
        {
            writer.WriteGuid(member.PlayerId);
            writer.WriteByte((byte)member.Presence);
        }

        writer.WriteString(room.RulesFingerprint ?? string.Empty);
    }

    /// <summary>Reads an advertisement written by <see cref="Write"/>, up to the end of the data.</summary>
    /// <exception cref="ProtocolException">The bytes are not an advertisement.</exception>
    public static RoomAdvertisement Read(PacketReader reader)
    {
        var room = new RoomAdvertisement
        {
            RoomId = reader.ReadGuid(),
            Name = reader.ReadString(),
            HostName = reader.ReadString(),
            Mode = reader.ReadEnum<RoomMode>("Room mode"),
            Players = reader.ReadInt(0, RoomLimits.MaxPlayers, "Players"),
            MaxPlayers = reader.ReadInt(0, RoomLimits.MaxPlayers, "Max players"),
            Spectators = reader.ReadInt(0, RoomLimits.MaxSpectators, "Spectators"),
            GameVersion = reader.ReadString(),
            ContentVersion = reader.ReadString(),
            InGame = reader.ReadBool(),
            IsOpen = reader.ReadBool(),
            JoinableInProgress = reader.ReadBool(),
        };
        byte extension = reader.Remaining == 0 ? (byte)0 : reader.ReadByte();
        if (extension < MembersExtension)
        {
            return room;
        }

        bool spectatorsWelcome = reader.ReadBool();
        int count = reader.ReadCount(ProtocolLimits.MaxRoomPlayers, "Members");
        var members = new RoomMember[count];
        for (int i = 0; i < count; i++)
        {
            members[i] = new RoomMember(reader.ReadGuid(), reader.ReadEnum<PlayerPresence>("Member presence"));
        }

        string? fingerprint = null;
        if (extension >= FingerprintExtension)
        {
            string read = reader.ReadString(RoomLimits.MaxVersionLength);
            fingerprint = read.Length > 0 ? read : null;
        }

        return room with { Members = members, SpectatorsWelcome = spectatorsWelcome, RulesFingerprint = fingerprint };
    }
}
