using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>The campaign's end: the closing story after its last mission.</summary>
public sealed partial class GameSimulation
{
    /// <summary>The campaign's closing dialogue (the original titles it 「劇終」, string s.103).</summary>
    public const int ClosingDialogue = ScenePlayer.ClosingDialogue;

    /// <summary>
    /// Whether the campaign's closing story follows now: the mission is won and the one after it
    /// opens with the closing dialogue (the original plays that dialogue in this world when the
    /// result screen of its last campaign mission is left, and never starts the next mission).
    /// </summary>
    public bool ClosingSceneFollows
    {
        get
        {
            IReadOnlyList<MissionRules> missions = World.Content.Rules.Missions.Missions;
            int next = World.Mission + 1;
            return Victory == true && World.Mode == WorldMode.Campaign && next < missions.Count
                && missions[next].OpeningDialogue == ClosingDialogue;
        }
    }

    /// <summary>
    /// Plays the closing story in this world (no new map), as the original does once the result
    /// screen of the campaign's last mission is left: on the chapter screen (its title 「劇終」), the
    /// world no longer counting as over so the story's steps run; it ends with a
    /// <see cref="SceneEnded"/> that does not resume play.
    /// False (and nothing changes) unless <see cref="ClosingSceneFollows"/>.
    /// </summary>
    public bool PlayClosingScene()
    {
        if (!ClosingSceneFollows)
        {
            return false;
        }
        World.EndCountdown = 0;
        systems.Scenes.Start(ClosingDialogue, chapterScreen: true);
        return true;
    }
}
