using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Tribes.Core.World;

namespace Tribes.Session;

/// <summary>What a save holds.</summary>
public enum SaveKind : byte
{
    /// <summary>A game played alone: the world and the local player.</summary>
    SinglePlayer = 1,

    /// <summary>A network match kept by its host: room, roster, world and queued commands.</summary>
    HostedMatch = 2,
}

/// <summary>The kind of match, as the menus name them.</summary>
public enum MatchKind : byte
{
    /// <summary>A campaign mission.</summary>
    Campaign = 0,

    /// <summary>A custom map against the computer.</summary>
    Custom = 1,

    /// <summary>A versus match.</summary>
    Versus = 2,

    /// <summary>An endless world.</summary>
    Endless = 3,
}

/// <summary>What a save says about itself; readable without loading the match.</summary>
public sealed record SaveHeader
{
    /// <summary>Single player or a hosted match.</summary>
    public required SaveKind Kind { get; init; }

    /// <summary>The kind of match.</summary>
    public required MatchKind Match { get; init; }

    /// <summary>Game version that wrote the save.</summary>
    public required string GameVersion { get; init; }

    /// <summary>Content version that wrote the save.</summary>
    public required string ContentVersion { get; init; }

    /// <summary>Id of the match (stays the same when it is resumed).</summary>
    public Guid MatchId { get; init; }

    /// <summary>Mission (map) number, -1 for endless worlds.</summary>
    public int Mission { get; init; } = -1;

    /// <summary>Difficulty 0 or 1.</summary>
    public int Difficulty { get; init; }

    /// <summary>The tick the match continues from.</summary>
    public long Tick { get; init; }

    /// <summary>Length of a tick at normal speed.</summary>
    public int TickMilliseconds { get; init; } = 120;

    /// <summary>When the save was written (UTC).</summary>
    public DateTime SavedAt { get; init; }

    /// <summary>A name to show (an endless world's), or empty.</summary>
    public string Title { get; init; } = "";

    /// <summary>The participants as the save leaves them; a resumed match waits for these people.</summary>
    public PlayerTable Players { get; init; } = PlayerTable.Empty;

    /// <summary>The settings the world was started with (a match's or an endless world's); null for the campaign and saves of format 1.</summary>
    public WorldSettings? Settings { get; init; }

    /// <summary>Game time played, in seconds.</summary>
    public long PlaySeconds => Tick * TickMilliseconds / 1000;
}

/// <summary>Why a save cannot be used.</summary>
public enum SaveProblem
{
    /// <summary>The file is not a save of this game.</summary>
    NotASave,

    /// <summary>A newer version of the game wrote it.</summary>
    NewerFormat,

    /// <summary>The file is damaged (cut short, changed, or inconsistent).</summary>
    Damaged,

    /// <summary>Another game or content version wrote it, so the match cannot continue in this build.</summary>
    OtherVersion,

    /// <summary>Only the player who hosted the match can resume it.</summary>
    NotTheHost,

    /// <summary>The save is fine, but the room cannot be opened (for example the network port is in use).</summary>
    CannotHost,
}

/// <summary>A save that cannot be read or continued.</summary>
public sealed class SaveFileException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SaveFileException()
        : this(SaveProblem.Damaged, "The save is damaged.")
    {
    }

    /// <summary>Creates the exception.</summary>
    public SaveFileException(string message)
        : this(SaveProblem.Damaged, message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SaveFileException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problem = SaveProblem.Damaged;
    }

    /// <summary>Creates the exception.</summary>
    public SaveFileException(SaveProblem problem, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Problem = problem;
    }

    /// <summary>What is wrong.</summary>
    public SaveProblem Problem { get; }
}

/// <summary>
/// The save file of single-player games and hosted matches (versioned):
/// <c>TRSV</c>, <c>u16</c> format version, <c>u8</c> kind, header block, body block, <c>u32</c>
/// FNV-1a of everything before it. Blocks are an <c>i32</c> length and the bytes. The header is
/// <see cref="SaveHeader"/>; the body is the world (single player: a <c>GameSimulation</c> save)
/// or the host session's match save. Damaged files and files of newer versions are refused.
/// </summary>
public static class MatchSaveFile
{
    /// <summary>The format version this build writes and the newest it reads.</summary>
    public const ushort FormatVersion = 2;

    /// <summary>The first format whose header holds the world's settings.</summary>
    private const ushort SettingsVersion = 2;

    private const uint Magic = 0x56535254; // "TRSV"
    private const int MaxTextLength = 256;

