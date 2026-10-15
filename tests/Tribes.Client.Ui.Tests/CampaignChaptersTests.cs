using System;
using System.Linq;
using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The campaign list follows the original mission menu: one entry per mission the player can
/// start (0, 2, 3, 4, 5); missions 1 and 6 are played on from 0 and 5 in the same world, so they
/// belong to those entries. Entries open as the original's menu grew: up to one past the furthest
/// one won.
/// </summary>
public sealed class CampaignChaptersTests
{
    /// <summary>The original's menu table (missions-rules.json menuMissions).</summary>
    private static readonly int[] Menu = { 0, 2, 3, 4, 5, 7, 8, 9, 10, 11 };

    /// <summary>The first custom map (Core's MissionStart.FirstCustomMission).</summary>
    private const int FirstCustom = 7;

    [Fact]
    public void Chapters_are_the_menu_entries_before_the_custom_maps()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, []);

        Assert.Equal(new[] { 0, 2, 3, 4, 5 }, chapters.Select(c => c.StartMission));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, chapters.Select(c => c.Number));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, chapters.Select(c => c.MenuIndex));
    }

    [Fact]
    public void Missions_after_a_start_belong_to_its_chapter()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, []);

        Assert.Equal(new[] { 0, 1 }, chapters[0].Missions);
        Assert.Equal(new[] { 2 }, chapters[1].Missions);
        Assert.Equal(new[] { 3 }, chapters[2].Missions);
        Assert.Equal(new[] { 4 }, chapters[3].Missions);
        Assert.Equal(new[] { 5, 6 }, chapters[4].Missions);
    }

    [Fact]
    public void Custom_maps_are_the_menu_entries_from_the_first_custom_mission()
    {
        Assert.Equal(new[] { 7, 8, 9, 10, 11 }, CampaignChapters.CustomMaps(Menu, FirstCustom));
    }

    [Fact]
    public void Only_the_first_chapter_is_open_without_progress()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, []);

        Assert.Equal(new[] { true, false, false, false, false }, chapters.Select(c => c.Unlocked));
        Assert.All(chapters, c => Assert.False(c.Completed));
    }

    [Fact]
    public void A_chapter_is_won_when_its_last_mission_is_won()
    {
        // Winning mission 0 alone (the first part) does not finish the chapter.
        var firstPartOnly = CampaignChapters.Build(Menu, FirstCustom, [0]);
        Assert.False(firstPartOnly[0].Completed);
        Assert.False(firstPartOnly[1].Unlocked);

        var whole = CampaignChapters.Build(Menu, FirstCustom, [0, 1]);
        Assert.True(whole[0].Completed);
        Assert.True(whole[1].Unlocked);
        Assert.False(whole[2].Unlocked);
    }

    [Fact]
    public void Winning_a_chapter_opens_the_next_one()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, [0, 1, 2, 3]);

        Assert.Equal(new[] { true, true, true, true, false }, chapters.Select(c => c.Unlocked));
        Assert.Equal(new[] { true, true, true, false, false }, chapters.Select(c => c.Completed));
    }

    [Fact]
    public void Every_chapter_up_to_the_furthest_won_is_open()
    {
        // As the original's menu, which shows every entry up to the furthest one reached.
        var chapters = CampaignChapters.Build(Menu, FirstCustom, [3]);

        Assert.Equal(new[] { true, true, true, true, false }, chapters.Select(c => c.Unlocked));
        Assert.Equal(new[] { false, false, true, false, false }, chapters.Select(c => c.Completed));
    }

    [Fact]
    public void The_whole_campaign_won_opens_and_marks_every_chapter()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, [0, 1, 2, 3, 4, 5, 6, 7]);

        Assert.All(chapters, c => Assert.True(c.Unlocked));
        Assert.All(chapters, c => Assert.True(c.Completed));
    }

    [Fact]
    public void Wins_outside_the_campaign_change_nothing()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, [7, 8, 11, -1, 99]);

        Assert.Equal(new[] { true, false, false, false, false }, chapters.Select(c => c.Unlocked));
        Assert.All(chapters, c => Assert.False(c.Completed));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(7, -1)]
    [InlineData(-1, -1)]
    public void A_mission_is_found_in_its_chapter(int mission, int chapter)
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, []);

        Assert.Equal(chapter, CampaignChapters.IndexOf(chapters, mission));
    }

    [Fact]
    public void The_list_starts_at_the_first_open_chapter_not_won()
    {
        Assert.Equal(0, CampaignChapters.Initial(CampaignChapters.Build(Menu, FirstCustom, []))!.StartMission);
        Assert.Equal(3, CampaignChapters.Initial(CampaignChapters.Build(Menu, FirstCustom, [0, 1, 2]))!.StartMission);
    }

    [Fact]
    public void The_list_starts_at_the_last_chapter_when_all_are_won()
    {
        var chapters = CampaignChapters.Build(Menu, FirstCustom, [0, 1, 2, 3, 4, 5, 6]);

        Assert.Equal(5, CampaignChapters.Initial(chapters)!.StartMission);
    }

    [Fact]
    public void No_campaign_entries_give_no_chapters()
    {
        Assert.Empty(CampaignChapters.Build([7, 8], FirstCustom, [1]));
        Assert.Null(CampaignChapters.Initial([]));
    }

    [Fact]
    public void Later_parts_follow_their_start_in_mission_order_whatever_the_menu_order()
    {
        var chapters = CampaignChapters.Build([3, 0, 5], FirstCustom, []);

        Assert.Equal(new[] { 3, 0, 5 }, chapters.Select(c => c.StartMission));
        Assert.Equal(new[] { 3, 4 }, chapters[0].Missions);
        Assert.Equal(new[] { 0, 1, 2 }, chapters[1].Missions);
        Assert.Equal(new[] { 5, 6 }, chapters[2].Missions);
    }

    [Fact]
    public void A_start_listed_twice_makes_one_chapter()
    {
        var chapters = CampaignChapters.Build([0, 2, 2, 5], FirstCustom, []);

        Assert.Equal(new[] { 0, 2, 5 }, chapters.Select(c => c.StartMission));
        Assert.Equal(new[] { 1, 2, 3 }, chapters.Select(c => c.Number));
        Assert.Equal(new[] { 0, 1, 3 }, chapters.Select(c => c.MenuIndex));
    }

    [Fact]
    public void Missing_tables_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => CampaignChapters.Build(null!, FirstCustom, []));
        Assert.Throws<ArgumentNullException>(() => CampaignChapters.Build(Menu, FirstCustom, null!));
        Assert.Throws<ArgumentNullException>(() => CampaignChapters.CustomMaps(null!, FirstCustom));
    }
}
