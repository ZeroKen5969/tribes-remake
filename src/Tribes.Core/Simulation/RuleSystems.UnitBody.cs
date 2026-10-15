using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Senses;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>
/// The units' bodies and movement: ageing, needs, mood, disease, thoughts, hiding, couples, the
/// start of a unit's turn, stepping between cells, going into and out of buildings and animations.
/// The units' call cycle (deciding anew, planning routes, needs, work) is reached through
/// delegates, so the work systems may in turn use these.
/// </summary>
public sealed partial class RuleSystems
{
    /// <summary>The mark of a sanctuary's spell order whose spell is being cast.</summary>
    private const int SpellBeingCast = 1;

    private UnitNeeds? unitNeeds;
    private UnitAgeing? unitAgeing;
    private UnitMood? unitMood;
    private UnitDisease? unitDisease;
    private UnitThoughts? unitThoughts;
    private UnitStealth? unitStealth;
    private UnitHousekeeping? unitHousekeeping;
    private WorkPositions? workPositions;
    private Arrival? arrival;
    private StepMotion? stepMotion;
    private Chatting? chatting;
    private AnimationChooser? animationChooser;
    private ActionAnimation? actionAnimation;
    private Interruptions? interruptions;
    private PlaceSwap? placeSwap;
    private Waypoints? waypoints;
    private MovementDriver? movementDriver;
    private BuildingEntry? buildingEntry;
    private BuildingExit? buildingExit;
    private AnimationDriver? animationDriver;
    private Courtship? courtship;

    /// <summary>Hunger, tiredness, healing and wounds.</summary>
    internal UnitNeeds UnitNeeds => unitNeeds ??= new UnitNeeds(World, UnitLifecycle, Hints, Deaths);

    /// <summary>Growing older, then the needs and the protection spell.</summary>
    internal UnitAgeing UnitAgeing => unitAgeing ??= new UnitAgeing(World, UnitActions, UnitSpawning, UnitNeeds);

    /// <summary>The units' mood.</summary>
    internal UnitMood UnitMood => unitMood ??= new UnitMood(World);

    /// <summary>Catching and spreading the disease.</summary>
    internal UnitDisease UnitDisease => unitDisease ??= new UnitDisease(World);

    /// <summary>The thought bubbles of the player's adults.</summary>
    internal UnitThoughts UnitThoughts => unitThoughts ??= new UnitThoughts(World);

    /// <summary>Scouts hiding and being noticed.</summary>
    internal UnitStealth UnitStealth => unitStealth ??= new UnitStealth(World, SpiralSearch);

    /// <summary>The start of a unit's turn: dying, ageing, body, mood, home and disease.</summary>
    internal UnitHousekeeping UnitHousekeeping => unitHousekeeping ??=
        new UnitHousekeeping(World, UnitLifecycle, Occupancy, Deaths, Projectiles, UnitAgeing, UnitMood, UnitDisease);

    /// <summary>The work positions of the building kinds, as the movement rules look them up.</summary>
    internal WorkPositions WorkPositions => workPositions ??= new WorkPositions(World.Content.Rules.Buildings.Kinds);

    /// <summary>Arriving inside a building through its entrance.</summary>
    internal Arrival Arrival => arrival ??=
        new Arrival(World, UnitActions, UnitOrders, Occupancy, TargetActions, WorkPositions, Raise);

    /// <summary>Stepping from cell to cell.</summary>
    internal StepMotion StepMotion => stepMotion ??= new StepMotion(World, Arrival);

    /// <summary>Idle tribe mates chatting.</summary>
    internal Chatting Chatting => chatting ??= new Chatting(World, GridDistances);

    /// <summary>Picking the animation of an action step.</summary>
    internal AnimationChooser AnimationChooser => animationChooser ??= new AnimationChooser(World, Routes, Chatting, WorkPositions);

    /// <summary>Starting a unit's next action step.</summary>
    internal ActionAnimation ActionAnimation => actionAnimation ??= new ActionAnimation(World, UnitActions, Routes,
        AnimationChooser, StoryProgress, unit => ActionPauses.Pause(unit), unit => Spells.MarkCasting(unit, SpellBeingCast));

    /// <summary>Whether busy units let themselves be disturbed.</summary>
    internal Interruptions Interruptions => interruptions ??= new Interruptions(World, UnitActions, ActionAnimation, WorkPositions);

    /// <summary>Tribe mates swapping cells.</summary>
    internal PlaceSwap PlaceSwap => placeSwap ??= new PlaceSwap(World, Interruptions, StepMotion);

    /// <summary>Turning to the next waypoint of a route.</summary>
    internal Waypoints Waypoints => waypoints ??=
        new Waypoints(World, Passability, PlaceSwap, turn => TargetCycle.NextTarget(turn), turn => TargetCycle.Approach(turn));

    /// <summary>The walking part of a unit's turn.</summary>
    internal MovementDriver MovementDriver => movementDriver ??= new MovementDriver(World, Routes, Waypoints, StepMotion);

    /// <summary>Stepping into the building a unit is to work in.</summary>
    internal BuildingEntry BuildingEntry => buildingEntry ??=
        new BuildingEntry(World, UnitActions, UnitOrders, Arrival, NearestBuildings);

    /// <summary>Leaving a building.</summary>
    internal BuildingExit BuildingExit => buildingExit ??=
        new BuildingExit(World, UnitActions, Occupancy, SpiralSearch, StoryProgress, UnitGoals, UnitCalls, Raise);

    /// <summary>The animation part of a unit's turn.</summary>
    internal AnimationDriver AnimationDriver => animationDriver ??= new AnimationDriver(World, ActionTicks, ActionAnimation,
        BuildingExit, BuildingEntry, Occupancy, UnitOrders, Routes, Deaths, UnitCalls, turn => ActionSteps.End(turn), Raise);

    /// <summary>Couples forming and walking into their hut.</summary>
    internal Courtship Courtship => courtship ??=
        new Courtship(World, UnitActions, UnitOrders, Occupancy, CoupleLinks, UnitGoals, turn => TargetCycle.NextTarget(turn));

    /// <summary>The units' call cycle as the movement rules continue with it.</summary>
    private UnitCalls UnitCalls => new(turn => NeedsChoices.SeeToNeeds(turn), turn => TargetCycle.NextTarget(turn),
        turn => TargetCycle.Approach(turn));
}
