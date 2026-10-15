using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>Where a participant of a match stands.</summary>
public enum PlayerState : byte
{
    /// <summary>Plays: the tribe takes the player's orders (computer tribes are always playing).</summary>
    Playing = 0,

    /// <summary>Disconnected or left; the tribe gets no orders (and no computer help) until the player returns.</summary>
    Absent = 1,

    /// <summary>Gave up; final for the match. The player may watch.</summary>
    Forfeited = 2,

    /// <summary>The tribe was wiped out; final for the match. The player may watch.</summary>
    Out = 3,

    /// <summary>Watches without a tribe.</summary>
    Spectator = 4,
}

/// <summary>One participant of a match as every peer's rules know it.</summary>
public sealed record PlayerEntry
{
    /// <summary>The persistent public id (the room's id of the player; a returning player keeps it).</summary>
    public required Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Room slot 0..3, or -1 without a slot.</summary>
    public int Slot { get; init; } = -1;

    /// <summary>The tribe the player plays, or -1 when the world has no tribe for the slot.</summary>
    public int Tribe { get; init; } = -1;

    /// <summary>Tribe colour 0..7, or -1 for the mission's own look.</summary>
    public int Colour { get; init; } = -1;

    /// <summary>Team; tribes of one team are allies.</summary>
    public int Team { get; init; }

    /// <summary>Whether the computer plays the tribe.</summary>
    public bool IsComputer { get; init; }

    /// <summary>Computer strength (computer tribes only).</summary>
    public int AiLevel { get; init; }

    /// <summary>Where the participant stands.</summary>
    public PlayerState State { get; init; }

    /// <summary>Whether the participant holds a slot.</summary>
    public bool HasSlot => Slot >= 0;

    /// <summary>Whether orders from this participant reach a tribe now.</summary>
    public bool TakesOrders => !IsComputer && State == PlayerState.Playing && Tribe >= 0;

    /// <summary>Whether the participant's game is over for good (forfeited or wiped out).</summary>
    public bool IsFinished => State is PlayerState.Forfeited or PlayerState.Out;

    /// <summary>The entry of a room participant at the start of a match.</summary>
    /// <param name="player">The participant from the match start.</param>
    /// <param name="tribe">The tribe of its slot, or -1.</param>
    public static PlayerEntry From(PlayerInfo player, int tribe)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new PlayerEntry
        {
            Id = player.Id,
            Name = player.Name,
            Slot = player.Slot,
            Tribe = player.HasSlot ? tribe : -1,
            Colour = player.Colour,
            Team = player.Team,
            IsComputer = player.Kind == PlayerKind.Computer,
            AiLevel = player.AiLevel,
            State = StateOf(player),
        };
    }

    private static PlayerState StateOf(PlayerInfo player)
    {
        if (!player.HasSlot)
        {
            return PlayerState.Spectator;
        }
        return player.Presence switch
        {
            PlayerPresence.Absent => PlayerState.Absent,
            PlayerPresence.Forfeited => PlayerState.Forfeited,
            _ => PlayerState.Playing,
        };
    }
}

/// <summary>
/// The participants of a match: slot holders in slot order, then spectators and the world's
/// computer tribes (any number). Immutable; every change makes a new table. It is part of the
/// match state, so it is saved with the world and changes only through tick commands (identically
/// on every peer).
/// </summary>
public sealed class PlayerTable : IReadOnlyList<PlayerEntry>
{
    private const int MaxNameBytes = 256;

    /// <summary>The fewest bytes an entry takes in <see cref="Write"/> (an empty name).</summary>
    private const int MinEntryBytes = 16 + 1 + (5 * sizeof(int)) + sizeof(bool) + sizeof(byte);

    private readonly PlayerEntry[] entries;
    private Dictionary<Guid, PlayerEntry>? byId;
    private Dictionary<int, PlayerEntry>? byTribe;
    private uint? digest;

    private PlayerTable(PlayerEntry[] entries) => this.entries = entries;

    /// <summary>A table without participants.</summary>
    public static PlayerTable Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => entries.Length;

    /// <inheritdoc />
    public PlayerEntry this[int index] => entries[index];

