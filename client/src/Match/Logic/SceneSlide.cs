using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Story;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// A scene's first step sliding in, as the original's story screen draws it: black bars close
/// over the game 4 lines a frame from the scene's transition value until past 164, the scene moves
/// once, then the bars open again 4 lines a frame until -48. The scene does not move during the
/// other frames, so the screen holds the game (the tick's Paused) while <see cref="Holds"/>. Scenes
/// without a transition, scenes of their own layout and the chapter screen do not slide.
/// </summary>
public sealed class SceneSlide
{
    /// <summary>Scene kinds from this one on have their own layout and never slide.</summary>
    public const int OwnLayoutScene = 27;

    private const int Step = 4, ClosedPast = 160, OpenFrom = -48;

    private int bars;
    private bool firstFrame = true;

    /// <summary>The slide of a scene of <paramref name="sceneKind"/> with <paramref name="transition"/>.</summary>
    public SceneSlide(int transition, int sceneKind, bool chapterScreen)
    {
        bars = chapterScreen || sceneKind >= OwnLayoutScene || transition <= 0 ? 0 : transition;
    }

    /// <summary>No slide.</summary>
    public static SceneSlide None => new(0, 0, false);

    /// <summary>The slide of <paramref name="step"/>: only a scene's first step slides in.</summary>
    public static SceneSlide For(IReadOnlyList<StoryScene> scenes, SceneStep step)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(step);
        if (step.Step != 0 || step.Scene < 0 || step.Scene >= scenes.Count)
        {
            return None;
        }
        return new SceneSlide(scenes[step.Scene].Transition, step.Scene, step.ChapterScreen);
    }

    /// <summary>Whether the scene does not move on the next frame (the game is held for it).</summary>
    public bool Holds => !Frame(bars, firstFrame).Moves;

    /// <summary>
    /// How far the bars have closed, 0 (open) to 1 (the screen covered), for drawing: closing
    /// while positive, opening while negative.
    /// </summary>
    public float Closed => bars > 0 ? Math.Min(1f, bars / (float)(ClosedPast + Step))
        : bars < 0 ? Math.Min(1f, (-bars + OpenFrom) / (float)(ClosedPast + Step + OpenFrom)) : 0f;

    /// <summary>Whether the slide is over.</summary>
    public bool Done => bars == 0 && !Holds;

    /// <summary>One frame of the slide.</summary>
    public void Advance() => (bars, firstFrame, _) = Frame(bars, firstFrame);

    /// <summary>The frames the scene is held for a slide of <paramref name="transition"/>.</summary>
    public static int HeldFrames(int transition)
    {
        var slide = new SceneSlide(transition, 0, false);
        int held = 0;
        for (int frame = 0; frame < 1000 && (slide.bars != 0 || slide.Holds); frame++)
        {
            held += slide.Holds ? 1 : 0;
            slide.Advance();
        }
        return held;
    }

    /// <summary>The original's frame: the bars move, and whether the scene moves this frame.</summary>
    private static (int Bars, bool FirstFrame, bool Moves) Frame(int bars, bool firstFrame)
    {
        if (bars > 0)
        {
            bars += Step;
            if (bars - Step <= ClosedPast)
            {
                return (bars, firstFrame, false);
            }
            bars = -bars;
        }
        // The scene moves when the bars are open, and once on the frame they turn.
        bool moves = bars == 0 || firstFrame;
        if (bars < 0)
        {
            bars += Step;
            if (bars >= OpenFrom)
            {
                bars = 0;
            }
        }
        return (bars, false, moves);
    }
}
