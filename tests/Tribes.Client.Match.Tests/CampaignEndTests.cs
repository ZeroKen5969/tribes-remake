using System.Collections.Generic;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Logic;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// How the campaign moves on, from the campaign list's chapters, and how it ends: after the last
/// chapter is won the closing story plays in the same world, as in the original.
/// </summary>
public sealed class CampaignEndTests
{
    /// <summary>The rules' victory countdown start (the development cheat uses it too).</summary>
    private const int VictoryCountdownStart = 1;

    /// <summary>The second part of the fifth chapter: the campaign's last mission.</summary>
    private const int LastMission = 6;

    private static IReadOnlyList<CampaignChapter> Chapters() =>
        CampaignChapters.Build(Repo.Instance.Content.Rules.Missions.MenuMissions, MissionStart.FirstCustomMission, []);

    [Fact]
    public void LaterParts_AreStartedFromTheirChaptersStart()
    {
        IReadOnlyList<CampaignChapter> chapters = Chapters();
        Assert.Equal(0, CampaignFlow.StartMissionFor(chapters, 1));
        Assert.Equal(5, CampaignFlow.StartMissionFor(chapters, 6));
        Assert.Equal(3, CampaignFlow.StartMissionFor(chapters, 3));
        // Custom maps are not in the campaign: they start as they are.
        Assert.Equal(7, CampaignFlow.StartMissionFor(chapters, 7));
        Assert.True(CampaignFlow.IsLaterPart(chapters, 1));
        Assert.True(CampaignFlow.IsLaterPart(chapters, 6));
        Assert.False(CampaignFlow.IsLaterPart(chapters, 0));
        Assert.False(CampaignFlow.IsLaterPart(chapters, 7));
    }

    [Fact]
    public void AVictory_CompletesItsChapterUpToIt_AndLeadsToTheNextChapter()
    {
        IReadOnlyList<CampaignChapter> chapters = Chapters();
        Assert.Equal(new[] { 0, 1 }, CampaignFlow.CompletedBy(chapters, 1));
        Assert.Equal(new[] { 5, 6 }, CampaignFlow.CompletedBy(chapters, 6));
        Assert.Equal(new[] { 4 }, CampaignFlow.CompletedBy(chapters, 4));
        Assert.Equal(new[] { 8 }, CampaignFlow.CompletedBy(chapters, 8));
        Assert.Equal(2, CampaignFlow.NextMission(chapters, 1));
        Assert.Equal(5, CampaignFlow.NextMission(chapters, 4));
        // After the last chapter there is no next mission (mission 7 is a custom map): the campaign ends.
        Assert.Null(CampaignFlow.NextMission(chapters, LastMission));
        Assert.True(CampaignFlow.EndsCampaign(chapters, LastMission));
        Assert.False(CampaignFlow.EndsCampaign(chapters, 4));
        Assert.False(CampaignFlow.EndsCampaign(chapters, 5));
        Assert.Null(CampaignFlow.NextMission(chapters, 9));
        Assert.False(CampaignFlow.EndsCampaign(chapters, 9));
    }

    [Fact]
    public void WinningTheLastMission_PlaysTheClosingStoryInTheSameWorld()
    {
        GameSimulation game = Won(LastMission);
        int mapWidth = game.World.Map.Size.Width;
        Assert.True(game.ClosingSceneFollows);
        Assert.True(game.PlayClosingScene());
        Assert.False(game.IsOver);
        Assert.Equal(LastMission, game.World.Mission);
        Assert.Equal(mapWidth, game.World.Map.Size.Width);
        var ended = new List<SceneEnded>();
        int shown = -1;
        bool onChapterScreen = false;
        for (int i = 0; i < 400 && ended.Count == 0; i++)
        {
            shown = game.CurrentScene?.Dialogue ?? shown;
            onChapterScreen |= game.CurrentScene?.ChapterScreen == true;
            TickResult result = game.Step(game.CanContinueScene ? TickInput.WithOrders(new AdvanceScene(0)) : TickInput.Idle);
            ended.AddRange(result.Events.OfType<SceneEnded>());
        }
        Assert.Equal(GameSimulation.ClosingDialogue, shown);
        // The original shows it on its chapter screen (state 18), titled 「劇終」.
        Assert.True(onChapterScreen);
        SceneEnded end = Assert.Single(ended);
        Assert.Equal(GameSimulation.ClosingDialogue, end.Dialogue);
        Assert.False(end.ResumesPlay);
        // It plays once.
        Assert.False(game.PlayClosingScene());
    }

    [Fact]
    public void OtherVictories_HaveNoClosingStory()
    {
        GameSimulation game = Won(4);
        Assert.False(game.ClosingSceneFollows);
        Assert.False(game.PlayClosingScene());
        Assert.True(game.IsOver);
        Assert.False(Repo.Instance.Started(LastMission).ClosingSceneFollows);
    }

    /// <summary>Mission <paramref name="mission"/> won (its victory countdown run to the end).</summary>
    private static GameSimulation Won(int mission)
    {
        GameSimulation game = Repo.Instance.Started(mission);
        game.World.EndCountdown = VictoryCountdownStart;
        for (int i = 0; i < 400 && !game.IsOver; i++)
        {
            game.Step();
        }
        Assert.True(game.IsOver);
        Assert.True(game.Victory);
        return game;
    }
}
