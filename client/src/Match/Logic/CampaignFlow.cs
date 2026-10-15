using System;
using System.Collections.Generic;
using Tribes.Client.Ui.Logic;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// How the campaign goes from mission to mission, read from the campaign list's chapters
/// (<see cref="CampaignChapters"/>), as in the original: a chapter's later parts are played on
/// from its start in the same world (the rules switch over by themselves), so they are never
/// started on their own; winning a chapter's last mission leads to the next chapter's start, and
/// winning the last chapter ends the campaign (the closing story plays in that world).
/// </summary>
public static class CampaignFlow
{
    /// <summary>The chapter <paramref name="mission"/> is played in, or null (custom maps).</summary>
    public static CampaignChapter? ChapterOf(IReadOnlyList<CampaignChapter> chapters, int mission)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        int at = CampaignChapters.IndexOf(chapters, mission);
        return at >= 0 ? chapters[at] : null;
    }

    /// <summary>Whether <paramref name="mission"/> is a later part of its chapter (played on, never started alone).</summary>
    public static bool IsLaterPart(IReadOnlyList<CampaignChapter> chapters, int mission) =>
        ChapterOf(chapters, mission) is { } chapter && chapter.StartMission != mission;

    /// <summary>The mission to start when <paramref name="mission"/> is asked for: its chapter's start.</summary>
    public static int StartMissionFor(IReadOnlyList<CampaignChapter> chapters, int mission) =>
        ChapterOf(chapters, mission)?.StartMission ?? mission;

    /// <summary>The missions a victory in <paramref name="mission"/> completes: its chapter's missions up to it.</summary>
    public static IReadOnlyList<int> CompletedBy(IReadOnlyList<CampaignChapter> chapters, int mission)
    {
        if (ChapterOf(chapters, mission) is not { } chapter)
        {
            return new[] { mission };
        }
        var done = new List<int>();
        foreach (int part in chapter.Missions)
        {
            done.Add(part);
            if (part == mission)
            {
                break;
            }
        }
        return done;
    }

    /// <summary>
    /// The mission that follows a victory in <paramref name="mission"/>: the next chapter's start
    /// when it is the last mission of a chapter that has one after it; else null.
    /// </summary>
    public static int? NextMission(IReadOnlyList<CampaignChapter> chapters, int mission)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        int at = CampaignChapters.IndexOf(chapters, mission);
        return at >= 0 && at + 1 < chapters.Count && chapters[at].LastMission == mission ? chapters[at + 1].StartMission : null;
    }

    /// <summary>Whether a victory in <paramref name="mission"/> ends the campaign: it is the last chapter's last mission.</summary>
    public static bool EndsCampaign(IReadOnlyList<CampaignChapter> chapters, int mission)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        return chapters.Count > 0 && chapters[^1].LastMission == mission;
    }
}
