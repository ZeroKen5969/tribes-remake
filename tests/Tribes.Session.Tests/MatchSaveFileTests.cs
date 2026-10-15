using System;
using System.Buffers.Binary;
using Tribes.Net;
using Xunit;

namespace Tribes.Session.Tests;

public sealed class MatchSaveFileTests
{
    private static SaveHeader Header(SaveKind kind = SaveKind.HostedMatch) => new()
    {
        Kind = kind,
        Match = MatchKind.Endless,
        GameVersion = "1.2.3",
        ContentVersion = "content-r9",
        MatchId = Guid.NewGuid(),
        Mission = -1,
        Difficulty = 1,
        Tick = 12_345,
        TickMilliseconds = 120,
        SavedAt = new DateTime(2026, 10, 14, 3, 4, 5, DateTimeKind.Utc),
        Title = "世界 7",
        Players = PlayerTable.Of(
        [
            new PlayerEntry { Id = Guid.NewGuid(), Name = "Host", Slot = 0, Tribe = 0, Colour = 3 },
            new PlayerEntry { Id = Guid.NewGuid(), Name = "Gone", Slot = 2, Tribe = 2, Colour = 5, State = PlayerState.Forfeited },
        ]),
    };

    [Fact]
    public void WriteRead_RoundTripsHeaderAndBody()
    {
        SaveHeader header = Header();
        byte[] body = [1, 2, 3, 4, 5];
        byte[] file = MatchSaveFile.Write(header, body);

        (SaveHeader read, byte[] readBody) = MatchSaveFile.Read(file);
        Assert.Equal(body, readBody);
        Assert.Equal(header.Kind, read.Kind);
        Assert.Equal(header.Match, read.Match);
        Assert.Equal(header.GameVersion, read.GameVersion);
        Assert.Equal(header.ContentVersion, read.ContentVersion);
        Assert.Equal(header.MatchId, read.MatchId);
        Assert.Equal((header.Mission, header.Difficulty, header.Tick, header.TickMilliseconds), (read.Mission, read.Difficulty, read.Tick, read.TickMilliseconds));
        Assert.Equal(header.SavedAt, read.SavedAt);
        Assert.Equal(DateTimeKind.Utc, read.SavedAt.Kind);
        Assert.Equal(header.Title, read.Title);
        Assert.Equal(header.Players[1], read.Players[1]);
        Assert.Equal(12_345L * 120 / 1000, read.PlaySeconds);
        Assert.Equal(read.Tick, MatchSaveFile.ReadHeader(file).Tick);
    }

    [Fact]
    public void WriteRead_KeepsAHeaderWithThousandsOfComputerTribes()
    {
        var players = new PlayerEntry[5001];
        players[0] = new PlayerEntry { Id = Guid.NewGuid(), Name = "Host", Slot = 0, Tribe = 0 };
        for (int t = 1; t < players.Length; t++)
        {
            players[t] = new PlayerEntry { Id = Guid.NewGuid(), Name = "", Tribe = t, IsComputer = true, State = t % 7 == 0 ? PlayerState.Out : PlayerState.Playing };
        }
        SaveHeader header = Header() with { Players = PlayerTable.Of(players) };

        SaveHeader read = MatchSaveFile.ReadHeader(MatchSaveFile.Write(header, [1]));

        Assert.Equal(5001, read.Players.Count);
        Assert.Equal(players[4998], read.Players.ByTribe(4998));
        Assert.Equal(header.Players.Digest, read.Players.Digest);
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        Assert.Equal(SaveProblem.NotASave, Problem([]));
        Assert.Equal(SaveProblem.NotASave, Problem("TRBS"u8.ToArray()));
        Assert.Equal(SaveProblem.NotASave, Problem("hello, world"u8.ToArray()));
    }

