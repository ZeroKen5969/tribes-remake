using System;
using System.Collections.Generic;
using System.Linq;

namespace Tribes.Net;

internal enum AdmitOutcome
{
    Rejected,

    /// <summary>A new player took a slot in the lobby.</summary>
    Joined,

    /// <summary>A new player took a slot of a running endless match.</summary>
    JoinedInProgress,

    /// <summary>An absent player came back to their slot.</summary>
    Returned,

    /// <summary>A known participant connected again and keeps their state.</summary>
    Reconnected,

    /// <summary>A new spectator.</summary>
    Spectating,
}

internal readonly record struct AdmitResult(AdmitOutcome Outcome, PlayerInfo? Player, RejectReason Reason, string Message)
{
    public static AdmitResult Reject(RejectReason reason, string message) => new(AdmitOutcome.Rejected, null, reason, message);
}

/// <summary>
/// The host's roster and the lobby rules: slots, unique colours, teams, readiness, computer
/// players, admission of new and returning players. Every change replaces the changed
/// <see cref="PlayerInfo"/> record and bumps the revision.
/// </summary>
internal sealed class HostRoom
{
    private readonly List<PlayerInfo> players = [];
    private readonly HashSet<Guid> banned = [];
    private RoomState? cached;
    private int revision;

    public HostRoom(Guid roomId, RoomSettings settings, Guid hostId, string hostName, int preferredColour)
    {
        RoomId = roomId;
        Settings = settings;
        players.Add(new PlayerInfo
        {
            Id = hostId,
            Name = hostName,
            Slot = 0,
            Colour = RoomLimits.IsValidColour(preferredColour) ? preferredColour : 0,
            Team = 0,
            Presence = PlayerPresence.Active,
            IsConnected = true,
            IsHost = true,
            IsReady = true,
        });
    }

    private HostRoom(RoomState saved, IEnumerable<Guid> bannedPlayers)
    {
        RoomId = saved.RoomId;
        Settings = saved.Settings;
        Phase = saved.Phase;
        revision = saved.Revision;
        players.AddRange(saved.Players);
        banned.UnionWith(bannedPlayers);
    }

    public Guid RoomId { get; }

    public RoomSettings Settings { get; private set; }

    public RoomPhase Phase { get; private set; }

    public IReadOnlyCollection<Guid> Banned => banned;

    public RoomState State => cached ??= new RoomState
    {
        RoomId = RoomId,
        Settings = Settings,
        Phase = Phase,
        Revision = revision,
        Players = [.. players.OrderBy(p => p.HasSlot ? p.Slot : RoomLimits.MaxPlayers)],
    };

    public PlayerInfo Host => players.First(p => p.IsHost);

    /// <summary>
    /// The room of a saved match, as it continues: the saving host is connected again, everyone
    /// else is disconnected, and players who were active are absent until they return.
    /// </summary>
    public static HostRoom Restore(RoomState saved, IEnumerable<Guid> bannedPlayers, Guid hostId, out List<int> newlyAbsentSlots)
    {
        newlyAbsentSlots = [];
        var room = new HostRoom(saved, bannedPlayers);
        foreach (PlayerInfo player in room.players.ToArray())
        {
            if (player.IsHost)
            {
                room.Replace(player, player with { IsConnected = true });
            }
            else if (player.Kind == PlayerKind.Human && !player.HasSlot)
            {
                room.players.Remove(player);
            }
            else if (player.Kind == PlayerKind.Human)
            {
                bool wasActive = player.Presence == PlayerPresence.Active;
                room.Replace(player, player with
                {
                    IsConnected = false,
                    Presence = wasActive ? PlayerPresence.Absent : player.Presence,
                });
                if (wasActive)
                {
                    newlyAbsentSlots.Add(player.Slot);
                }
            }
        }

        if (room.Host.Id != hostId)
        {
            throw new ArgumentException("Only the player who hosted the match can resume it.", nameof(hostId));
        }

        room.Touch();
        return room;
    }

    /// <summary>
    /// The room a client rebuilds when it takes over hosting. Slot holders and their presence come
    /// from the ledger (what every simulation has; a change the old host had queued but not sent
    /// in a batch is not in it), names and colours from the old host's last room update. Only the
    /// new host is connected; the others come back by reconnecting.
    /// </summary>
    public static HostRoom ForSuccessor(RoomState latest, IEnumerable<Guid> bannedPlayers, MatchLedger ledger, Guid selfId)
    {
        var rebuilt = new List<PlayerInfo>();
        foreach (PlayerInfo entry in ledger.Roster)
        {
            PlayerInfo basis = latest.PlayerInSlot(entry.Slot) is { } known && known.Id == entry.Id ? known : entry;
            bool self = entry.Id == selfId;
            rebuilt.Add(basis with { Presence = entry.Presence, IsHost = self, IsConnected = self, IsReady = false });
        }

        if (!rebuilt.Exists(p => p.Id == selfId) && latest.FindPlayer(selfId) is { } watcher)
        {
            rebuilt.Add(watcher with { IsHost = true, IsConnected = true });
        }

        var state = new RoomState
        {
            RoomId = latest.RoomId,
            Settings = latest.Settings,
            Phase = RoomPhase.InGame,
            Revision = latest.Revision + 1,
            Players = rebuilt,
        };
        return new HostRoom(state, bannedPlayers);
    }

