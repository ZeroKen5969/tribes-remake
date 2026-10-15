using System;
using System.Collections.Generic;
using System.Linq;

namespace Tribes.Net.Transport;

/// <summary>A person holding a slot in an advertised room.</summary>
/// <param name="PlayerId">The public id, as <see cref="PlayerInfo.Id"/> in the room.</param>
/// <param name="Presence">Whether they are playing, away, or have forfeited.</param>
public sealed record RoomMember(Guid PlayerId, PlayerPresence Presence);

/// <summary>What the host publishes about its room for room lists.</summary>
public sealed record RoomAdvertisement
{
    /// <summary>Id of the room.</summary>
    public required Guid RoomId { get; init; }

    /// <summary>Room name.</summary>
    public required string Name { get; init; }

    /// <summary>Name of the host player.</summary>
    public string HostName { get; init; } = string.Empty;

    /// <summary>Versus or endless.</summary>
    public RoomMode Mode { get; init; }

    /// <summary>Slots taken (people and computer players).</summary>
    public int Players { get; init; }

    /// <summary>Slots in the room.</summary>
    public int MaxPlayers { get; init; }

    /// <summary>Spectators in the room.</summary>
    public int Spectators { get; init; }

    /// <summary>The host's game version.</summary>
    public required string GameVersion { get; init; }

    /// <summary>The host's content version.</summary>
    public required string ContentVersion { get; init; }

    /// <summary>Whether the match is running.</summary>
    public bool InGame { get; init; }

    /// <summary>Whether a new player could take a slot now (in the lobby, or joining a running endless match).</summary>
    public bool IsOpen { get; init; }

    /// <summary>Whether the room accepts new players while its match runs (endless rooms).</summary>
    public bool JoinableInProgress { get; init; }

    /// <summary>
    /// The people holding slots, so a returning player can find their match; null when the host
    /// did not say (older versions).
    /// </summary>
    public IReadOnlyList<RoomMember>? Members { get; init; }

    /// <summary>Whether a spectator could join now; null when the host did not say (older versions).</summary>
    public bool? SpectatorsWelcome { get; init; }

    /// <summary>The host's rules fingerprint (<see cref="HostOptions.RulesFingerprint"/>); null when it has none.</summary>
    public string? RulesFingerprint { get; init; }

    /// <summary>Value equality, members included (room lists only report real changes).</summary>
    /// <param name="other">The other advertisement.</param>
    public bool Equals(RoomAdvertisement? other) =>
        other is not null
        && RoomId == other.RoomId
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(HostName, other.HostName, StringComparison.Ordinal)
        && Mode == other.Mode
        && Players == other.Players
        && MaxPlayers == other.MaxPlayers
        && Spectators == other.Spectators
        && string.Equals(GameVersion, other.GameVersion, StringComparison.Ordinal)
        && string.Equals(ContentVersion, other.ContentVersion, StringComparison.Ordinal)
        && InGame == other.InGame
        && IsOpen == other.IsOpen
        && JoinableInProgress == other.JoinableInProgress
        && SpectatorsWelcome == other.SpectatorsWelcome
        && string.Equals(RulesFingerprint, other.RulesFingerprint, StringComparison.Ordinal)
        && (Members is null ? other.Members is null : other.Members is not null && Members.SequenceEqual(other.Members));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RoomId);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Mode);
        hash.Add(Players);
        hash.Add(InGame);
        hash.Add(IsOpen);
        hash.Add(SpectatorsWelcome);
        hash.Add(Members?.Count ?? -1);
        return hash.ToHashCode();
    }
}

/// <summary>A room found by an <see cref="IRoomBrowser"/>.</summary>
public sealed record RoomInfo
{
    /// <summary>What to pass to <see cref="ITransport.Connect"/>.</summary>
    public required string Address { get; init; }

    /// <summary>The host's description of the room.</summary>
    public required RoomAdvertisement Room { get; init; }

    /// <summary>The host's network protocol version.</summary>
    public int ProtocolVersion { get; init; } = NetProtocol.Version;

    /// <summary>Round trip of the query that found the room; zero when unknown.</summary>
    public TimeSpan Ping { get; init; }

    /// <summary>
    /// How the player with secret key <paramref name="playerKey"/> stands in this room: their
    /// presence when they hold a slot (a returning or forfeited player), otherwise null (also when
    /// the host does not list its members).
    /// </summary>
    /// <param name="playerKey">This device's player key (<see cref="ClientOptions.PlayerId"/>).</param>
    public PlayerPresence? PresenceOf(Guid playerKey)
    {
        if (Room.Members is not { } members)
        {
            return null;
        }

        Guid publicId = PublicIdOf(playerKey);
        foreach (RoomMember member in members)
        {
            if (member.PlayerId == publicId)
            {
                return member.Presence;
            }
        }

        return null;
    }

    /// <summary>The public id the player with secret key <paramref name="playerKey"/> has in this room.</summary>
    /// <param name="playerKey">A player key (<see cref="ClientOptions.PlayerId"/>).</param>
    public Guid PublicIdOf(Guid playerKey) => SessionShared.PublicId(playerKey, Room.RoomId);

    /// <summary>Whether a client with these versions (and rules) may join.</summary>
    /// <param name="gameVersion">The client's game version.</param>
    /// <param name="contentVersion">The client's content version.</param>
    /// <param name="rulesFingerprint">
    /// The client's rules fingerprint, or null to leave it out. A room whose host has a fingerprint
    /// only matches the same one.
    /// </param>
    public bool IsCompatible(string gameVersion, string contentVersion, string? rulesFingerprint = null) =>
        ProtocolVersion == NetProtocol.Version
        && string.Equals(Room.GameVersion, gameVersion, StringComparison.Ordinal)
        && string.Equals(Room.ContentVersion, contentVersion, StringComparison.Ordinal)
        && (rulesFingerprint is null || Room.RulesFingerprint is null
            || string.Equals(Room.RulesFingerprint, rulesFingerprint, StringComparison.Ordinal));
}
