using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// The story's part of each frame, after the resource nodes: alarm calls, the chief, the
/// mission's story steps, victory and defeat, the tutorial and the building levels unlocked over
/// time. A step that starts a scene ends the story's part for the frame. A match has no story:
/// after the alarm calls its own part runs (<see cref="Match.MatchUpdate"/>).
/// </summary>
internal sealed class StoryUpdate
{
    /// <summary>The ambush mission has scenes of its own instead of the usual story steps.</summary>
    private const int AmbushMission = 4;

    /// <summary>Levels are unlocked over time in missions up to this one.</summary>
    private const int LastUnlockMission = 4;

    /// <summary>Frames without fighting before a level is unlocked.</summary>
    private const int QuietFrames = 50;

    /// <summary>The unit slot unlock scenes cast extras from.</summary>
    private const int FirstExtraCandidate = 3;

    private readonly GameWorld world;
    private readonly AlarmCalls alarms;
    private readonly StoryTriggers triggers;
    private readonly MissionOutcome outcome;
    private readonly Tutorial tutorial;
    private readonly UnlockLevels unlocks;
    private readonly StoryProgress story;
    private readonly Match.MatchUpdate match;

    /// <summary>Runs the story of <paramref name="world"/>.</summary>
    public StoryUpdate(GameWorld world, AlarmCalls alarms, StoryTriggers triggers, MissionOutcome outcome, Tutorial tutorial,
        UnlockLevels unlocks, StoryProgress story, Match.MatchUpdate match)
    {
        this.match = match ?? throw new ArgumentNullException(nameof(match));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.alarms = alarms ?? throw new ArgumentNullException(nameof(alarms));
        this.triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
        this.outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        this.tutorial = tutorial ?? throw new ArgumentNullException(nameof(tutorial));
        this.unlocks = unlocks ?? throw new ArgumentNullException(nameof(unlocks));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
    }

    /// <summary>The story's part of a frame; <paramref name="inputs"/> is what the player's screen shows.</summary>
    public void Advance(TutorialInputs inputs)
    {
        alarms.Update();
        if (world.Mode == WorldMode.Match)
        {
            match.Advance();
            return;
        }
        if (triggers.CheckChief())
        {
            return;
        }
        bool sceneStarted = world.Mission == AmbushMission ? triggers.CheckAmbushScenes() : triggers.CheckObjective();
        if (sceneStarted)
        {
            return;
        }
        outcome.Check();
        tutorial.Advance(inputs);
        if (world.Mission <= LastUnlockMission)
        {
            UnlockDueLevel();
        }
    }

    /// <summary>
    /// When the game is quiet and the next timed unlock is due, it is given (with the one after it
    /// if that is due too), the levels are worked out again and its scene starts.
    /// </summary>
    private void UnlockDueLevel()
    {
        IReadOnlyList<TechUnlock> list = world.Content.Rules.Missions.Missions[world.Mission].TechUnlocks;
        int next = world.Script.UnlocksFired;
        if (world.FramesSinceCombat < QuietFrames || next < 0 || next >= list.Count
            || world.Frame < list[next].TimeUnits * UnlockLevels.FramesPerTimeUnit)
        {
            return;
        }
        bool followingDue = next + 1 < list.Count && world.Frame >= list[next + 1].TimeUnits * UnlockLevels.FramesPerTimeUnit;
        world.Script.UnlocksFired += followingDue ? 2 : 1;
        unlocks.Recompute();
        story.CastActors(0, 0, FirstExtraCandidate);
        story.StartScene(list[next].Dialogue);
    }
}
