using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Tribes.Net;

namespace Tribes.Client.App;

/// <summary>
/// How network sessions build the rules simulation of a match. The menus create sessions with
/// <see cref="Create"/>; the match layer sets <see cref="Factory"/> to its Tribes.Core runner.
/// Until it does, a <see cref="WaitingSimulation"/> stands in, so rooms can still start.
/// </summary>
public static class MatchSimulations
{
    /// <summary>The factory used for every new or joined match.</summary>
    public static Func<MatchStart, ILockstepSimulation> Factory { get; set; } = start => new WaitingSimulation();

    /// <summary>Builds the simulation for <paramref name="start"/> with the current <see cref="Factory"/>.</summary>
    public static ILockstepSimulation Create(MatchStart start) => Factory(start);
}

/// <summary>
/// A stand-in simulation with no rules: it only counts ticks, so every machine agrees on it. Used
/// until the match layer provides the real one.
/// </summary>
public sealed class WaitingSimulation : ILockstepSimulation
{
    /// <inheritdoc/>
    public long Tick { get; private set; }

    /// <inheritdoc/>
    public void Step(IReadOnlyList<PlayerCommand> commands) => Tick++;

    /// <inheritdoc/>
    public uint StateHash() => (uint)Tick;

    /// <inheritdoc/>
    public byte[] SaveSnapshot() => BitConverter.GetBytes(Tick);

    /// <inheritdoc/>
    public void LoadSnapshot(byte[] data) => Tick = BitConverter.ToInt64(data, 0);
}

/// <summary>
/// The start data a host sends to everyone in a room (<c>HostSession.Start</c>), as UTF-8 JSON:
/// <c>{"mission": 8, "difficulty": 0, "seed": 1234, "endless": {"seed": .., "startAreaSize": .., "computerTribes": .., "newTribes": .., "devAllianceHalls": false}}</c>.
/// </summary>
/// <param name="Mission">Map (mission number) of a versus match; -1 for endless worlds.</param>
/// <param name="Difficulty">Difficulty of the computer tribes (0 easy, 1 hard).</param>
/// <param name="Seed">Random seed of the match.</param>
/// <param name="Endless">Endless world settings, or null.</param>
/// <param name="Settings">The world settings (opening grace, start stock, wild animals), or null for the defaults.</param>
public sealed record MatchSetup(int Mission, int Difficulty, int Seed, EndlessSettings? Endless, Tribes.Core.World.WorldSettings? Settings = null)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The bytes for <c>HostSession.Start</c>.</summary>
    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json));

    /// <summary>Reads the bytes of a <see cref="MatchStart.GameSetup"/>; null when they are not a setup.</summary>
    public static MatchSetup? FromBytes(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return bytes.IsEmpty ? null : JsonSerializer.Deserialize<MatchSetup>(bytes, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