    /// <summary>A table of <paramref name="players"/>, sorted (slot holders by slot, then the others in their order).</summary>
    /// <exception cref="ArgumentException">Two entries share an id or a slot.</exception>
    public static PlayerTable Of(IEnumerable<PlayerEntry> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var list = new List<PlayerEntry>(players);
        var ids = new HashSet<Guid>();
        var slots = new HashSet<int>();
        foreach (PlayerEntry entry in list)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!ids.Add(entry.Id) || (entry.HasSlot && !slots.Add(entry.Slot)))
            {
                throw new ArgumentException($"Participant {entry.Name} shares an id or slot with another.", nameof(players));
            }
        }
        var sorted = new List<PlayerEntry>(list.Count);
        sorted.AddRange(list.FindAll(e => e.HasSlot));
        sorted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        sorted.AddRange(list.FindAll(e => !e.HasSlot));
        return new PlayerTable([.. sorted]);
    }

    /// <summary>The holder of <paramref name="slot"/>, or null.</summary>
    public PlayerEntry? BySlot(int slot) => Array.Find(entries, e => e.HasSlot && e.Slot == slot);

    /// <summary>The participant with <paramref name="id"/>, or null.</summary>
    public PlayerEntry? ById(Guid id) => IdIndex.GetValueOrDefault(id);

    /// <summary>The participant playing <paramref name="tribe"/>, or null.</summary>
    public PlayerEntry? ByTribe(int tribe) => tribe < 0 ? null : TribeIndex.GetValueOrDefault(tribe);

    /// <summary>Whether a participant plays <paramref name="tribe"/>.</summary>
    public bool HasTribe(int tribe) => tribe >= 0 && TribeIndex.ContainsKey(tribe);

    /// <summary>A digest of the whole table (what <see cref="Write"/> writes), for state hashes.</summary>
    internal uint Digest => digest ??= Fnv.Hash(Bytes());

    /// <summary>The tribe of the lowest human slot that has one, or -1 (what a spectator watches).</summary>
    public int FirstHumanTribe => Array.Find(entries, e => !e.IsComputer && e.Tribe >= 0)?.Tribe ?? -1;

    /// <summary>The table with <paramref name="entry"/> replacing the entry of the same id, or added.</summary>
    public PlayerTable With(PlayerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return WithAll([entry]);
    }

    /// <summary>The table with each of <paramref name="changed"/> replacing the entry of the same id, or added (in order).</summary>
    public PlayerTable WithAll(IReadOnlyCollection<PlayerEntry> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (changed.Count == 0)
        {
            return this;
        }
        var list = new List<PlayerEntry>(entries);
        var index = new Dictionary<Guid, int>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            index[list[i].Id] = i;
        }
        foreach (PlayerEntry entry in changed)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (index.TryGetValue(entry.Id, out int at))
            {
                list[at] = entry;
            }
            else
            {
                index[entry.Id] = list.Count;
                list.Add(entry);
            }
        }
        return Of(list);
    }

    /// <inheritdoc />
    public IEnumerator<PlayerEntry> GetEnumerator() => ((IEnumerable<PlayerEntry>)entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Dictionary<Guid, PlayerEntry> IdIndex => byId ??= Index(e => e.Id, _ => true);

    private Dictionary<int, PlayerEntry> TribeIndex => byTribe ??= Index(e => e.Tribe, e => e.Tribe >= 0);

    /// <summary>The entries by <paramref name="key"/> (the first entry of each key), of those <paramref name="listed"/>.</summary>
    private Dictionary<TKey, PlayerEntry> Index<TKey>(Func<PlayerEntry, TKey> key, Predicate<PlayerEntry> listed)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, PlayerEntry>(entries.Length);
        foreach (PlayerEntry entry in entries)
        {
            if (listed(entry))
            {
                index.TryAdd(key(entry), entry);
            }
        }
        return index;
    }

    private byte[] Bytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Write(writer);
        }
        return stream.ToArray();
    }

    /// <summary>Writes the table (the format of saves and snapshots).</summary>
    internal void Write(BinaryWriter writer)
    {
        writer.Write(entries.Length);
        foreach (PlayerEntry e in entries)
        {
            writer.Write(e.Id.ToByteArray());
            writer.Write(e.Name);
            writer.Write(e.Slot);
            writer.Write(e.Tribe);
            writer.Write(e.Colour);
            writer.Write(e.Team);
            writer.Write(e.IsComputer);
            writer.Write(e.AiLevel);
            writer.Write((byte)e.State);
        }
    }

    /// <summary>Reads a table written by <see cref="Write"/>.</summary>
    /// <exception cref="InvalidDataException">The data is not a table.</exception>
    internal static PlayerTable Read(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || (reader.BaseStream.CanSeek && count > (reader.BaseStream.Length - reader.BaseStream.Position) / MinEntryBytes))
        {
            throw new InvalidDataException($"A player table cannot have {count} entries.");
        }
        var list = new List<PlayerEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var id = new Guid(reader.ReadBytes(16));
            string name = reader.ReadString();
            if (name.Length > MaxNameBytes)
            {
                throw new InvalidDataException("A player name is too long.");
            }
            int slot = reader.ReadInt32();
            int tribe = reader.ReadInt32();
            int colour = reader.ReadInt32();
            int team = reader.ReadInt32();
            bool computer = reader.ReadBoolean();
            int ai = reader.ReadInt32();
            byte state = reader.ReadByte();
            if (slot is < -1 or >= RoomLimits.MaxPlayers || tribe < -1 || colour is < -1 or >= RoomLimits.ColourCount
                || !Enum.IsDefined((PlayerState)state))
            {
                throw new InvalidDataException($"Player entry {i} is out of range.");
            }
            list.Add(new PlayerEntry
            {
                Id = id,
                Name = name,
                Slot = slot,
                Tribe = tribe,
                Colour = colour,
                Team = team,
                IsComputer = computer,
                AiLevel = ai,
                State = (PlayerState)state,
            });
        }
        try
        {
            return Of(list);
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }
}
