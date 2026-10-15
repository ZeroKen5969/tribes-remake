using System;
using System.Collections.Generic;

namespace Tribes.Client.Ui.Logic;

/// <summary>
/// One entry of the campaign list: a mission the player starts, with the missions played on from
/// it in the same world (its later parts, which are never started on their own).
/// </summary>
/// <param name="Number">Place in the list, from 1.</param>
/// <param name="MenuIndex">Position of its start in the original mission menu (its name is string s.82 + this).</param>
/// <param name="Missions">Its start, then its later parts in play order.</param>
/// <param name="Unlocked">Whether it can be started.</param>
/// <param name="Completed">Whether its last mission has been won.</param>
public sealed record CampaignChapter(int Number, int MenuIndex, IReadOnlyList<int> Missions, bool Unlocked, bool Completed)
{
    /// <summary>The mission the chapter starts with.</summary>
    public int StartMission => Missions[0];

    /// <summary>The mission whose victory finishes the chapter.</summary>
    public int LastMission => Missions[^1];
}

/// <summary>
/// The campaign list as the original's mission menu has it: its entries before the first custom
/// map are the missions a player starts (0, 2, 3, 4, 5); the missions between two starts (1, 6)
/// are played on from the start before them. The original's menu grew one entry with each won
/// chapter and showed every entry up to the furthest reached, so a chapter is open when it is the
/// first, or one at or before the chapter after the furthest one won.
/// </summary>
public static class CampaignChapters
{
    /// <summary>
    /// The chapters of <paramref name="menuMissions"/> (the original menu's missions, in menu
    /// order) that start before <paramref name="firstCustomMission"/>, with progress from the
    /// missions won (<paramref name="completed"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException">A table is missing.</exception>
    public static IReadOnlyList<CampaignChapter> Build(IReadOnlyList<int> menuMissions, int firstCustomMission,
        IEnumerable<int> completed)
    {
        ArgumentNullException.ThrowIfNull(menuMissions);
        ArgumentNullException.ThrowIfNull(completed);
        var won = new HashSet<int>(completed);
        var starts = new List<(int Mission, int MenuIndex)>();
        var startSet = new HashSet<int>();
        for (int i = 0; i < menuMissions.Count; i++)
        {
            // A start listed again is the same chapter: the first entry names it.
            if (menuMissions[i] >= 0 && menuMissions[i] < firstCustomMission && startSet.Add(menuMissions[i]))
            {
                starts.Add((menuMissions[i], i));
            }
        }

        var chains = new List<IReadOnlyList<int>>();
        int furthestWon = -1;
        foreach ((int start, _) in starts)
        {
            var chain = new List<int> { start };
            for (int next = start + 1; next < firstCustomMission && !startSet.Contains(next); next++)
            {
                chain.Add(next);
            }
            if (won.Contains(chain[^1]))
            {
                furthestWon = chains.Count;
            }
            chains.Add(chain);
        }

        var chapters = new List<CampaignChapter>();
        for (int i = 0; i < chains.Count; i++)
        {
            bool unlocked = i <= furthestWon + 1;
            chapters.Add(new CampaignChapter(i + 1, starts[i].MenuIndex, chains[i], unlocked, won.Contains(chains[i][^1])));
        }
        return chapters;
    }

    /// <summary>The missions of <paramref name="menuMissions"/> from <paramref name="firstCustomMission"/> on: the custom maps, in menu order.</summary>
    /// <exception cref="ArgumentNullException">The table is missing.</exception>
    public static IReadOnlyList<int> CustomMaps(IReadOnlyList<int> menuMissions, int firstCustomMission)
    {
        ArgumentNullException.ThrowIfNull(menuMissions);
        var maps = new List<int>();
        foreach (int mission in menuMissions)
        {
            if (mission >= firstCustomMission)
            {
                maps.Add(mission);
            }
        }
        return maps;
    }

    /// <summary>The position in <paramref name="chapters"/> of the chapter <paramref name="mission"/> is played in, or -1.</summary>
    public static int IndexOf(IReadOnlyList<CampaignChapter> chapters, int mission)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        for (int i = 0; i < chapters.Count; i++)
        {
            foreach (int part in chapters[i].Missions)
            {
                if (part == mission)
                {
                    return i;
                }
            }
        }
        return -1;
    }

    /// <summary>The chapter the list opens on: the first open one not won yet, else the last open one; null when there are none.</summary>
    public static CampaignChapter? Initial(IReadOnlyList<CampaignChapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        CampaignChapter? last = null;
        foreach (CampaignChapter chapter in chapters)
        {
            if (!chapter.Unlocked)
            {
                continue;
            }
            if (!chapter.Completed)
            {
                return chapter;
            }
            last = chapter;
        }
        return last;
    }
}
