using System;
using System.Security.Cryptography;
using Tribes.Net.Protocol;

namespace Tribes.Net;

/// <summary>Helpers both session types use.</summary>
internal static class SessionShared
{
    /// <summary>
    /// The id other peers see for a player: a hash of the player's secret key and the room id.
    /// It stays the same across reconnects and resumed saves of the room, while the key, the only
    /// proof of who a returning player is, never leaves the player's device and the host.
    /// </summary>
    public static Guid PublicId(Guid playerKey, Guid roomId)
    {
        Span<byte> input = stackalloc byte[48];
        playerKey.TryWriteBytes(input);
        roomId.TryWriteBytes(input[16..]);
        "tribes.player.v1"u8.CopyTo(input[32..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    public static string CleanName(string? name)
    {
        string clean = RoomLimits.CleanText(name, RoomLimits.MaxNameLength);
        return clean.Length == 0 ? "Player" : clean;
    }

    public static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && version.Length <= RoomLimits.MaxVersionLength;

    /// <summary>A rules fingerprint may be empty (not checked) but not longer than a version.</summary>
    public static bool IsValidFingerprint(string? fingerprint) =>
        fingerprint is not null && fingerprint.Length <= RoomLimits.MaxVersionLength;

    public static string ProtocolMismatchText(int clientVersion) =>
        $"Your network protocol version {clientVersion} differs from the host's {NetProtocol.Version}.";

    /// <summary>Raises one event per system command of a batch that just ran.</summary>
    public static void RaisePresenceChanges(object sender, TickBatch batch, RoomState? room, EventHandler<PlayerPresenceChangedEventArgs>? handler)
    {
        if (handler is null)
        {
            return;
        }

        foreach (PlayerCommand command in batch.Commands)
        {
            if (!command.IsSystem)
            {
                continue;
            }

            PlayerInfo? player = command.Kind == CommandKind.PlayerJoined ? command.ReadJoinedPlayer() : room?.PlayerInSlot(command.Slot);
            handler(sender, new PlayerPresenceChangedEventArgs
            {
                Tick = batch.Tick,
                Slot = command.Slot,
                Change = command.Kind,
                Presence = command.Kind switch
                {
                    CommandKind.PlayerAbsent => PlayerPresence.Absent,
                    CommandKind.PlayerForfeited => PlayerPresence.Forfeited,
                    _ => PlayerPresence.Active,
                },
                Player = player,
            });
        }
    }

    public static string Describe(RequestResult result) => result switch
    {
        RequestResult.Accepted => "Done.",
        RequestResult.NotAllowedNow => "That is not possible right now.",
        RequestResult.InvalidValue => "That value is not allowed.",
        RequestResult.ColourTaken => "Another tribe already has that colour.",
        RequestResult.NotAPlayer => "Only active players can do that.",
        RequestResult.PlayersNotReady => "Not every player is ready.",
        RequestResult.NotEnoughPlayers => "More players are needed to start.",
        RequestResult.NeedTwoTeams => "A versus match needs at least two teams.",
        RequestResult.NotFound => "No such player or slot.",
        RequestResult.TooManyCommands => "Too many commands in one tick.",
        RequestResult.RateLimited => "Too many messages; wait a moment.",
        _ => result.ToString(),
    };
}
