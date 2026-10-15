using System;

namespace Tribes.Net;

/// <summary>Why the host turned a join request down.</summary>
public enum RejectReason
{
    /// <summary>Not rejected.</summary>
    None = 0,

    /// <summary>The network protocol version differs from the host's.</summary>
    ProtocolMismatch = 1,

    /// <summary>The game version differs from the host's.</summary>
    GameVersionMismatch = 2,

    /// <summary>The content (data) version differs from the host's.</summary>
    ContentVersionMismatch = 3,

    /// <summary>No free slot (or spectator place) is left.</summary>
    RoomFull = 4,

    /// <summary>The match is running and does not accept new players.</summary>
    MatchInProgress = 5,

    /// <summary>The room does not allow spectators.</summary>
    SpectatorsNotAllowed = 6,

    /// <summary>The host removed this player from the room.</summary>
    Banned = 7,

    /// <summary>The join request was not valid (for example an empty player id).</summary>
    InvalidRequest = 8,

    /// <summary>The host is closing the room.</summary>
    HostClosing = 9,

    /// <summary>
    /// The game's rules differ from the host's (<see cref="HostOptions.RulesFingerprint"/>): a
    /// build with the same version numbers but other rules or data, which would not stay in sync.
    /// </summary>
    RulesMismatch = 10,
}

/// <summary>Why a client session ended.</summary>
public enum SessionEndReason
{
    /// <summary>The session has not ended.</summary>
    None = 0,

    /// <summary>The local player left.</summary>
    Left = 1,

    /// <summary>The host closed the room.</summary>
    HostClosed = 2,

    /// <summary>The connection to the host was lost and could not be restored.</summary>
    ConnectionLost = 3,

    /// <summary>The host removed this player.</summary>
    Kicked = 4,

    /// <summary>The host turned the join request down; see <see cref="SessionEndedEventArgs.Rejection"/>.</summary>
    Rejected = 5,

    /// <summary>A peer sent data that does not follow the format.</summary>
    ProtocolError = 6,

    /// <summary>The same player connected again from another connection.</summary>
    ReplacedByNewConnection = 7,

    /// <summary>
    /// The host left and this peer took over hosting the match; it goes on in the
    /// <see cref="HostSession"/> passed to <see cref="ClientSession.BecameHost"/>.
    /// </summary>
    BecameHost = 8,
}

/// <summary>The answer to a lobby or match request.</summary>
public enum RequestResult
{
    /// <summary>Done (for client requests: sent to the host, which may still deny it).</summary>
    Accepted = 0,

    /// <summary>Not possible in the current phase.</summary>
    NotAllowedNow = 1,

    /// <summary>The value is out of range.</summary>
    InvalidValue = 2,

    /// <summary>Another slot uses the colour.</summary>
    ColourTaken = 3,

    /// <summary>The participant has no slot or may not send commands.</summary>
    NotAPlayer = 4,

    /// <summary>A connected player is not ready.</summary>
    PlayersNotReady = 5,

    /// <summary>Too few slots are taken to start.</summary>
    NotEnoughPlayers = 6,

    /// <summary>Versus needs at least two teams.</summary>
    NeedTwoTeams = 7,

    /// <summary>No such participant or slot.</summary>
    NotFound = 8,

    /// <summary>The player already has the most commands allowed in that tick.</summary>
    TooManyCommands = 9,

    /// <summary>Messages are coming too fast.</summary>
    RateLimited = 10,
}

/// <summary>Which request a <see cref="RequestDeniedEventArgs"/> answers.</summary>
public enum RequestKind
{
    /// <summary>Choosing a colour.</summary>
    Colour = 0,

    /// <summary>Choosing a team.</summary>
    Team = 1,

    /// <summary>Saying ready or not ready.</summary>
    Ready = 2,

    /// <summary>A chat line.</summary>
    Chat = 3,

    /// <summary>A game command.</summary>
    Command = 4,

    /// <summary>Giving up.</summary>
    Forfeit = 5,

    /// <summary>Pausing or resuming.</summary>
    Pause = 6,

    /// <summary>Changing the game speed.</summary>
    Speed = 7,
}

/// <summary>Where a client session is.</summary>
public enum ClientPhase
{
    /// <summary>Created; <see cref="ClientSession.Connect"/> not called yet.</summary>
    Idle = 0,

