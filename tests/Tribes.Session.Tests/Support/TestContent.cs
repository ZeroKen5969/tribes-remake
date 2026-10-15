using System;
using System.Collections.Generic;
using System.IO;
using Tribes.Core.Content;
using Tribes.Net;
using Tribes.Session;

namespace Tribes.Session.Tests.Support;

/// <summary>The game content and shared test values.</summary>
internal static class TestContent
{
    public const string GameVersion = "1.0.0-test";
    public const string ContentVersion = "content-test";

    private static readonly Lazy<GameContent> Loaded = new(() => GameContent.Load(Path.Combine(RepoRoot(), "data")));

    public static GameContent Content => Loaded.Value;

    public static RunnerOptions Runner(ISaveStore? saves = null, int autosaveTicks = 1500, List<string>? log = null, bool background = false) => new()
    {
        GameVersion = GameVersion,
        ContentVersion = ContentVersion,
        Saves = saves,
        AutosaveTicks = autosaveTicks,
        Log = log is null ? null : log.Add,
        WriteInBackground = background,
    };

    /// <summary>A match start like a host's: the given roster, setup bytes of <paramref name="plan"/>.</summary>
    public static MatchStart Start(MatchPlan plan, params PlayerInfo[] players) => new()
    {
        MatchId = Guid.NewGuid(),
        Mode = plan.Endless is null ? RoomMode.Versus : RoomMode.Endless,
        TickMilliseconds = 120,
        HashIntervalTicks = 8,
        Players = players,
        GameSetup = plan.ToBytes(),
    };

    public static PlayerInfo Human(int slot, string name, int colour = -1, PlayerPresence presence = PlayerPresence.Active) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slot = slot,
        Kind = PlayerKind.Human,
        Colour = colour < 0 ? slot : colour,
        Team = slot,
        Presence = presence,
        IsConnected = true,
    };

    public static PlayerInfo Computer(int slot, int level = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "CPU",
        Slot = slot,
        Kind = PlayerKind.Computer,
        Colour = slot,
        Team = slot,
        AiLevel = level,
    };

    public static PlayerInfo Spectator(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slot = -1,
        Kind = PlayerKind.Human,
        Presence = PlayerPresence.Spectator,
        IsConnected = true,
    };

    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "rules")) && File.Exists(Path.Combine(dir.FullName, "Tribes.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Saves kept in memory.</summary>
internal sealed class MemorySaveStore : ISaveStore
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<SaveSlot> Written { get; } = [];

    public bool Fail { get; set; }

    public bool FailPictures { get; set; }

    public Dictionary<string, byte[]> Pictures { get; } = new(StringComparer.Ordinal);

    public void Write(SaveSlot slot, byte[] data)
    {
        if (Fail)
        {
            throw new IOException("disk full");
        }
        lock (Files)
        {
            Pictures.Remove(slot.Stem);
            Files[slot.Stem] = data;
            Written.Add(slot);
        }
    }

    public void WritePicture(SaveSlot slot, byte[] png)
    {
        if (FailPictures)
        {
            throw new IOException("picture disk full");
        }
        lock (Files)
        {
            Pictures[slot.Stem] = png;
        }
    }
}
