using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport;

namespace Tribes.Client.Ui.Logic;

/// <summary>What the player can do in a listed room.</summary>
public enum RoomAction
{
    /// <summary>Nothing: the room runs another version of the game.</summary>
    None,

    /// <summary>Take a free slot: a room in its lobby, or an endless match with invites open.</summary>
    Join,

    /// <summary>Come back to this player's own slot and tribe in a running match.</summary>
    Return,

    /// <summary>Watch a match this player forfeited (the host lets them in as themselves, watching).</summary>
    Watch,

    /// <summary>Watch as a new spectator.</summary>
    Spectate,

    /// <summary>The host is an older version that does not say who is in the match: connect and let it decide.</summary>
    Connect,
}

/// <summary>A room as the LAN list shows it.</summary>
/// <param name="Room">The room.</param>
/// <param name="Action">What this device's player can do there.</param>
/// <param name="InMatch">Whether its match is running (shown with the 「對局中」 badge).</param>
/// <param name="Compatible">Whether it runs the same game and content version.</param>
public sealed record RoomListEntry(RoomInfo Room, RoomAction Action, bool InMatch, bool Compatible)
{
    /// <summary>Whether the button does anything.</summary>
    public bool CanAct => Action != RoomAction.None;

    /// <summary>Whether to connect as a spectator (only new spectators; known players connect as themselves).</summary>
    public bool ConnectsAsSpectator => Action == RoomAction.Spectate;

    /// <summary>The text key of the room's button.</summary>
    public string ActionKey => Action switch
    {
        RoomAction.Return => "multiplayer.return",
        RoomAction.Watch or RoomAction.Spectate => "multiplayer.spectate",
        RoomAction.Connect => "multiplayer.connect_room",
        _ => "multiplayer.join",
    };

    /// <summary>The text key of the room's badge, or null for rooms in their lobby.</summary>
    public string? BadgeKey => InMatch ? "multiplayer.badge_in_match" : null;

    /// <summary>The text key of the line saying what the action means, or null.</summary>
    public string? NoteKey => Action switch
    {
        RoomAction.Return => "multiplayer.note_return",
        RoomAction.Watch => "multiplayer.note_forfeited",
        RoomAction.Spectate when InMatch => "multiplayer.note_spectate",
        RoomAction.Join when InMatch => "multiplayer.note_join_in_match",
        RoomAction.None => "multiplayer.incompatible",
        _ => null,
    };
}

/// <summary>
/// Decides which rooms the LAN list shows and what the player can do in each. Rooms in a match
/// are shown when the player can come back to their tribe, join (endless rooms with invites
/// open) or watch; rooms that allow none of these are left out.
/// </summary>
public static class RoomListing
{
    /// <summary>
    /// The entry for <paramref name="room"/> as seen by the player with key
    /// <paramref name="playerKey"/>, or null when the room is not shown. A room whose host runs
    /// other rules (another <paramref name="rulesFingerprint"/>) counts as another version.
    /// </summary>
    public static RoomListEntry? Classify(RoomInfo room, Guid playerKey, string gameVersion, string contentVersion, string? rulesFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(room);
        RoomAdvertisement advert = room.Room;
        bool inMatch = advert.InGame;
        if (!room.IsCompatible(gameVersion, contentVersion, rulesFingerprint))
        {
            return new RoomListEntry(room, RoomAction.None, inMatch, Compatible: false);
        }
        RoomAction? action = inMatch ? InMatch(room, playerKey) : InLobby(advert);
        return action is { } chosen ? new RoomListEntry(room, chosen, inMatch, Compatible: true) : null;
    }

    /// <summary>The shown rooms: matches this player is in first, then the others in the given order.</summary>
    public static IReadOnlyList<RoomListEntry> Build(IEnumerable<RoomInfo> rooms, Guid playerKey, string gameVersion, string contentVersion, string? rulesFingerprint = null) =>
        [.. rooms
            .Select(room => Classify(room, playerKey, gameVersion, contentVersion, rulesFingerprint))
            .OfType<RoomListEntry>()
            .Select((entry, index) => (entry, index))
            .OrderBy(e => e.entry.Action is RoomAction.Return ? 0 : 1)
            .ThenBy(e => e.index)
            .Select(e => e.entry)];

    private static RoomAction? InLobby(RoomAdvertisement advert)
    {
        if (advert.IsOpen)
        {
            return RoomAction.Join;
        }
        return advert.SpectatorsWelcome == true ? RoomAction.Spectate : null;
    }

    private static RoomAction? InMatch(RoomInfo room, Guid playerKey)
    {
        RoomAdvertisement advert = room.Room;
        switch (room.PresenceOf(playerKey))
        {
            case PlayerPresence.Active or PlayerPresence.Absent:
                return RoomAction.Return;
            case PlayerPresence.Forfeited:
                return RoomAction.Watch;
        }
        if (advert.JoinableInProgress && advert.IsOpen)
        {
            return RoomAction.Join;
        }
        if (advert.SpectatorsWelcome == true)
        {
            return RoomAction.Spectate;
        }
        // An older host lists nobody: it may still be this player's match, so let it decide.
        return advert.Members is null ? RoomAction.Connect : null;
    }
}