    public PlayerInfo? Find(Guid id) => players.Find(p => p.Id == id);

    public PlayerInfo? InSlot(int slot) => players.Find(p => p.HasSlot && p.Slot == slot);

    public AdmitResult Admit(Guid id, string name, bool spectate, int preferredColour)
    {
        if (banned.Contains(id))
        {
            return AdmitResult.Reject(RejectReason.Banned, "The host removed you from this room.");
        }

        PlayerInfo? known = Find(id);
        if (known is not null)
        {
            return AdmitKnown(known, name);
        }

        if (spectate)
        {
            return AdmitSpectator(id, name);
        }

        if (Phase == RoomPhase.InGame
            && (Settings.Mode != RoomMode.Endless || !Settings.AllowJoinInProgress))
        {
            return AdmitResult.Reject(RejectReason.MatchInProgress, "The match is running and does not accept new players.");
        }

        int slot = FreeSlot();
        if (slot < 0)
        {
            return AdmitResult.Reject(RejectReason.RoomFull, $"All {Settings.MaxPlayers} slots are taken.");
        }

        var player = new PlayerInfo
        {
            Id = id,
            Name = name,
            Slot = slot,
            Colour = ColourFor(preferredColour),
            Team = slot,
            Presence = PlayerPresence.Active,
            IsConnected = true,
        };
        players.Add(player);
        Touch();
        return new AdmitResult(Phase == RoomPhase.InGame ? AdmitOutcome.JoinedInProgress : AdmitOutcome.Joined, player, RejectReason.None, string.Empty);
    }

