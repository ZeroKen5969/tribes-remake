using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>A scene of the story player: a full-screen picture or a staging of the actors in the game.</summary>
/// <param name="Id">Scene number 0..27.</param>
/// <param name="Mode">
/// 1 an animated full-screen picture; 2 or more the actors are staged in the game (action 105 with
/// the poses of <see cref="PoseOrPicture"/>); 0 neither.
/// </param>
/// <param name="PoseOrPicture">The picture (mode 1) or the pose (index into <see cref="StoryTables.Poses"/>, mode 2).</param>
/// <param name="Transition">How the scene slides in; 1 starts at once (unconfirmed).</param>
/// <param name="DurationFrames">Frames before the scene moves on; negative: no timer.</param>
public sealed record StoryScene(int Id, int Mode, int PoseOrPicture, int Transition, int DurationFrames);

/// <summary>How one actor of a staged scene is posed.</summary>
/// <param name="Animation">The animation number set, or 0 to keep the current one.</param>
/// <param name="FacingCode">The facing + 10 set (the route is cleared), or 0 to keep the facing.</param>
/// <param name="SubState">The action sub-state set with the animation.</param>
public sealed record ActorPose(int Animation, int FacingCode, int SubState);

/// <summary>A staging of the actors.</summary>
/// <param name="Id">Pose number.</param>
/// <param name="Actors">The pose of actor slots 0..6 (later slots use slot 6's).</param>
public sealed record StagePose(int Id, IReadOnlyList<ActorPose> Actors);

/// <summary>A page of a dialogue.</summary>
/// <param name="Speaker">The actor slot speaking.</param>
/// <param name="Scene">The scene shown with the page (index into <see cref="StoryTables.Scenes"/>).</param>
/// <param name="Camera">
/// Where the view goes: more than 100 a literal value, 99 actor 0, 1..98 an actor slot, -100 special, other negatives other effects.
/// </param>
public sealed record DialoguePage(int Speaker, int Scene, int Camera);

/// <summary>A dialogue: consecutive pages shown together.</summary>
/// <param name="Id">Dialogue number, as the rules show it.</param>
/// <param name="FirstPage">Its first page (index into <see cref="StoryTables.Pages"/>).</param>
/// <param name="PageCount">Its number of pages.</param>
/// <param name="Cast">The cast list its actors come from (index into <see cref="StoryTables.CastLists"/>).</param>
public sealed record Dialogue(int Id, int FirstPage, int PageCount, int Cast);

/// <summary>A step of the tutorial.</summary>
/// <param name="Id">Step number.</param>
/// <param name="Dialogue">The dialogue shown when the step completes.</param>
/// <param name="Trigger">
/// 0 time only; 1 a building of kind <see cref="Parameter"/> stands; 2 a unit does an action of
/// category <see cref="Parameter"/>; 3 the player confirms; 4 the enemy population is gone.
/// </param>
/// <param name="Parameter">The trigger's building kind or action category.</param>
/// <param name="Delay">Eight-frame steps to wait (after the trigger, or in total for time-only steps).</param>
public sealed record TutorialStep(int Id, int Dialogue, int Trigger, int Parameter, int Delay);

/// <summary>A hint the game may show.</summary>
/// <param name="Id">Hint number.</param>
/// <param name="Channel">
/// The cool-down channel: the hint is shown only when the channel is free, which it is again
/// <see cref="StoryTables.HintChannelCooldowns"/> frames after showing.
/// </param>
/// <param name="MinMission">The hint is shown only from this mission on.</param>
public sealed record Hint(int Id, int Channel, int MinMission);

/// <summary>Story scenes, dialogues, the tutorial and hints.</summary>
/// <param name="Scenes">Story scenes by number.</param>
/// <param name="Poses">Stage poses by number.</param>
/// <param name="CastLists">The default actor slots of each cast list.</param>
/// <param name="ChapterIllustrations">
/// The first illustration of each mission's chapter (0..7), then of the closing scene.
/// </param>
/// <param name="Dialogues">Dialogues by number.</param>
/// <param name="Pages">Dialogue pages by number.</param>
/// <param name="TutorialSteps">Tutorial steps by number.</param>
/// <param name="TutorialStepLimitByStoryStep">
/// While the story is at step s, tutorial steps run up to (not including) TutorialStepLimitByStoryStep[s].
/// </param>
/// <param name="Hints">Hints by number.</param>
/// <param name="HintChannelCooldowns">Frames before each hint channel is free again.</param>
/// <param name="UnusedCast">A cast entry after the last dialogue that nothing reads; kept as data.</param>
public sealed record StoryTables(IReadOnlyList<StoryScene> Scenes, IReadOnlyList<StagePose> Poses,
    IReadOnlyList<IReadOnlyList<int>> CastLists, IReadOnlyList<int> ChapterIllustrations,
    IReadOnlyList<Dialogue> Dialogues, IReadOnlyList<DialoguePage> Pages, IReadOnlyList<TutorialStep> TutorialSteps,
    IReadOnlyList<int> TutorialStepLimitByStoryStep, IReadOnlyList<Hint> Hints,
    IReadOnlyList<int> HintChannelCooldowns, int UnusedCast);
