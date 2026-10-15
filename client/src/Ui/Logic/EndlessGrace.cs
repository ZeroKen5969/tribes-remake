using System;

namespace Tribes.Client.Ui.Logic;

/// <summary>
/// The opening grace of an endless world for a people's tribe: computer tribes send no wave at it
/// before the rules' opening grace frame, and a tribe that came later is spared for its own
/// newcomer grace (the rules' pacing, data/endless/endless.json). The time left is told in whole
/// minutes of world frames, each as long as the game's tick (the caller passes it).
/// </summary>
public static class EndlessGrace
{
    private const double MillisecondsPerMinute = 60_000;

    /// <summary>The first frame computer tribes may attack a tribe that arrived at <paramref name="arrivedAt"/> (0: from the start).</summary>
    public static int EndFrame(int openingGraceFrames, int newcomerGraceFrames, int arrivedAt) =>
        arrivedAt > 0 ? Math.Max(openingGraceFrames, arrivedAt + newcomerGraceFrames) : openingGraceFrames;

    /// <summary>
    /// Whole minutes left of the grace at <paramref name="frame"/> with frames of
    /// <paramref name="tickMilliseconds"/> (a part of a minute counts as one), 0 when it is over.
    /// </summary>
    public static int MinutesLeft(int frame, int endFrame, int tickMilliseconds) =>
        frame >= endFrame ? 0 : (int)Math.Ceiling((endFrame - frame) * (double)Math.Max(1, tickMilliseconds) / MillisecondsPerMinute);

    /// <summary>Whether the grace ended after <paramref name="previousFrame"/> and by <paramref name="frame"/>.</summary>
    public static bool EndedBetween(int previousFrame, int frame, int endFrame) => previousFrame < endFrame && frame >= endFrame;
}
