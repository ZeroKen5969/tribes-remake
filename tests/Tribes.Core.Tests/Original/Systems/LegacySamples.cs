using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Legacy;
using Tribes.Trace;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// A state of the translated original taken from a key-monkey game in rules mode. Restoring it puts
/// the game object back into that state, so a test can run original methods on it.
/// </summary>
internal sealed record LegacySample(long Seed, int Frame, GameCanvas Game, LegacySnapshot State)
{
    /// <summary>Puts <see cref="Game"/> back into the sampled state (in rules mode).</summary>
    public GameCanvas Restore()
    {
        PortHooks.rulesMode = true;
        State.Restore();
        return Game;
    }

    /// <inheritdoc />
    public override string ToString() => $"seed {Seed} frame {Frame}";
}

/// <summary>
/// Game states of key-monkey runs (seeds 1, 4 and 6), shared by all parity tests of the rule
/// systems so each game is played only once per test run.
/// </summary>
internal static class LegacySamples
{
    /// <summary>The monkey seeds sampled.</summary>
    public static readonly long[] Seeds = { 1L, 4L, 6L };

    /// <summary>The last frame of each run.</summary>
    public const int LastFrame = 20000;

    /// <summary>Frames between two samples.</summary>
    public const int Interval = 1000;

    private const int MonkeyFrom = 80;

    private static readonly object Gate = new();
    private static IReadOnlyList<LegacySample>? all;

    /// <summary>The content tables and maps.</summary>
    public static GameContent Content { get; } = GameContent.Load(Path.Combine(TestPaths.RepoRoot(), "data"));

    /// <summary>Every sample of every seed, in seed then frame order.</summary>
    public static IReadOnlyList<LegacySample> All
    {
        get
        {
            lock (Gate)
            {
                return all ??= Seeds.SelectMany(Collect).ToList();
            }
        }
    }

    private static List<LegacySample> Collect(long seed)
    {
        var samples = new List<LegacySample>();
        string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
        Program.Run(resources, LastFrame, seed, MonkeyFrom, (frame, game) =>
        {
            if (frame % Interval == 0 && InPlay(game))
            {
                samples.Add(new LegacySample(seed, frame, game, LegacySnapshot.Capture(game)));
            }
        }, rulesMode: true);
        return samples;
    }

    /// <summary>Whether a mission is being played: some unit and some building exist.</summary>
    private static bool InPlay(GameCanvas game)
    {
        sbyte[] state = game.bArr_e;
        bool units = Enumerable.Range(1, 100).Any(id => state[Parity.EntityFields.UnitBase + (19 * 101) + id] > 0);
        bool buildings = Enumerable.Range(1, 60).Any(id => state[Parity.EntityFields.BuildingBase + (2 * 61) + id] > 0);
        return units && buildings && game.world_a != null;
    }

    /// <summary>The live units of the sampled state (hit points above 0).</summary>
    public static IEnumerable<int> LiveUnits(GameCanvas game) =>
        Enumerable.Range(1, 100).Where(id => Field(game, UnitHitPointsField, id) > 0);

    /// <summary>The used building slots of the sampled state (hit points not 0, ruins included).</summary>
    public static IEnumerable<int> UsedBuildings(GameCanvas game) =>
        Enumerable.Range(1, 60).Where(id => game.bArr_e[Parity.EntityFields.BuildingBase + (2 * 61) + id] != 0);

    /// <summary>Unit field <paramref name="field"/> of unit <paramref name="id"/>.</summary>
    public static int Field(GameCanvas game, int field, int id) =>
        game.bArr_e[Parity.EntityFields.UnitBase + (field * Parity.EntityFields.UnitStride) + id];

    /// <summary>Building field <paramref name="field"/> of building <paramref name="id"/>.</summary>
    public static int BuildingField(GameCanvas game, int field, int id) =>
        game.bArr_e[Parity.EntityFields.BuildingBase + (field * Parity.EntityFields.BuildingStride) + id];

    /// <summary>The original's field number of unit hit points.</summary>
    public const int UnitHitPointsField = 19;

    /// <summary>Shuts out samples of a seed that never reached a mission, so a broken run is noticed.</summary>
    public static void RequireSamples()
    {
        foreach (long seed in Seeds)
        {
            int count = All.Count(s => s.Seed == seed);
            if (count < 5)
            {
                throw new InvalidOperationException($"seed {seed}: only {count} samples in play");
            }
        }
    }
}
