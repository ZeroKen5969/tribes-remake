using Tribes.Core.Systems.WorldObjects;

namespace Tribes.Core.Simulation;

/// <summary>The world frame: every part of the rules that runs once per frame, in order.</summary>
public sealed partial class RuleSystems
{
    private SpottedTrapSweep? spottedTrapSweep;
    private WorldFrame? worldFrame;

    /// <summary>The sweep that clears spotted traps, a row per frame.</summary>
    internal SpottedTrapSweep SpottedTrapSweep => spottedTrapSweep ??= new SpottedTrapSweep(World);

    /// <summary>One frame of the world.</summary>
    internal WorldFrame WorldFrame => worldFrame ??= new WorldFrame(World, new WorldFrameParts(BuildingUpdate, UnitUpdate,
        WildlifeUpdate, ResourceUpdate, SpottedTrapSweep, MissionOutcome, BuildingWorkLists, BuildingLifecycle, Sight, EndlessUpdate), events);
}
