using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Vision;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// Plays story scenes: a scene casts its actors and goes through its dialogue's pages one step at
/// a time; the host shows each step (<see cref="SceneStepShown"/>) and moves on when the player
/// taps (<see cref="Continue"/>). In a staged step the actors are posed and the world runs a few
/// frames (<see cref="AdvanceStep"/>). When the last step is over the actors are released, the
/// mission moves on to its next objective and some scenes lead straight into the next one.
/// </summary>
internal sealed class ScenePlayer : IStoryDirector
{
    /// <summary>The action of an actor posed by a scene.</summary>
    public const int StagedAction = 105;

    /// <summary>The step frame of a step with nothing (more) to run: it waits for the player's tap.</summary>
    public const int AwaitsTap = -1;

    /// <summary>The step frame of a step whose frames have run: it goes on by itself on the next display frame.</summary>
    public const int ContinuesByItself = -2;

    /// <summary>The scene layout kind of the council scene.</summary>
    public const int CouncilKind = 9;

    /// <summary>The scene a dialogue's first page shows when it names none (a layout of its own).</summary>
    public const int OwnLayoutScene = 27;

    /// <summary>The action of a released actor who is inside a building.</summary>
    private const int InsideAction = ActionNumbers.WitchDoctor;

    /// <summary>The dialogue whose first page always shows <see cref="OwnLayoutScene"/>.</summary>
    private const int OwnLayoutDialogue = 4;

    /// <summary>The page camera code that focuses on the first actor.</summary>
    private const int CameraOnFirstActor = 99;

    /// <summary>Scene modes: a full-screen picture; from this one on, the actors are staged.</summary>
    private const int PictureMode = 1, StagedMode = 2;

    /// <summary>The highest actor slot with a pose of its own; later slots use its pose.</summary>
    private const int LastPosedSlot = 6;

    /// <summary>A pose's facing is stored plus this.</summary>
    private const int FacingCodeOffset = 10;

    /// <summary>The scene after which the objective timer waits for a trigger to start it.</summary>
    private const int TimerWaitsDialogue = 15;

    /// <summary>The objective timer's value while it waits to be started.</summary>
    public const int TimerNotStarted = 1000000;

    /// <summary>The dialogue after which a step's scene is finished but nothing is resumed: the campaign's end.</summary>
    public const int ClosingDialogue = 59;

    /// <summary>The scene shown on the chapter screen when it follows another one.</summary>
    private const int ClosingChapterDialogue = 47;

    /// <summary>The tutorial scene that leads into the next one only while the tutorial flags are set.</summary>
    private const int TutorialLeadIn = 2;

    /// <summary>Scenes followed straight away by the next dialogue.</summary>
    private static readonly HashSet<int> LeadsIntoNext = new() { 0, 1, 32, 36, 37, 39, 46, 47, 49, 55 };

    private readonly GameWorld world;
    private readonly StoryTables story;
    private readonly SightSystem sight;
    private readonly SceneScripts scripts;
    private readonly Action<GameEvent> raise;

    /// <summary>Plays scenes in <paramref name="world"/>, telling the host through <paramref name="raise"/>.</summary>
    public ScenePlayer(GameWorld world, SightSystem sight, SceneScripts scripts, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
        this.scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        story = world.Content.Rules.Story;
    }

    /// <summary>The step being shown, or null when no scene is playing.</summary>
    public SceneStep? Current => world.Script.Active ? Describe() : null;

    /// <summary>Whether the player may go on to the next step now (the step's staged frames have all run or not started).</summary>
    public bool CanContinue => world.Script.Active && world.Script.StepFrame <= 0;

    /// <inheritdoc />
    public void StartScene(int dialogue) => Start(dialogue, chapterScreen: false);

    /// <summary>
    /// Starts the scene of <paramref name="dialogue"/>: its cast list fills the first actor slots
    /// (the others keep the actors the rules cast), the scene's own start script runs and its first
    /// step is shown.
    /// </summary>
    public void Start(int dialogue, bool chapterScreen)
    {
        ScriptState script = world.Script;
        Dialogue entry = story.Dialogues[dialogue];
        script.SceneId = dialogue;
        script.Step = 0;
        script.TableBase = entry.FirstPage;
        int kind = story.Pages[entry.FirstPage].Scene;
        script.SceneKind = dialogue == OwnLayoutDialogue || kind == 0 ? OwnLayoutScene : kind;
        script.StepCount = entry.PageCount;
        script.Active = true;
        script.ChapterScreen = chapterScreen;
        IReadOnlyList<int> cast = story.CastLists[entry.Cast];
        for (int slot = 0; slot < cast.Count; slot++)
        {
            script.SetActor(slot, cast[slot]);
        }
        scripts.OnStart(dialogue);
        ShowStep();
    }

    /// <summary>The player taps on: the next step is shown, or after the last one the scene ends.</summary>
    public void Continue()
    {
        ScriptState script = world.Script;
        if (!script.Active)
        {
            return;
        }
        if (script.Step == script.StepCount - 1)
        {
            End();
            return;
        }
        script.Step++;
        ShowStep();
    }

