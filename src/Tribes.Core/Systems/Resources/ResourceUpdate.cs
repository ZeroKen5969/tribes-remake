using System;
using Tribes.Core.Systems.Story;

namespace Tribes.Core.Systems.Resources;

/// <summary>
/// The last part of a frame's world update: the resource nodes (rotting, growing, running out),
/// then the story (alarm calls, the chief, story steps, victory and defeat, the tutorial, timed
/// unlocks).
/// </summary>
internal sealed class ResourceUpdate
{
    private readonly NodeUpkeep nodes;
    private readonly StoryUpdate story;

    /// <summary>Runs <paramref name="nodes"/>, then <paramref name="story"/>.</summary>
    public ResourceUpdate(NodeUpkeep nodes, StoryUpdate story)
    {
        this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
    }

    /// <summary>Runs the nodes' and the story's part of the current frame; <paramref name="inputs"/> is what the player's screen shows.</summary>
    public void RunFrame(TutorialInputs inputs)
    {
        nodes.RunFrame();
        story.Advance(inputs);
    }
}
