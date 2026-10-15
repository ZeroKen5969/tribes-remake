using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// The story steps reached when one of the player's units meets an enemy inside the step's area:
/// the scene is cast with the unit and the enemy (and the adults standing around), or in the ambush
/// mission's council step the council sits down.
/// </summary>
internal sealed class StoryEncounters
{
    /// <summary>The story step trigger of an encounter in an area.</summary>
    public const int AreaTrigger = 1;

    /// <summary>The mission played without the chief.</summary>
    private const int NoChiefMission = 3;

    /// <summary>The ambush mission and its story step at which the council meets instead.</summary>
    private const int AmbushMission = 4, CouncilStep = 1;

    /// <summary>The mission whose scene gathers the enemy's adults around the enemy.</summary>
    private const int EnemyGatheringMission = 2;

    /// <summary>The actor slots the council's seats fill (1..4).</summary>
    private const int FirstSeatActor = 1, Seats = ScriptState.MeetingSeats;

    /// <summary>The adults within this many spiral rounds of the scene are gathered.</summary>
    private const int GatherRounds = 25;

    private readonly GameWorld world;
    private readonly StoryProgress story;
    private readonly Council council;
    private readonly NearbySearches searches;

    /// <summary>Follows encounters in <paramref name="world"/>.</summary>
    public StoryEncounters(GameWorld world, StoryProgress story, Council council, NearbySearches searches)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        this.council = council ?? throw new ArgumentNullException(nameof(council));
        this.searches = searches ?? throw new ArgumentNullException(nameof(searches));
    }

    /// <summary>
    /// <paramref name="unit"/> sees the enemy unit <paramref name="enemy"/> (0 for none) at
    /// <paramref name="at"/>: if the story waits for an encounter in an area holding that cell (and
    /// the chief lives, where there is one), the step is reached.
    /// </summary>
    public void Meet(Unit unit, int enemy, GridPoint at)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (story.NextStep is not { Trigger: AreaTrigger } step || !step.Area.Contains(at) || !ChiefCounts())
        {
            return;
        }
        if (world.Mission == AmbushMission && world.Script.ObjectiveStage == CouncilStep)
        {
            council.Convene(false);
            for (int seat = 0; seat < Seats; seat++)
            {
                world.Script.SetActor(FirstSeatActor + seat, council.SeatedOrChief(seat));
            }
        }
        else
        {
            CastEncounter(unit, enemy);
        }
        story.Reach(step);
    }

    private void CastEncounter(Unit unit, int enemy)
    {
        story.CastActors(unit.Id, enemy, world.FirstUnitSlot(TribeRoles.Player(world)));
        Unit? other = enemy >= 1 && enemy <= world.UnitSlots ? world.Unit(enemy) : null;
        if (other is not null)
        {
            other.DestinationX = other.X;
            other.DestinationY = other.Y;
        }
        bool enemyCentred = world.Mission == EnemyGatheringMission;
        Unit centre = enemyCentred && other is not null ? other : unit;
        int tribe = enemyCentred ? TribeRoles.Computer(world) : TribeRoles.Player(world);
        searches.CollectAdults(new GridPoint(centre.X, centre.Y), GatherRounds, tribe);
    }

    private bool ChiefCounts()
    {
        if (world.Mission == NoChiefMission)
        {
            return true;
        }
        int chief = world.Tribes[TribeRoles.Player(world)][TribeCounter.Chief];
        return chief >= 1 && chief <= world.UnitSlots && world.Unit(chief).HitPoints > 0 && world.Unit(chief).Age >= UnitAges.Adult;
    }
}
