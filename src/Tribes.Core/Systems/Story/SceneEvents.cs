using Tribes.Core.Simulation;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// One step (page) of a story scene as the presentation shows it. The host shows the page's text
/// and picture, plays the staged frames (see <see cref="RuleSystems.AdvanceSceneStep"/>) and calls
/// <see cref="RuleSystems.ContinueScene"/> when the player taps on (or when a step without a
/// speaker has run its frames).
/// </summary>
/// <param name="Dialogue">The dialogue (row of <c>GameRules.Story.Dialogues</c>; the scene's number).</param>
/// <param name="Step">The step, from 0.</param>
/// <param name="StepCount">The dialogue's number of steps.</param>
/// <param name="Page">The page shown (row of <c>GameRules.Story.Pages</c>).</param>
/// <param name="Speaker">The actor slot speaking (0 for none).</param>
/// <param name="FocusUnit">The unit the step is about (the speaker, or the first actor), 0 for none.</param>
/// <param name="Scene">The scene picture or staging (row of <c>GameRules.Story.Scenes</c>).</param>
/// <param name="Illustration">The chapter illustration of the step.</param>
/// <param name="Camera">Where the view goes (as <c>DialoguePage.Camera</c>).</param>
/// <param name="ChapterScreen">Shown on the chapter screen between missions: nothing is staged.</param>
/// <param name="Staged">Whether the actors are posed and the world runs during the step.</param>
/// <param name="WorldFrames">World frames the step runs when staged; -1 when it has no timer.</param>
public sealed record SceneStep(int Dialogue, int Step, int StepCount, int Page, int Speaker, int FocusUnit,
    int Scene, int Illustration, int Camera, bool ChapterScreen, bool Staged, int WorldFrames);

/// <summary>A story scene shows a step; the world is paused for the player (it only runs staged frames).</summary>
/// <param name="Step">The step.</param>
public sealed record SceneStepShown(SceneStep Step) : GameEvent;

/// <summary>A story scene ended.</summary>
/// <param name="Dialogue">The scene's dialogue.</param>
/// <param name="ResumesPlay">
/// Whether the game goes on; false for the campaign's closing scene, after which the host returns
/// to its menus.
/// </param>
public sealed record SceneEnded(int Dialogue, bool ResumesPlay) : GameEvent;

/// <summary>What the host does on a display frame while a scene step is shown.</summary>
public enum SceneFrame
{
    /// <summary>Nothing more to run; wait for the player.</summary>
    Hold,

    /// <summary>Run one world frame (a staged frame: the end-of-mission countdown does not run).</summary>
    AdvanceWorld,

    /// <summary>The step's frames have run and nobody speaks: go on to the next step.</summary>
    AutoContinue,
}