    [Fact]
    public void Read_RejectsNewerFormats()
    {
        byte[] file = MatchSaveFile.Write(Header(), [9]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), MatchSaveFile.FormatVersion + 1);
        SaveFileException error = Assert.Throws<SaveFileException>(() => MatchSaveFile.Read(file));
        Assert.Equal(SaveProblem.NewerFormat, error.Problem);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 0);
        Assert.Equal(SaveProblem.Damaged, Problem(file));
    }

    [Fact]
    public void Read_RejectsCorruptFiles()
    {
        byte[] good = MatchSaveFile.Write(Header(), new byte[300]);
        for (int i = 4; i < good.Length; i += 7)
        {
            byte[] flipped = (byte[])good.Clone();
            flipped[i] ^= 0x5A;
            SaveProblem problem = Problem(flipped);
            Assert.True(problem is SaveProblem.Damaged or SaveProblem.NewerFormat, $"byte {i}: {problem}");
        }
        Assert.Equal(SaveProblem.Damaged, Problem(good[..6]));
        Assert.Equal(SaveProblem.Damaged, Problem(good[..^1]));
        Assert.Equal(SaveProblem.Damaged, Problem([.. good, 0]));
    }

    [Fact]
    public void Read_RejectsInconsistentContent_EvenWithAValidChecksum()
    {
        Assert.Equal(SaveProblem.Damaged, Problem(Resealed(MatchSaveFile.Write(Header(), [1]), file => file[6] = 9)));
        Assert.Equal(SaveProblem.Damaged, Problem(Resealed(MatchSaveFile.Write(Header(), [1]), file => file[7] = 0xFF)));
        Assert.Equal(SaveProblem.Damaged, Problem(Resealed(MatchSaveFile.Write(Header(), [1]), file => file[11] = (byte)MatchKind.Endless + 7)));
        byte[] extra = MatchSaveFile.Write(Header(), [1]);
        byte[] longer = [.. extra[..^4], 0, 0, 0, 0, 0];
        Assert.Equal(SaveProblem.Damaged, Problem(Resealed(longer, _ => { })));
    }

    [Fact]
    public void Exception_HasTheStandardConstructors()
    {
        Assert.Equal(SaveProblem.Damaged, new SaveFileException().Problem);
        Assert.Equal("x", new SaveFileException("x").Message);
        var inner = new InvalidOperationException();
        Assert.Same(inner, new SaveFileException("x", inner).InnerException);
        Assert.Equal(SaveProblem.NotTheHost, new SaveFileException(SaveProblem.NotTheHost, "y").Problem);
    }

    [Fact]
    public void SaveSlot_NamesAndChecks()
    {
        var slot = new SaveSlot(Header(), "3");
        Assert.Equal("endless", slot.ModeName);
        Assert.Equal("endless-3", slot.Stem);
        Assert.True(SaveSlot.IsValidSlot("auto"));
        Assert.True(SaveSlot.IsValidSlot("5"));
        foreach (string? bad in new[] { null, "", "0", "6", "05", "+1", "x", "1 " })
        {
            Assert.False(SaveSlot.IsValidSlot(bad));
        }
    }

    [Fact]
    public void PlayerRules_KnowOnlySystemCommands()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerRules.KindOf(CommandKind.Game));
        Assert.Null(PlayerRules.After(PlayerState.Spectator, CommandKind.PlayerForfeited));
        Assert.Null(PlayerRules.After(PlayerState.Playing, CommandKind.PlayerReturned));
        Assert.Equal(PlayerState.Forfeited, PlayerRules.After(PlayerState.Absent, CommandKind.PlayerForfeited));
    }

    private static SaveProblem Problem(byte[] file) => Assert.Throws<SaveFileException>(() => MatchSaveFile.Read(file)).Problem;

    /// <summary>Changes a save and writes a fresh checksum, so only the content checks can catch it.</summary>
    private static byte[] Resealed(byte[] file, Action<byte[]> change)
    {
        byte[] copy = (byte[])file.Clone();
        change(copy);
        uint hash = 2166136261;
        foreach (byte b in copy.AsSpan(0, copy.Length - 4))
        {
            hash = (hash ^ b) * 16777619;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(copy.Length - 4), hash);
        return copy;
    }
}
