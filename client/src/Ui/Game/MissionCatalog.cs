using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Match;
using Tribes.Core.Systems.Missions;

namespace Tribes.Client.Ui.Game;

/// <summary>A mission the menus offer.</summary>
/// <param name="Mission">Mission number (missions.json, missions-rules.json).</param>
/// <param name="MenuIndex">Position in the original mission menu, or -1 for missions played on from the one before.</param>
/// <param name="IsTutorial">Mission 0: the campaign's first mission, which carries the tutorial.</param>
/// <param name="Number">Number of its campaign chapter (1..5), 0 for custom maps.</param>
/// <param name="Illustration">Story illustration (pi8 index) of its chapter, or -1.</param>
/// <param name="BriefingPages">Story pages of its opening dialogue.</param>
/// <param name="Width">Playable width in cells.</param>
/// <param name="Height">Playable height in cells.</param>
public sealed record MissionEntry(int Mission, int MenuIndex, bool IsTutorial, int Number, int Illustration,
    IReadOnlyList<int> BriefingPages, int Width, int Height)
{
    /// <summary>The mission's name in the current language.</summary>
    public string Name => MissionCatalog.NameOf(this);
}

/// <summary>
/// The campaign missions and custom maps, as the original mission menu has them (see
/// <see cref="CampaignChapters"/>): its entries before the first custom map (mission 7) start the
/// campaign chapters, the missions between two starts (1, 6) are played on from the start before
/// them, and the entries from mission 7 on are the custom maps. Names come from the original
/// strings (table s from 82, in menu order); a mission played on from another one carries that
/// one's name. The campaign ends with mission 6, after which its closing story plays in the same
/// world (mission 7's opening dialogue).
/// </summary>
public static class MissionCatalog
{
    /// <summary>First original string naming the mission menu entries (s.82 "走出洞穴").</summary>
    public const int MissionNameBase = 82;

    private static IReadOnlyList<MissionEntry>? campaign;
    private static IReadOnlyList<MissionEntry>? custom;
    private static readonly Dictionary<int, MissionEntry> ByMission = new();
    private static Task<IReadOnlyDictionary<int, int>>? capacities;

    /// <summary>
    /// The campaign's missions in play order: the missions of its chapters (0..6), each with its
    /// opening briefing. The campaign list shows <see cref="Chapters"/>.
    /// </summary>
    public static IReadOnlyList<MissionEntry> Campaign
    {
        get
        {
            Build();
            return campaign!;
        }
    }

    /// <summary>The maps of custom games, in menu order.</summary>
    public static IReadOnlyList<MissionEntry> CustomMaps
    {
        get
        {
            Build();
            return custom!;
        }
    }

    /// <summary>The campaign list's chapters, open and won as <paramref name="progress"/> says.</summary>
    public static IReadOnlyList<CampaignChapter> Chapters(CampaignProgress progress) =>
        CampaignChapters.Build(GameApp.Instance.Content.Rules.Missions.MenuMissions, MissionStart.FirstCustomMission, progress.Completed ?? []);

    /// <summary>The entry of mission <paramref name="mission"/>, or null.</summary>
    public static MissionEntry? Find(int mission)
    {
        Build();
        return ByMission.TryGetValue(mission, out MissionEntry? entry) ? entry : null;
    }

    /// <summary>The name of <paramref name="entry"/> in the current language.</summary>
    public static string NameOf(MissionEntry entry)
    {
        if (entry.MenuIndex >= 0)
        {
            return UiText.S(MissionNameBase + entry.MenuIndex);
        }
        // A later part is named as its chapter: the closest menu entry before it.
        for (int mission = entry.Mission - 1; mission >= 0; mission--)
        {
            if (Find(mission) is { MenuIndex: >= 0 } start)
            {
                return start.Name;
            }
        }
        return UiText.F("mission.unnamed", entry.Mission);
    }

    /// <summary>The name of <paramref name="chapter"/> in the current language.</summary>
    public static string NameOf(CampaignChapter chapter) => UiText.S(MissionNameBase + chapter.MenuIndex);

    /// <summary>
    /// The most tribes a match on <paramref name="mission"/> starts with (see <see cref="MatchRoom.TribeLimit"/>):
    /// the camps a custom map has room for (worked out once per map), two on a campaign map, no
    /// limit for an endless world (-1).
    /// </summary>
    public static int TribeLimit(int mission) => MatchRoom.TribeLimit(mission, MissionStart.FirstCustomMission, CampCapacity(mission));

    /// <summary>Why <paramref name="tribes"/> tribes (<paramref name="humans"/> of them people) cannot start on <paramref name="mission"/>.</summary>
    public static MatchProblem Check(int mission, int humans, int tribes) =>
        MatchRoom.Check(mission, MissionStart.FirstCustomMission, CampCapacity(mission), humans, tribes);

    /// <summary>What <paramref name="problem"/> means for a match of <paramref name="tribes"/> tribes on <paramref name="mission"/>, for the player.</summary>
    public static string ProblemText(MatchProblem problem, int mission, int tribes) => problem switch
    {
        MatchProblem.MapFull => UiText.F("match_room.map_full", TribeLimit(mission), tribes - TribeLimit(mission)),
        MatchProblem.CampaignMapTooManyPeople => UiText.T("match_room.campaign_map"),
        _ => "",
    };

