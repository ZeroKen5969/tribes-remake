using System;
using System.Collections.Generic;
using System.Text;

namespace Tribes.Net;

/// <summary>The kind of match a room plays.</summary>
public enum RoomMode
{
    /// <summary>Teams fight until one side remains; new players join only before the start.</summary>
    Versus = 0,

    /// <summary>An open-ended world; the host can let players join while the match runs.</summary>
    Endless = 1,
}

/// <summary>Where a room is in its life.</summary>
public enum RoomPhase
{
    /// <summary>Players gather, pick colours and teams, and get ready.</summary>
    Lobby = 0,

    /// <summary>The match is running.</summary>
    InGame = 1,

    /// <summary>The host closed the room.</summary>
    Closed = 2,
}

/// <summary>Who controls a tribe slot.</summary>
public enum PlayerKind
{
    /// <summary>A person on a connected device.</summary>
    Human = 0,

    /// <summary>The computer AI, added by the host.</summary>
    Computer = 1,
}

/// <summary>The state the host keeps for every participant.</summary>
public enum PlayerPresence
{
    /// <summary>Playing: connected and sending commands for the slot.</summary>
    Active = 0,

    /// <summary>
    /// Disconnected or left during the match; the tribe stays in the world without orders (the
    /// computer does not play it) until the player returns with the same player id.
    /// </summary>
    Absent = 1,

    /// <summary>Gave up or was defeated; final for the match. May keep watching.</summary>
    Forfeited = 2,

    /// <summary>Watches without a slot.</summary>
    Spectator = 3,
}

/// <summary>The fixed limits of a room.</summary>
public static class RoomLimits
{
    /// <summary>Tribe slots in a room (people and computer players together).</summary>
    public const int MaxPlayers = 4;

    /// <summary>
    /// Tribe colours, ids 0 to 7. The colour also sets the tribe's skin tone, and no two slots
    /// may use the same one.
    /// </summary>
    public const int ColourCount = 8;

    /// <summary>Teams in versus mode, ids 0 to 3.</summary>
    public const int MaxTeams = 4;

    /// <summary>Spectators a room can hold at most.</summary>
    public const int MaxSpectators = 8;

    /// <summary>Characters kept of a player name.</summary>
    public const int MaxNameLength = 24;

    /// <summary>Characters kept of a room name.</summary>
    public const int MaxRoomNameLength = 32;

    /// <summary>Characters kept of a chat line.</summary>
    public const int MaxChatLength = 200;

    /// <summary>Characters allowed in a game or content version string.</summary>
    public const int MaxVersionLength = 32;

    /// <summary>Slowest game speed, in percent of normal.</summary>
    public const int MinSpeedPercent = 25;

    /// <summary>Fastest game speed, in percent of normal.</summary>
    public const int MaxSpeedPercent = 400;

    /// <summary>Whether <paramref name="colour"/> is one of the colour ids.</summary>
    /// <param name="colour">A colour id.</param>
    public static bool IsValidColour(int colour) => colour is >= 0 and < ColourCount;

    /// <summary>Whether <paramref name="team"/> is one of the team ids.</summary>
    /// <param name="team">A team id.</param>
    public static bool IsValidTeam(int team) => team is >= 0 and < MaxTeams;

    /// <summary>Whether <paramref name="speedPercent"/> is an allowed game speed.</summary>
    /// <param name="speedPercent">Speed in percent of normal.</param>
    public static bool IsValidSpeed(int speedPercent) =>
        speedPercent is >= MinSpeedPercent and <= MaxSpeedPercent;

    /// <summary>
    /// Text as a single line: control characters removed, surrounding spaces trimmed, cut to
    /// <paramref name="maxLength"/> characters without splitting a surrogate pair.
    /// </summary>
    internal static string CleanText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (char c in text.Trim())
        {
            if (builder.Length >= maxLength)
            {
                break;
            }

            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        if (builder.Length > 0 && char.IsHighSurrogate(builder[^1]))
        {
            builder.Length--;
        }

        return builder.ToString().Trim();
    }
}

/// <summary>A participant of a room: a person in a slot, a computer player, or a spectator.</summary>
public sealed record PlayerInfo
{
    /// <summary>
    /// The persistent id. People choose it on their device and keep it across reconnects; the
    /// host makes one up for computer players.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>The tribe slot, 0 to <see cref="RoomLimits.MaxPlayers"/> - 1, or -1 for a spectator.</summary>
    public int Slot { get; init; } = -1;

    /// <summary>Person or computer.</summary>
    public PlayerKind Kind { get; init; }

    /// <summary>Colour id 0 to 7 for slot holders, -1 for spectators.</summary>
    public int Colour { get; init; } = -1;

    /// <summary>Team id; in versus mode slots with the same team fight together.</summary>
    public int Team { get; init; }

