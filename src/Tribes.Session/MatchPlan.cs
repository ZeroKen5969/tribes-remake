using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;

namespace Tribes.Session;

/// <summary>Settings of a new endless world, as the menus choose them.</summary>
/// <param name="Seed">World seed.</param>
/// <param name="StartAreaSize">Side of the start area in cells.</param>
/// <param name="ComputerTribes">Computer tribes at the start.</param>
/// <param name="NewTribes">How often new computer tribes appear (0 none to 3 many).</param>
/// <param name="DevAllianceHalls">For scripted test runs: the people's tribes start with a finished alliance building (see <c>EndlessSetup.DevAllianceHalls</c>).</param>
public sealed record EndlessPlan(int Seed, int StartAreaSize, int ComputerTribes, int NewTribes, bool DevAllianceHalls = false);

/// <summary>
/// The game setup of a network match: the bytes the host puts in <c>MatchStart.GameSetup</c>,
/// UTF-8 JSON as the client's menus write it:
/// <c>{"mission": 8, "difficulty": 0, "seed": 1234, "endless": null}</c>. Every peer builds its
/// world from the same bytes.
/// </summary>
public sealed record MatchPlan
{
    /// <summary>Largest world a game setup may unpack to (the network layer's snapshot limit).</summary>
    public const int MaxWorldBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Mission (map) number; -1 for an endless world.</summary>
    public int Mission { get; init; } = -1;

    /// <summary>Difficulty of the computer tribes: 0 easy, 1 hard.</summary>
    public int Difficulty { get; init; }

    /// <summary>Seed of the rules' random generator.</summary>
    public long Seed { get; init; } = 1;

    /// <summary>Endless world settings, or null.</summary>
    public EndlessPlan? Endless { get; init; }

    /// <summary>
    /// The lobby's settings for a new world (opening grace, start stock, wild animals); null for the
    /// defaults (<see cref="DefaultMatchSettings"/> for a match, the endless rules' for an endless world).
    /// A world gone on with keeps the settings it was saved with.
    /// </summary>
    public WorldSettings? Settings { get; init; }

    /// <summary>World frames per minute at normal speed.</summary>
    public const int FramesPerMinute = 60_000 / LocalMatchRunner.TickMilliseconds;

    /// <summary>The opening grace of a match by default, in minutes.</summary>
    public const int DefaultMatchGraceMinutes = 5;

    /// <summary>A match's settings when the lobby chose none: the default opening grace, the map's stock, wild animals.</summary>
    public static WorldSettings DefaultMatchSettings { get; } = new() { OpeningGraceFrames = DefaultMatchGraceMinutes * FramesPerMinute };

    /// <summary>
    /// A world to go on with instead of a new one (a game played alone, opened to the network):
    /// a zlib-compressed <c>GameSimulation</c> save, base64 in the JSON. Null for a new world.
    /// </summary>
    public byte[]? World { get; init; }

    /// <summary>The setup that continues <paramref name="simulation"/>'s world.</summary>
    public static MatchPlan Continuing(GameSimulation simulation, EndlessPlan? endless = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            simulation.Save(zlib);
        }
        return new MatchPlan
        {
            Mission = simulation.World.Mission,
            Difficulty = simulation.World.Difficulty,
            Endless = endless,
            World = packed.ToArray(),
        };
    }

    /// <summary>The world of the match: <see cref="World"/> when set, else null (the caller builds a new one).</summary>
    /// <exception cref="InvalidDataException">The world bytes are not a saved game.</exception>
    public GameSimulation? LoadWorld(GameContent content)
    {
        if (World is null)
        {
            return null;
        }
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(World, writable: false), CompressionMode.Decompress);
            using var plain = new MemoryStream();
            // The bytes come from another device: never inflate more than a snapshot may hold.
            byte[] buffer = new byte[81920];
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (plain.Length + read > MaxWorldBytes)
                {
                    throw new InvalidDataException($"The game setup's world is larger than {MaxWorldBytes} bytes.");
                }
                plain.Write(buffer, 0, read);
            }
            // Network play: hints never stop the world (a world played alone saved them that way).
            return GameSimulation.Load(content, plain.ToArray(), new SimulationOptions { HintsPauseGame = false });
        }
        catch (Exception e) when (e is IOException or ArgumentException or FormatException or IndexOutOfRangeException or KeyNotFoundException)
        {
            throw new InvalidDataException("The game setup's world cannot be loaded: " + e.Message, e);
        }
    }

    /// <summary>
    /// The mission whose map the world is built from. Endless worlds make their own land and play by
    /// the rules of the first custom mission.
    /// </summary>
    [JsonIgnore]
    public int MapMission => Mission >= 0 ? Mission : MissionStart.FirstCustomMission;

    /// <summary>The bytes for <c>HostSession.Start</c>.</summary>
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Reads setup bytes.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a match setup.</exception>
    public static MatchPlan Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            throw new InvalidDataException("The match has no game setup.");
        }
        MatchPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<MatchPlan>(bytes, Json);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The game setup is not valid JSON: " + e.Message, e);
        }
        if (plan is null)
        {
            throw new InvalidDataException("The game setup is empty.");
        }
        if (plan.Difficulty is < 0 or > 1)
        {
            throw new InvalidDataException($"Difficulty {plan.Difficulty} is not 0 or 1.");
        }
        if (plan.Settings?.Problem() is string problem)
        {
            throw new InvalidDataException("The game setup's settings are out of range: " + problem);
        }
        return plan;
    }
}