    /// <summary>
    /// Starts working out the camps of every custom map, once and off the UI thread: each lays out
    /// a whole world (up to some 0.2 s), and the numbers only depend on the map. The boot screen
    /// starts it when the content is loaded.
    /// </summary>
    public static void PrepareCapacities()
    {
        if (capacities is null)
        {
            GameContent content = GameApp.Instance.Content;
            capacities = Task.Run(() => ComputeCapacities(content));
        }
    }

    /// <summary>Whether every custom map's camps are known.</summary>
    public static bool CapacitiesKnown => capacities is { IsCompleted: true };

    /// <summary>Runs <paramref name="action"/> on the main thread once the capacities are known (at once when they are).</summary>
    public static void WhenCapacitiesKnown(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PrepareCapacities();
        if (CapacitiesKnown)
        {
            action();
            return;
        }
        capacities!.ContinueWith(_ => Callable.From(action).CallDeferred(), TaskScheduler.Default);
    }

    /// <summary>The most tribes on <paramref name="mission"/> when that is known now (custom maps wait for <see cref="PrepareCapacities"/>).</summary>
    public static int? KnownTribeLimit(int mission) =>
        mission < MatchStart.FirstMapMission || CapacitiesKnown ? TribeLimit(mission) : null;

    private static IReadOnlyDictionary<int, int> ComputeCapacities(GameContent content)
    {
        var result = new Dictionary<int, int>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int map = 0; map < MatchStart.MapCount(content); map++)
        {
            try
            {
                result[MatchStart.FirstMapMission + map] = MatchStart.CampCapacity(content, map);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // A map the rules cannot lay out has no room: rooms on it cannot start.
                GD.PushError($"camp capacity of custom map {map}: {e.Message}");
            }
        }
        GD.Print($"MAP_CAPACITIES {string.Join(",", result.Values)} in {watch.ElapsedMilliseconds} ms (background)");
        return result;
    }

    /// <summary>
    /// The camps custom map <paramref name="mission"/> has room for (Core's camp layout); 0 for
    /// other missions. Known by the time a room or custom game opens; asked sooner, it waits.
    /// </summary>
    private static int CampCapacity(int mission)
    {
        if (mission < MatchStart.FirstMapMission)
        {
            return 0;
        }
        PrepareCapacities();
        if (!capacities!.IsCompleted)
        {
            // Only when asked before the background work is done (a start right after boot).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            capacities.Wait();
            GD.Print($"MAP_CAPACITIES waited {watch.ElapsedMilliseconds} ms on the UI thread");
        }
        return capacities.Result.TryGetValue(mission, out int capacity) ? capacity : 0;
    }

    private static void Build()
    {
        if (campaign is not null)
        {
            return;
        }
        GameContent content = GameApp.Instance.Content;
        MissionRuleTables rules = content.Rules.Missions;
        StoryTables story = content.Rules.Story;
        IReadOnlyList<CampaignChapter> chapters = CampaignChapters.Build(rules.MenuMissions, MissionStart.FirstCustomMission, []);
        var campaignList = new List<MissionEntry>();
        foreach (MissionRules mission in rules.Missions)
        {
            int chapter = CampaignChapters.IndexOf(chapters, mission.Id);
            if (chapter < 0 || mission.OpeningDialogue is not int dialogueId)
            {
                continue;
            }
            var pages = new List<int>();
            Dialogue dialogue = story.Dialogues[dialogueId];
            for (int page = dialogue.FirstPage; page < dialogue.FirstPage + dialogue.PageCount; page++)
            {
                pages.Add(page);
            }
            int illustration = mission.Id < story.ChapterIllustrations.Count ? story.ChapterIllustrations[mission.Id] : -1;
            var entry = new MissionEntry(mission.Id, IndexOf(rules.MenuMissions, mission.Id), mission.Id == 0,
                chapter + 1, illustration, pages, 0, 0);
            campaignList.Add(Sized(content, entry));
        }
        var customList = new List<MissionEntry>();
        // The custom maps, which rooms and custom games offer (never a campaign map).
        foreach (int mission in MatchRoom.RoomMaps(rules.MenuMissions, MissionStart.FirstCustomMission))
        {
            customList.Add(Sized(content, new MissionEntry(mission, IndexOf(rules.MenuMissions, mission), false, 0, -1, [], 0, 0)));
        }
        foreach (MissionEntry entry in campaignList)
        {
            ByMission[entry.Mission] = entry;
        }
        foreach (MissionEntry entry in customList)
        {
            ByMission[entry.Mission] = entry;
        }
        campaign = campaignList;
        custom = customList;
    }

    private static MissionEntry Sized(GameContent content, MissionEntry entry)
    {
        content.Missions.TryGetValue(entry.Mission, out MissionDefinition? definition);
        return entry with { Width = definition?.PlayableSize.Width ?? 0, Height = definition?.PlayableSize.Height ?? 0 };
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }
        return -1;
    }
}
