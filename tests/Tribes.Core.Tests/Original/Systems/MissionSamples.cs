using System.Collections.Generic;
using System.Linq;
using Tribes.Legacy;
using Tribes.Trace;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// States of the other missions: the key-monkey games stay in the first mission, so each later
/// mission is started with the original's own mission start and played (without input) by the
/// original's world update; its state is taken every so often. The player's units then work by
/// themselves, the computer player builds and attacks, animals are born and roam.
/// </summary>
internal static class MissionSamples
{
    /// <summary>Missions started.</summary>
    public static readonly int[] Missions = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    /// <summary>Frames each mission is played, and frames between two samples.</summary>
    public const int Frames = 6000, Interval = 600;

    private static readonly object Gate = new();
    private static IReadOnlyList<LegacySample>? all;

    /// <summary>Every sample of every mission, in mission then frame order.</summary>
    public static IReadOnlyList<LegacySample> All
    {
        get
        {
            lock (Gate)
            {
                return all ??= Missions.SelectMany(Play).ToList();
            }
        }
    }

    private static List<LegacySample> Play(int mission)
    {
        LegacySample start = LegacySamples.All[0];
        GameCanvas game = start.Restore();
        game.g_B((sbyte)mission);
        var samples = new List<LegacySample>();
        for (int frame = 1; frame <= Frames; frame++)
        {
            game.j_();
            if (frame % Interval == 0 && GameRuns.InPlay(game))
            {
                samples.Add(new LegacySample(start.Seed, (mission * 100000) + frame, game, LegacySnapshot.Capture(game)));
            }
        }
        return samples;
    }
}