    /// <summary>The bytes of a save.</summary>
    public static byte[] Write(SaveHeader header, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(header);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write((byte)header.Kind);
            byte[] head = WriteHeader(header);
            writer.Write(head.Length);
            writer.Write(head);
            writer.Write(body.Length);
            writer.Write(body);
            writer.Flush();
            writer.Write(Fnv.Hash(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
        }
        return stream.ToArray();
    }

    /// <summary>The header of a save, for load menus.</summary>
    /// <exception cref="SaveFileException">The file is not a usable save.</exception>
    public static SaveHeader ReadHeader(ReadOnlySpan<byte> file) => Read(file).Header;

    /// <summary>The header and body of a save.</summary>
    /// <exception cref="SaveFileException">The file is not a usable save.</exception>
    public static (SaveHeader Header, byte[] Body) Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic)
        {
            throw new SaveFileException(SaveProblem.NotASave, "This file is not a Tribes save.");
        }
        if (file.Length < 7 + sizeof(uint))
        {
            throw new SaveFileException(SaveProblem.Damaged, "The save is cut short.");
        }
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(file[4..]);
        if (version > FormatVersion)
        {
            throw new SaveFileException(SaveProblem.NewerFormat, $"The save has format {version}; this game reads up to {FormatVersion}.");
        }
        if (version == 0)
        {
            throw new SaveFileException(SaveProblem.Damaged, "The save has format 0.");
        }
        int checkedLength = file.Length - sizeof(uint);
        if (Fnv.Hash(file[..checkedLength]) != BinaryPrimitives.ReadUInt32LittleEndian(file[checkedLength..]))
        {
            throw new SaveFileException(SaveProblem.Damaged, "The save is damaged (checksum differs).");
        }
        try
        {
            using var reader = new BinaryReader(new MemoryStream(file[6..checkedLength].ToArray(), writable: false), Encoding.UTF8);
            var kind = (SaveKind)reader.ReadByte();
            if (!Enum.IsDefined(kind))
            {
                throw new SaveFileException(SaveProblem.Damaged, $"Save kind {(int)kind} is unknown.");
            }
            // The header holds the participants, any number of computer tribes among them.
            byte[] head = ReadBlock(reader, int.MaxValue);
            byte[] body = ReadBlock(reader, int.MaxValue);
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new SaveFileException(SaveProblem.Damaged, "The save has bytes left over.");
            }
            return (ReadHeader(head, kind, version), body);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or FormatException or DecoderFallbackException)
        {
            throw new SaveFileException(SaveProblem.Damaged, "The save is damaged: " + e.Message, e);
        }
    }

    private static byte[] WriteHeader(SaveHeader header)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)header.Match);
            writer.Write(header.GameVersion);
            writer.Write(header.ContentVersion);
            writer.Write(header.MatchId.ToByteArray());
            writer.Write(header.Mission);
            writer.Write(header.Difficulty);
            writer.Write(header.Tick);
            writer.Write(header.TickMilliseconds);
            writer.Write(header.SavedAt.ToUniversalTime().Ticks);
            writer.Write(header.Title);
            header.Players.Write(writer);
            writer.Write(header.Settings is not null);
            if (header.Settings is { } settings)
            {
                writer.Write(settings.OpeningGraceFrames);
                writer.Write(settings.StartStockPercent);
                writer.Write(settings.WildAnimals);
            }
        }
        return stream.ToArray();
    }

    private static SaveHeader ReadHeader(byte[] head, SaveKind kind, ushort version)
    {
        using var reader = new BinaryReader(new MemoryStream(head, writable: false), Encoding.UTF8);
        var match = (MatchKind)reader.ReadByte();
        string game = ReadText(reader);
        string contentVersion = ReadText(reader);
        var id = new Guid(reader.ReadBytes(16));
        int mission = reader.ReadInt32();
        int difficulty = reader.ReadInt32();
        long tick = reader.ReadInt64();
        int tickMs = reader.ReadInt32();
        long saved = reader.ReadInt64();
        string title = ReadText(reader);
        PlayerTable players = PlayerTable.Read(reader);
        WorldSettings? settings = version >= SettingsVersion && reader.ReadBoolean()
            ? new WorldSettings { OpeningGraceFrames = reader.ReadInt32(), StartStockPercent = reader.ReadInt32(), WildAnimals = reader.ReadBoolean() }
            : null;
        if (settings?.Problem() is string problem)
        {
            throw new InvalidDataException("The save header's settings are out of range: " + problem);
        }
        if (!Enum.IsDefined(match) || tick < 0 || tickMs <= 0 || saved < DateTime.MinValue.Ticks || saved > DateTime.MaxValue.Ticks
            || reader.BaseStream.Position != reader.BaseStream.Length)
        {
            throw new InvalidDataException("The save header is out of range.");
        }
        return new SaveHeader
        {
            Kind = kind,
            Match = match,
            GameVersion = game,
            ContentVersion = contentVersion,
            MatchId = id,
            Mission = mission,
            Difficulty = difficulty,
            Tick = tick,
            TickMilliseconds = tickMs,
            SavedAt = new DateTime(saved, DateTimeKind.Utc),
            Title = title,
            Players = players,
            Settings = settings,
        };
    }

    private static string ReadText(BinaryReader reader)
    {
        string text = reader.ReadString();
        return text.Length <= MaxTextLength ? text : throw new InvalidDataException("A text of the save is too long.");
    }

    private static byte[] ReadBlock(BinaryReader reader, int limit)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > limit || length > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException($"A block of {length} bytes does not fit the save.");
        }
        return reader.ReadBytes(length);
    }
}
