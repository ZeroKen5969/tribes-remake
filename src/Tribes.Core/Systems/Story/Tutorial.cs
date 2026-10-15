using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>What the player's screen shows that the tutorial looks at, passed in by the host each frame.</summary>
/// <param name="RosterOpen">Whether the unit roster is open (a building step does not count while it is).</param>
public readonly record struct TutorialInputs(bool RosterOpen);

/// <summary>
/// The tutorial of the first two missions: a list of steps, each shown as a scene once its
/// trigger has held for its delay (or, for time-only steps, after its delay). Each story stage
/// allows the steps up to its limit.
/// </summary>
internal sealed class Tutorial
{
    /// <summary>The tutorial step after which the computer's first attack is scheduled.</summary>
    public const int AttackScheduledStep = 11;

    /// <summary>Frames from that step to the computer's first attack.</summary>
    public const int FirstAttackDelay = 500;

    /// <summary>Trigger kinds of a tutorial step.</summary>
    private const int TimeOnly = 0, BuildingStands = 1, ActionDone = 2, PlayerConfirms = 3, EnemyGoneAllOut = 4;

    /// <summary>A triggered step's timer runs up to this value; the trigger sets it to this minus the step's delay.</summary>
    private const int TriggeredEnd = 120;

    /// <summary>The timer counts one step every this many frames (a mask of the frame number).</summary>
    private const int TimerMask = 7;

    /// <summary>Bit of the tutorial flags that switches the tutorial on.</summary>
    private const int TutorialOn = 1;

    /// <summary>The tutorial runs in missions before this one.</summary>
    private const int TutorialMissions = 2;

    /// <summary>Unit slot the tutorial's scenes cast extras from.</summary>
    private const int FirstExtraCandidate = 3;

    private readonly GameWorld world;
    private readonly StoryProgress story;
    private readonly IReadOnlyList<TutorialStep> steps;

    /// <summary>Runs the tutorial of <paramref name="world"/>.</summary>
    public Tutorial(GameWorld world, StoryProgress story)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        steps = world.Content.Rules.Story.TutorialSteps;
    }

    /// <summary>One frame of the tutorial: the current step's trigger and timer; when it is due its scene starts.</summary>
    public void Advance(TutorialInputs inputs)
    {
        ScriptState script = world.Script;
        IReadOnlyList<int> limits = world.Content.Rules.Story.TutorialStepLimitByStoryStep;
        int stage = script.ObjectiveStage;
        if ((script.TutorialFlags & TutorialOn) == 0 || world.Mission >= TutorialMissions || stage < 0
            || stage >= limits.Count || script.TutorialObjective >= limits[stage] || script.AwaitingNewChief)
        {
            return;
        }
        TutorialStep step = steps[script.TutorialObjective];
        int triggered = TriggeredEnd - step.Delay;
        if (script.TutorialTimer < triggered)
        {
            CheckTrigger(step, triggered, inputs);
        }
        if ((world.Frame & TimerMask) == 0)
        {
            script.TutorialTimer++;
        }
        if (script.TutorialTimer >= (step.Trigger == TimeOnly ? step.Delay : TriggeredEnd))
        {
            script.TutorialTimer = 0;
            story.CastActors(0, 0, FirstExtraCandidate);
            story.StartScene(step.Dialogue);
            script.TutorialObjective++;
            if (script.TutorialObjective == AttackScheduledStep)
            {
                world.Ai.NextWaveFrame = world.Frame + FirstAttackDelay;
            }
        }
    }

    /// <summary>
    /// The player opened the game menu: a step waiting for the player's confirmation counts as
    /// triggered (whether or not the tutorial is on).
    /// </summary>
    public void PlayerOpenedMenu()
    {
        ScriptState script = world.Script;
        if (script.TutorialObjective >= 0 && script.TutorialObjective < steps.Count
            && steps[script.TutorialObjective].Trigger == PlayerConfirms)
        {
            script.TutorialTimer = TriggeredEnd - steps[script.TutorialObjective].Delay;
        }
    }

    private void CheckTrigger(TutorialStep step, int triggered, TutorialInputs inputs)
    {
        ScriptState script = world.Script;
        switch (step.Trigger)
        {
            case BuildingStands:
                script.TutorialTimer = !inputs.RosterOpen && PlayerHasBuilding(step.Parameter) ? triggered : 0;
                break;
            case ActionDone:
                if (PlayerDoesAction(step.Parameter))
                {
                    script.TutorialTimer = triggered;
                }
                break;
            case EnemyGoneAllOut:
                if (world.Tribes[TribeRoles.Computer(world)][TribeCounter.Population] == 0 && world.Orders.AllOut)
                {
                    script.TutorialTimer = triggered;
                }
                break;
        }
    }

    private bool PlayerHasBuilding(int kind)
    {
        foreach (Building building in world.BuildingsOf(TribeRoles.Player(world)))
        {
            if (building.HitPoints > 0 && building.Kind == kind)
            {
                return true;
            }
        }
        return false;
    }

    private bool PlayerDoesAction(int category)
    {
        IReadOnlyList<ActionDefinition> actions = world.Content.Rules.Actions.Actions;
        foreach (Unit unit in world.UnitsOf(TribeRoles.Player(world)))
        {
            if (unit.HitPoints > 0 && actions[unit.Action].Category == category)
            {
                return true;
            }
        }
        return false;
    }
}
