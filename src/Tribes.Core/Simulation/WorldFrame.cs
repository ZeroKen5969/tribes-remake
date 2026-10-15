using System;
using System.Collections.Generic;
using Tribes.Core.Systems.Buildings;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Resources;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.Systems.Vision;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>The rule systems one world frame runs, in the frame's order.</summary>
internal sealed record WorldFrameParts(BuildingUpdate Buildings, UnitUpdate Units, WildlifeUpdate Wildlife,
    ResourceUpdate Resources, SpottedTrapSweep TrapSweep, MissionOutcome Outcome, BuildingWorkLists WorkLists,
    BuildingLifecycle Lifecycle, SightSystem Sight, Systems.Endless.EndlessUpdate Endless);

/// <summary>
/// One frame of the world: the buildings, the units, the wild animals, the resource nodes and the
/// story, then the frame counter, the spotted-trap sweep, the end-of-mission countdown, the dust
/// clouds and the buildings coming into view; last, in an endless world, its own part (new land,
/// new tribes, alliance invitations).
/// </summary>
internal sealed class WorldFrame
{
    private readonly GameWorld world;
    private readonly WorldFrameParts parts;
    private readonly IReadOnlyList<GameEvent> events;

    /// <summary>
    /// Runs frames of <paramref name="world"/> with <paramref name="parts"/>; <paramref name="events"/>
    /// is the live list of events raised and not taken yet (a hint shown pauses the world).
    /// </summary>
    public WorldFrame(GameWorld world, WorldFrameParts parts, IReadOnlyList<GameEvent> events)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.parts = parts ?? throw new ArgumentNullException(nameof(parts));
        this.events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>
    /// Runs one frame. <paramref name="staged"/>: a frame a story scene runs while it is shown.
    /// The end-of-mission countdown only moves on normal frames on which the world keeps running:
    /// no scene started and, when <paramref name="hintsPause"/>, no hint was shown. Returns the
    /// mission's result when it was announced on this frame.
    /// </summary>
    public bool? Run(bool staged, TutorialInputs inputs, bool hintsPause)
    {
        int firstEvent = events.Count;
        parts.Buildings.RunFrame();
        parts.Units.RunFrame();
        parts.Wildlife.RunFrame();
        parts.Resources.RunFrame(inputs);
        world.Frame++;
        parts.TrapSweep.Advance();
        bool keepsRunning = !staged && !world.Script.Active && !(hintsPause && HintShownSince(firstEvent));
        bool? result = parts.Outcome.AdvanceCountdown(keepsRunning);
        parts.WorkLists.AdvanceDust(parts.Lifecycle.Free);
        parts.Sight.RevealSeenBuildings();
        parts.Endless.Advance();
        return result;
    }

    /// <summary>Whether a hint was shown since event <paramref name="first"/>.</summary>
    public bool HintShownSince(int first)
    {
        for (int i = first; i < events.Count; i++)
        {
            if (events[i] is HintEvent)
            {
                return true;
            }
        }
        return false;
    }
}
