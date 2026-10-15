using System.Collections.Generic;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// When the screen holds the game (the tick's <see cref="TickInput.Paused"/>): a scene's first step
/// sliding in, as the original's story screen draws its black bars before the scene moves.
/// </summary>
public sealed class HoldTests
{
    [Fact]
    public void ASlidingScene_HoldsWhileItsBarsCloseAndOpen_ButMovesOnceInBetween()
    {
        // The original: the bars grow 4 lines a frame from the scene's transition value until past
        // 164 (frames 1..40 for transition 2), the scene moves once, then the bars open again
        // 4 lines a frame from minus that value until -48 (29 more frames).
        var slide = new SceneSlide(transition: 2, sceneKind: 13, chapterScreen: false);
        var held = new List<bool>();
        for (int frame = 0; frame < 80; frame++)
        {
            held.Add(slide.Holds);
            slide.Advance();
        }
        Assert.All(held.Take(40), Assert.True);
        Assert.False(held[40]);
        Assert.All(held.Skip(41).Take(29), Assert.True);
        Assert.All(held.Skip(70), Assert.False);
        Assert.Equal(69, SceneSlide.HeldFrames(2));
        Assert.Equal(69, SceneSlide.HeldFrames(1));
    }

    [Fact]
    public void ScenesWithoutTransition_TheirOwnLayout_OrTheChapterScreen_DoNotSlide()
    {
        Assert.False(new SceneSlide(transition: 0, sceneKind: 8, chapterScreen: false).Holds);
        Assert.False(new SceneSlide(transition: 2, sceneKind: SceneSlide.OwnLayoutScene, chapterScreen: false).Holds);
        Assert.False(new SceneSlide(transition: 2, sceneKind: 13, chapterScreen: true).Holds);
        Assert.False(SceneSlide.None.Holds);
    }

    [Fact]
    public void TheSlideDataOfTheRules_IsWhatTheScreenUses()
    {
        // Dialogues 37 and 38 start with the two scenes that slide in with transition 2.
        IReadOnlyList<StoryScene> scenes = Repo.Instance.Content.Rules.Story.Scenes;
        Assert.Equal(2, scenes[13].Transition);
        Assert.Equal(2, scenes[14].Transition);
        SceneSlide slide = SceneSlide.For(scenes, new SceneStep(37, 0, 3, 0, 0, 0, 13, 0, 0, false, true, 10));
        Assert.True(slide.Holds);
        Assert.False(SceneSlide.For(scenes, new SceneStep(37, 1, 3, 0, 0, 0, 13, 0, 0, false, true, 10)).Holds);
    }
}