    /// <summary>Whether the player said they are ready to start (lobby only).</summary>
    public bool IsReady { get; init; }

    /// <summary>The state the host keeps for the participant.</summary>
    public PlayerPresence Presence { get; init; }

    /// <summary>Whether the participant's device is connected right now.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Whether this is the host.</summary>
    public bool IsHost { get; init; }

    /// <summary>Strength of a computer player; the game decides what the numbers mean.</summary>
    public int AiLevel { get; init; }

    /// <summary>Whether the participant holds a tribe slot.</summary>
    public bool HasSlot => Slot >= 0;

    /// <summary>Whether the participant may send game commands now.</summary>
    public bool CanSendCommands =>
        HasSlot && Kind == PlayerKind.Human && Presence == PlayerPresence.Active;
}

/// <summary>The settings the host chooses when creating a room.</summary>
public sealed record RoomSettings
{
    /// <summary>Name shown in room lists.</summary>
    public string Name { get; init; } = "Tribes";

    /// <summary>Versus or endless.</summary>
    public RoomMode Mode { get; init; } = RoomMode.Versus;

    /// <summary>Tribe slots, 1 to <see cref="RoomLimits.MaxPlayers"/>.</summary>
    public int MaxPlayers { get; init; } = RoomLimits.MaxPlayers;

    /// <summary>
    /// Endless mode only: whether new players may join while the match runs. The host can change
    /// it during the match with <see cref="HostSession.SetJoinInProgress"/>.
    /// </summary>
    public bool AllowJoinInProgress { get; init; } = true;

    /// <summary>Whether people may join without a slot to watch.</summary>
    public bool AllowSpectators { get; init; } = true;

    /// <summary>Spectators allowed at once, 0 to <see cref="RoomLimits.MaxSpectators"/>.</summary>
    public int MaxSpectators { get; init; } = 4;

    /// <summary>Whether players may pause and resume the match.</summary>
    public bool AllowClientPause { get; init; } = true;

    /// <summary>
    /// Whether speed requests from players apply at once. When false the host decides through
    /// <see cref="HostSession.SpeedRequested"/>.
    /// </summary>
    public bool AcceptSpeedRequests { get; init; }

    /// <summary>What is wrong with the settings, or null when they are usable.</summary>
    internal string? FindProblem()
    {
        if (MaxPlayers is < 1 or > RoomLimits.MaxPlayers)
        {
            return $"MaxPlayers must be between 1 and {RoomLimits.MaxPlayers}.";
        }

        if (MaxSpectators is < 0 or > RoomLimits.MaxSpectators)
        {
            return $"MaxSpectators must be between 0 and {RoomLimits.MaxSpectators}.";
        }

        if (!Enum.IsDefined(Mode))
        {
            return "Mode is not a room mode.";
        }

        return null;
    }
}

/// <summary>Everything a peer knows about a room; the host sends a new one after every change.</summary>
public sealed record RoomState
{
    /// <summary>Id of the room, made when it was created; stays the same in a resumed match.</summary>
    public required Guid RoomId { get; init; }

    /// <summary>The host's settings.</summary>
    public required RoomSettings Settings { get; init; }

    /// <summary>Lobby, in game or closed.</summary>
    public RoomPhase Phase { get; init; }

    /// <summary>All participants: slot holders first in slot order, then spectators.</summary>
    public required IReadOnlyList<PlayerInfo> Players { get; init; }

    /// <summary>Counts up with every change, so a newer state is easy to tell from an older one.</summary>
    public int Revision { get; init; }

    /// <summary>Number of slots taken by people or computer players.</summary>
    public int OccupiedSlots
    {
        get
        {
            int count = 0;
            foreach (PlayerInfo player in Players)
            {
                if (player.HasSlot)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Number of participants without a slot.</summary>
    public int SpectatorCount => Players.Count - OccupiedSlots;

    /// <summary>The participant with the given id, or null.</summary>
    /// <param name="playerId">A persistent player id.</param>
    public PlayerInfo? FindPlayer(Guid playerId)
    {
        foreach (PlayerInfo player in Players)
        {
            if (player.Id == playerId)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>The holder of a slot, or null when the slot is free.</summary>
    /// <param name="slot">A slot index.</param>
    public PlayerInfo? PlayerInSlot(int slot)
    {
        foreach (PlayerInfo player in Players)
        {
            if (player.HasSlot && player.Slot == slot)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>Whether a slot holder other than <paramref name="exceptPlayer"/> uses the colour.</summary>
    /// <param name="colour">A colour id.</param>
    /// <param name="exceptPlayer">A player whose own colour does not count.</param>
    public bool IsColourTaken(int colour, Guid exceptPlayer)
    {
        foreach (PlayerInfo player in Players)
        {
            if (player.HasSlot && player.Colour == colour && player.Id != exceptPlayer)
            {
                return true;
            }
        }

        return false;
    }
}