    public RequestResult ChooseColour(Guid id, int colour)
    {
        if (Phase != RoomPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        if (!RoomLimits.IsValidColour(colour))
        {
            return RequestResult.InvalidValue;
        }

        PlayerInfo? player = Find(id);
        if (player is not { HasSlot: true })
        {
            return RequestResult.NotAPlayer;
        }

        if (State.IsColourTaken(colour, id))
        {
            return RequestResult.ColourTaken;
        }

        Replace(player, player with { Colour = colour });
        return RequestResult.Accepted;
    }

    public RequestResult ChooseTeam(Guid id, int team)
    {
        if (Phase != RoomPhase.Lobby || Settings.Mode != RoomMode.Versus)
        {
            return RequestResult.NotAllowedNow;
        }

        if (!RoomLimits.IsValidTeam(team))
        {
            return RequestResult.InvalidValue;
        }

        PlayerInfo? player = Find(id);
        if (player is not { HasSlot: true })
        {
            return RequestResult.NotAPlayer;
        }

        Replace(player, player with { Team = team });
        return RequestResult.Accepted;
    }

    public RequestResult SetReady(Guid id, bool ready)
    {
        if (Phase != RoomPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        PlayerInfo? player = Find(id);
        if (player is not { HasSlot: true })
        {
            return RequestResult.NotAPlayer;
        }

        Replace(player, player with { IsReady = ready });
        return RequestResult.Accepted;
    }

    /// <summary>Adds a computer player to the first free slot; returns the slot or -1.</summary>
    public int AddComputer(int team, int aiLevel)
    {
        int slot = FreeSlot();
        if (Phase != RoomPhase.Lobby || slot < 0)
        {
            return -1;
        }

        players.Add(new PlayerInfo
        {
            Id = Guid.NewGuid(),
            Name = "Computer " + (slot + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Slot = slot,
            Kind = PlayerKind.Computer,
            Colour = ColourFor(-1),
            Team = RoomLimits.IsValidTeam(team) ? team : slot,
            AiLevel = Math.Clamp(aiLevel, 0, 255),
            IsReady = true,
            Presence = PlayerPresence.Active,
            IsConnected = true,
        });
        Touch();
        return slot;
    }

    public RequestResult ConfigureComputer(int slot, int colour, int team, int aiLevel)
    {
        if (Phase != RoomPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        PlayerInfo? player = InSlot(slot);
        if (player is not { Kind: PlayerKind.Computer })
        {
            return RequestResult.NotFound;
        }

        if (!RoomLimits.IsValidColour(colour) || !RoomLimits.IsValidTeam(team) || aiLevel is < 0 or > 255)
        {
            return RequestResult.InvalidValue;
        }

        if (State.IsColourTaken(colour, player.Id))
        {
            return RequestResult.ColourTaken;
        }

        Replace(player, player with { Colour = colour, Team = team, AiLevel = aiLevel });
        return RequestResult.Accepted;
    }

    public RequestResult RemoveComputer(int slot)
    {
        if (Phase != RoomPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        PlayerInfo? player = InSlot(slot);
        if (player is not { Kind: PlayerKind.Computer })
        {
            return RequestResult.NotFound;
        }

        players.Remove(player);
        Touch();
        return RequestResult.Accepted;
    }

    public RequestResult CanStart()
    {
        if (Phase != RoomPhase.Lobby)
        {
            return RequestResult.NotAllowedNow;
        }

        List<PlayerInfo> slotHolders = players.FindAll(p => p.HasSlot);
        if (slotHolders.Count < (Settings.Mode == RoomMode.Versus ? 2 : 1))
        {
            return RequestResult.NotEnoughPlayers;
        }

        if (slotHolders.Exists(p => p.Kind == PlayerKind.Human && !p.IsHost && !p.IsReady))
        {
            return RequestResult.PlayersNotReady;
        }

        if (Settings.Mode == RoomMode.Versus && slotHolders.Select(p => p.Team).Distinct().Count() < 2)
        {
            return RequestResult.NeedTwoTeams;
        }

        return RequestResult.Accepted;
    }

    public void BeginMatch()
    {
        Phase = RoomPhase.InGame;
        Touch();
    }

    public void Close()
    {
        Phase = RoomPhase.Closed;
        Touch();
    }

    public void SetJoinInProgress(bool allowed)
    {
        Settings = Settings with { AllowJoinInProgress = allowed };
        Touch();
    }

    public void Remove(Guid id)
    {
        if (players.RemoveAll(p => p.Id == id && !p.IsHost) > 0)
        {
            Touch();
        }
    }

    public void SetConnected(Guid id, bool connected)
    {
        if (Find(id) is { } player && player.IsConnected != connected)
        {
            Replace(player, player with { IsConnected = connected });
        }
    }

    public void SetPresence(Guid id, PlayerPresence presence)
    {
        if (Find(id) is { } player && player.Presence != presence)
        {
            Replace(player, player with { Presence = presence });
        }
    }

    public void Ban(Guid id) => banned.Add(id);

    private AdmitResult AdmitKnown(PlayerInfo known, string name)
    {
        if (known.IsHost || known.Kind == PlayerKind.Computer)
        {
            return AdmitResult.Reject(RejectReason.InvalidRequest, "That player id belongs to another participant.");
        }

        bool returning = Phase == RoomPhase.InGame && known.HasSlot && known.Presence == PlayerPresence.Absent;
        PlayerInfo updated = known with
        {
            Name = name,
            IsConnected = true,
            Presence = returning ? PlayerPresence.Active : known.Presence,
        };
        Replace(known, updated);
        return new AdmitResult(returning ? AdmitOutcome.Returned : AdmitOutcome.Reconnected, updated, RejectReason.None, string.Empty);
    }

    private AdmitResult AdmitSpectator(Guid id, string name)
    {
        if (!Settings.AllowSpectators)
        {
            return AdmitResult.Reject(RejectReason.SpectatorsNotAllowed, "This room does not allow spectators.");
        }

        if (State.SpectatorCount >= Settings.MaxSpectators)
        {
            return AdmitResult.Reject(RejectReason.RoomFull, "No spectator place is left.");
        }

        var spectator = new PlayerInfo
        {
            Id = id,
            Name = name,
            Presence = PlayerPresence.Spectator,
            IsConnected = true,
        };
        players.Add(spectator);
        Touch();
        return new AdmitResult(AdmitOutcome.Spectating, spectator, RejectReason.None, string.Empty);
    }

    private int FreeSlot()
    {
        for (int slot = 0; slot < Settings.MaxPlayers; slot++)
        {
            if (InSlot(slot) is null)
            {
                return slot;
            }
        }

        return -1;
    }

    private int ColourFor(int preferred)
    {
        RoomState state = State;
        if (RoomLimits.IsValidColour(preferred) && !state.IsColourTaken(preferred, Guid.Empty))
        {
            return preferred;
        }

        for (int colour = 0; colour < RoomLimits.ColourCount; colour++)
        {
            if (!state.IsColourTaken(colour, Guid.Empty))
            {
                return colour;
            }
        }

        throw new InvalidOperationException("More slots than colours.");
    }

    private void Replace(PlayerInfo current, PlayerInfo updated)
    {
        players[players.IndexOf(current)] = updated;
        Touch();
    }

    private void Touch()
    {
        revision++;
        cached = null;
    }
}