    /// <summary>Connecting and waiting for the host's answer.</summary>
    Connecting = 1,

    /// <summary>In the room before the match.</summary>
    Lobby = 2,

    /// <summary>Receiving the match state to join a running match.</summary>
    Loading = 3,

    /// <summary>Running the match.</summary>
    InGame = 4,

    /// <summary>The connection dropped during the match; trying again.</summary>
    Reconnecting = 5,

    /// <summary>Over; see <see cref="ClientSession.EndReason"/>.</summary>
    Ended = 6,

    /// <summary>The host left during the match; connecting to the player who takes over.</summary>
    Migrating = 7,
}

/// <summary>Settings of a <see cref="HostSession"/>.</summary>
public sealed record HostOptions
{
    /// <summary>The game version; clients with another version are rejected.</summary>
    public required string GameVersion { get; init; }

    /// <summary>The content (data) version; clients with another version are rejected.</summary>
    public required string ContentVersion { get; init; }

    /// <summary>
    /// A fingerprint of the game's rules and data as this build runs them (up to
    /// <see cref="RoomLimits.MaxVersionLength"/> characters). When set, players and spectators
    /// whose fingerprint differs are rejected with <see cref="RejectReason.RulesMismatch"/>, and
    /// the room's advertisement carries it so room lists can mark the room. Empty: not checked.
    /// </summary>
    public string RulesFingerprint { get; init; } = string.Empty;

    /// <summary>The host player's persistent id.</summary>
    public required Guid PlayerId { get; init; }

    /// <summary>The host player's name.</summary>
    public required string PlayerName { get; init; }

    /// <summary>Colour the host wants, or -1 for the first free one.</summary>
    public int PreferredColour { get; init; } = -1;

    /// <summary>The room to create.</summary>
    public RoomSettings Room { get; init; } = new();

    /// <summary>Lockstep timing and limits.</summary>
    public LockstepOptions Lockstep { get; init; } = new();

    /// <summary>A connection that does not introduce itself within this time is closed.</summary>
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// After taking over a match from a host that left, how long the players who have not come
    /// back yet stay active before they count as absent.
    /// </summary>
    public TimeSpan MigrationGracePeriod { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Settings of a <see cref="ClientSession"/>.</summary>
public sealed record ClientOptions
{
    /// <summary>The game version; must equal the host's.</summary>
    public required string GameVersion { get; init; }

    /// <summary>The content (data) version; must equal the host's.</summary>
    public required string ContentVersion { get; init; }

    /// <summary>The fingerprint of this build's rules (see <see cref="HostOptions.RulesFingerprint"/>); must equal the host's when the host sets one.</summary>
    public string RulesFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// The player's persistent id. Keep it on the device: the host recognises a returning player
    /// by it.
    /// </summary>
    public required Guid PlayerId { get; init; }

    /// <summary>The player's name.</summary>
    public required string PlayerName { get; init; }

    /// <summary>Join without a slot, only to watch.</summary>
    public bool Spectate { get; init; }

    /// <summary>Colour the player wants, or -1 for the first free one.</summary>
    public int PreferredColour { get; init; } = -1;

    /// <summary>
    /// Input delay bounds, catch-up and size limits for this client. The tick length and hash
    /// interval come from the host.
    /// </summary>
    public LockstepOptions Lockstep { get; init; } = new();

    /// <summary>How often to try again when the connection drops during a match.</summary>
    public int ReconnectAttempts { get; init; } = 3;

    /// <summary>Wait before each reconnect attempt.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// When the host leaves during a match: how long to keep trying to reach the player who takes
    /// over before trying the next one in line.
    /// </summary>
    public TimeSpan MigrationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>If this peer takes over hosting: how long the others may take to come back before they count as absent.</summary>
    public TimeSpan MigrationGracePeriod { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Summary of a saved match, for a load menu.</summary>
public sealed record SavedMatchInfo
{
    /// <summary>Id of the match.</summary>
    public required Guid MatchId { get; init; }

    /// <summary>The room as it was saved.</summary>
    public required RoomState Room { get; init; }

    /// <summary>The tick the match continues from.</summary>
    public required long Tick { get; init; }

    /// <summary>Game version that made the save.</summary>
    public required string GameVersion { get; init; }

    /// <summary>Content version that made the save.</summary>
    public required string ContentVersion { get; init; }
}