    /// <summary>
    /// One display frame of the step shown (call it once the step's slide-in is over). In a staged
    /// step the actors take the scene's pose on the first one; then the world runs one frame per
    /// call until the step's frames have run. A full-screen picture about nobody stays up for the
    /// scene's frames and then goes on by itself. Other steps never run the world.
    /// </summary>
    public SceneFrame AdvanceStep()
    {
        ScriptState script = world.Script;
        SceneStep? step = Current;
        if (step is null || script.StepFrame < 0)
        {
            return SceneFrame.Hold;
        }
        if (!step.ChapterScreen && story.Scenes[step.Scene].Mode == PictureMode)
        {
            return AdvancePicture(step);
        }
        if (!step.Staged || step.Scene <= 0)
        {
            return SceneFrame.Hold;
        }
        StoryScene scene = story.Scenes[step.Scene];
        if (script.StepFrame == 0 && scene.PoseOrPicture > 0)
        {
            PoseActors(story.Poses[scene.PoseOrPicture]);
        }
        if (script.StepFrame >= scene.DurationFrames)
        {
            bool silent = step.Speaker == 0;
            script.StepFrame = silent ? ContinuesByItself : AwaitsTap;
            return silent ? SceneFrame.AutoContinue : SceneFrame.Hold;
        }
        script.StepFrame++;
        return SceneFrame.AdvanceWorld;
    }

    /// <summary>A picture step about nobody counts its frames, then goes on by itself; one about a unit waits for a tap.</summary>
    private SceneFrame AdvancePicture(SceneStep step)
    {
        ScriptState script = world.Script;
        if (step.FocusUnit != 0)
        {
            return SceneFrame.Hold;
        }
        if (script.StepFrame < story.Scenes[step.Scene].DurationFrames)
        {
            script.StepFrame++;
            return SceneFrame.Hold;
        }
        if (step.Scene == 0)
        {
            return SceneFrame.Hold;
        }
        script.StepFrame = ContinuesByItself;
        return SceneFrame.AutoContinue;
    }

    private void ShowStep()
    {
        ScriptState script = world.Script;
        DialoguePage page = story.Pages[script.TableBase + script.Step];
        int scene = StepScene();
        script.StepFrame = story.Scenes[scene].DurationFrames < 0 ? AwaitsTap : 0;
        script.FocusUnit = script.Actor(page.Camera == CameraOnFirstActor ? 0 : page.Speaker);
        raise(new SceneStepShown(Describe()));
    }

    /// <summary>The scene of the current step: the first step shows the dialogue's layout kind.</summary>
    private int StepScene()
    {
        ScriptState script = world.Script;
        return script.Step == 0 ? script.SceneKind : story.Pages[script.TableBase + script.Step].Scene;
    }

    private SceneStep Describe()
    {
        ScriptState script = world.Script;
        int pageIndex = script.TableBase + script.Step;
        DialoguePage page = story.Pages[pageIndex];
        int scene = StepScene();
        int mode = story.Scenes[scene].Mode;
        bool staged = !script.ChapterScreen && mode != PictureMode
            && (mode >= StagedMode || story.Scenes[script.SceneKind].Mode >= StagedMode);
        IReadOnlyList<int> chapters = story.ChapterIllustrations;
        int illustration = chapters[Math.Min(world.Mission, chapters.Count - 1)] + script.Step;
        return new SceneStep(script.SceneId, script.Step, script.StepCount, pageIndex, page.Speaker, script.FocusUnit,
            scene, illustration, page.Camera, script.ChapterScreen, staged,
            staged ? story.Scenes[scene].DurationFrames : -1);
    }

    private void PoseActors(StagePose pose)
    {
        ScriptState script = world.Script;
        for (int slot = 0; slot < ScriptState.ActorSlots; slot++)
        {
            if (SceneActors.Unit(world, slot) is not Unit actor)
            {
                continue;
            }
            ActorPose part = pose.Actors[Math.Min(slot, LastPosedSlot)];
            if (part.Animation != 0)
            {
                actor.Animation = part.Animation;
                actor.ActionSubState = part.SubState;
                actor.Action = StagedAction;
            }
            if (part.FacingCode != 0)
            {
                actor.Facing = part.FacingCode - FacingCodeOffset;
                actor.PathLength = 0;
            }
            actor.AnimationTick = 0;
        }
    }

    /// <summary>The scene is over: its end script runs, the actors are released and the objectives move on.</summary>
    private void End()
    {
        ScriptState script = world.Script;
        int dialogue = script.SceneId;
        sight.UpdateSight();
        scripts.OnEnd(dialogue, script.SceneKind);
        ReleaseActors();
        if (script.ObjectiveStage >= StoryProgress.ScenePlaying)
        {
            script.ObjectiveTimer = dialogue == TimerWaitsDialogue ? TimerNotStarted : world.Frame;
            script.ObjectiveStage -= StoryProgress.ScenePlaying - 1;
            IReadOnlyList<int> limits = story.TutorialStepLimitByStoryStep;
            if (script.ObjectiveStage <= limits.Count)
            {
                script.TutorialObjective = limits[script.ObjectiveStage - 1];
            }
            script.TutorialTimer = 0;
        }
        script.Active = false;
        bool leadsOn = LeadsIntoNext.Contains(dialogue) || (dialogue == TutorialLeadIn && script.TutorialFlags != 0);
        if (leadsOn)
        {
            Start(dialogue + 1, chapterScreen: dialogue + 1 == ClosingChapterDialogue);
            return;
        }
        raise(new SceneEnded(dialogue, dialogue != ClosingDialogue));
    }

    /// <summary>Actors still posed go back to idling (or to their work inside a building); every actor slot is emptied.</summary>
    private void ReleaseActors()
    {
        ScriptState script = world.Script;
        for (int slot = 0; slot < ScriptState.ActorSlots; slot++)
        {
            if (SceneActors.Unit(world, slot) is Unit actor && actor.Action == StagedAction)
            {
                actor.Action = actor.BuildingSlot < 0 ? ActionNumbers.Idle : InsideAction;
            }
            script.SetActor(slot, 0);
        }
    }
}
