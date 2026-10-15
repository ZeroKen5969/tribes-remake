using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>Starts story scenes (a dialogue with its actors and whatever the scene changes in the world).</summary>
internal interface IStoryDirector
{
    /// <summary>Starts the scene of dialogue <paramref name="dialogue"/> with the actors cast so far.</summary>
    void StartScene(int dialogue);
}

/// <summary>
/// The mission's story: which step comes next, casting the actors of a scene from the player's
/// tribe, and moving on to the next step when its scene starts.
/// </summary>
internal sealed class StoryProgress
{
    /// <summary>Story step trigger: an animal or an enemy unit is killed.</summary>
    public const int KillTrigger = 10;

    /// <summary>
    /// Added to <see cref="ScriptState.ObjectiveStage"/> while the scene of a reached step plays; the
    /// scene's end takes it off again and moves to the next step.
    /// </summary>
    public const int ScenePlaying = 50;

    /// <summary>Actor positions a scene is cast into.</summary>
    private const int FirstActor = 0, Chief = 1, Speaker = 2, LastCast = 4, Subject = 5;

    /// <summary>The first free actor position when the scene's first actor is the chief (or nobody).</summary>
    private const int FirstFreeWithChief = 2;

    private readonly GameWorld world;
    private readonly IStoryDirector director;

    /// <summary>Follows the story of the mission played in <paramref name="world"/>.</summary>
    public StoryProgress(GameWorld world, IStoryDirector director)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.director = director ?? throw new ArgumentNullException(nameof(director));
    }

    /// <summary>The story step the mission waits for, or null when the story is over or a scene is playing.</summary>
    public StoryStep? NextStep
    {
        get
        {
            IReadOnlyList<StoryStep> steps = world.Content.Rules.Missions.Missions[world.Mission].StorySteps;
            int stage = world.Script.ObjectiveStage;
            return stage >= 0 && stage < steps.Count ? steps[stage] : null;
        }
    }

    /// <summary>
    /// Casts a scene: the chief plays the first two parts and <paramref name="subject"/> is what the
    /// scene is about; <paramref name="lead"/> (0 for none) takes the first, third, fourth and fifth
    /// parts, and the remaining parts up to the fifth go to the player's other adults in slot
    /// order, from slot <paramref name="firstCandidate"/> on.
    /// </summary>
    public void CastActors(int lead, int subject, int firstCandidate)
    {
        ScriptState script = world.Script;
        int chief = world.Tribes[TribeRoles.Player(world)][TribeCounter.Chief];
        script.SetActor(Chief, chief);
        script.SetActor(Speaker, chief);
        script.SetActor(Subject, subject);
        if (lead != 0)
        {
            for (int part = FirstActor; part <= LastCast; part++)
            {
                if (part != Chief)
                {
                    script.SetActor(part, lead);
                }
            }
        }
        int next = lead != chief && lead != 0 ? FirstFreeWithChief + 1 : FirstFreeWithChief;
        foreach (Unit unit in world.UnitsOf(TribeRoles.Player(world)))
        {
            int id = unit.Id;
            if (next > LastCast)
            {
                break;
            }
            if (id >= firstCandidate && unit.HitPoints > 0 && unit.Age >= UnitAges.Adult && id != lead && id != chief)
            {
                script.SetActor(next++, id);
            }
        }
    }

    /// <summary>Starts the scene of <paramref name="step"/> and marks the step reached.</summary>
    public void Reach(StoryStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        director.StartScene(step.Dialogue);
        world.Script.ObjectiveStage += ScenePlaying;
    }

    /// <summary>Starts a scene that is not a story step (an unlock or a tutorial scene).</summary>
    public void StartScene(int dialogue) => director.StartScene(dialogue);
}
