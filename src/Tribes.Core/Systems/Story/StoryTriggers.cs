using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// What moves the story on: the chief's death (the player must name a new one), the timed council
/// scenes of the ambush mission, and the mission's story steps with their triggers — places
/// reached, the enemy wiped out, timers, buildings lost.
/// </summary>
internal sealed partial class StoryTriggers
{
    /// <summary>Story step triggers handled here (others are handled where they happen).</summary>
    public const int EnemyGoneTrigger = 3, CounterTrigger = 4, TimerTrigger = 5, FewBuildingsTrigger = 6,
        AdultInAreaTrigger = 7, FirstBuildingLostTrigger = 8, LastTimedTrigger = 9;

    /// <summary>The hint that asks the player to name a new chief.</summary>
    public const int NewChiefHint = 18;

    /// <summary>The mission without a chief and the village mission (its chief matters only early on).</summary>
    private const int ChieflessMission = 3, VillageMission = 2;

    /// <summary>The last story step of the village mission at which a dead chief is replaced.</summary>
    private const int VillageLastChiefStage = 2;

    /// <summary>Frames without fighting before the story moves on.</summary>
    private const int QuietFrames = 50;

    /// <summary>Frames after the enemy is gone before the step comes.</summary>
    private const int EnemyGoneDelay = 40;

    /// <summary>The timer trigger's delay in the first two missions and in the others.</summary>
    private const int EarlyTimerDelay = 30, TimerDelay = 120;

    /// <summary>The counter trigger fires when the objective timer has counted more than this.</summary>
    private const int CounterThreshold = 2;

    /// <summary>The enemy has fewer than this many buildings.</summary>
    private const int FewBuildings = 5;

    /// <summary>Missions before this one set whose units work freely when a story step comes.</summary>
    private const int FirstMissionsCount = 2;

    /// <summary>The mission whose first chapter scene is played by its mission number's own rule.</summary>
    private const int SoloScenesMission = 5;

    /// <summary>Spiral rounds searched for extras around the lead.</summary>
    private const int ExtrasRounds = 25;

    private readonly GameWorld world;
    private readonly StoryProgress story;
    private readonly Council council;
    private readonly HintSystem hints;
    private readonly SpiralSearch spiral;
    private readonly ObjectiveEvents events;
    private readonly Action<Unit> stopActor;

    /// <summary>
    /// Follows the story of <paramref name="world"/>; <paramref name="stopActor"/> makes a unit stop
    /// at its next cell and idle there (the actors of the third chapter's area steps).
    /// </summary>
    public StoryTriggers(GameWorld world, StoryProgress story, Council council, HintSystem hints, SpiralSearch spiral,
        ObjectiveEvents events, Action<Unit> stopActor)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        this.council = council ?? throw new ArgumentNullException(nameof(council));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.events = events ?? throw new ArgumentNullException(nameof(events));
        this.stopActor = stopActor ?? throw new ArgumentNullException(nameof(stopActor));
    }

    private int Player => TribeRoles.Player(world);

    private int Chief => world.Tribes[Player][TribeCounter.Chief];

    /// <summary>Whether the chief is a living adult.</summary>
    public bool ChiefIsActive()
    {
        int chief = Chief;
        return chief >= 1 && chief <= world.UnitSlots && world.Unit(chief).HitPoints > 0
            && world.Unit(chief).Age >= UnitAges.Adult;
    }

    /// <summary>Whether the chief's slot is free or holds a child (a chief still dying is not gone yet).</summary>
    private bool ChiefIsGone()
    {
        int chief = Chief;
        return chief < 1 || chief > world.UnitSlots || world.Unit(chief).HitPoints == 0 || world.Unit(chief).Age < UnitAges.Adult;
    }

    /// <summary>
    /// When the chief is gone, the player has other adults and the game is quiet, the player is
    /// asked to name a new chief (the first adult is the hint's subject). Returns whether that
    /// happened (the rest of the story waits for the next frame).
    /// </summary>
    public bool CheckChief()
    {
        ScriptState script = world.Script;
        bool chiefMatters = world.Mission != ChieflessMission
            && (world.Mission != VillageMission || script.ObjectiveStage <= VillageLastChiefStage);
        if (!chiefMatters || !ChiefIsGone() || script.AwaitingNewChief || world.Tribes[Player][TribeCounter.Population] <= 0
            || script.ObjectiveStage >= StoryProgress.ScenePlaying || world.EndCountdown != 0
            || world.FramesSinceCombat < QuietFrames)
        {
            return false;
        }
        foreach (Unit unit in world.UnitsOf(Player))
        {
            if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult)
            {
                script.AwaitingNewChief = true;
                hints.Show(NewChiefHint, unit.Id, unit.Id);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A unit of the player reached <paramref name="cell"/> (the chief, or for some steps any
    /// adult): when it is in the current step's area the step's scene starts, with
    /// <paramref name="lead"/> leading and <paramref name="subject"/> (a cell code, 0 for none) as
    /// its subject, and the adults around the lead (the enemy's around the subject in the village
    /// mission) join as extras.
    /// </summary>
    public void AreaReached(int lead, int subject, GridPoint cell)
    {
        if (story.NextStep is not StoryStep step || !step.Area.Contains(cell)
            || (world.Mission != ChieflessMission && !ChiefIsActive()))
        {
            return;
        }
        if (world.Mission == AmbushMission && world.Script.ObjectiveStage == AmbushCouncilStage)
        {
            ConveneCouncil(false);
        }
        else
        {
            story.CastActors(lead, subject, world.FirstUnitSlot(Player));
            bool village = world.Mission == VillageMission;
            if (subject >= 1 && subject <= world.UnitSlots)
            {
                Unit target = world.Unit(subject);
                (target.DestinationX, target.DestinationY) = (target.X, target.Y);
            }
            if (CellOf(village ? subject : lead) is GridPoint centre)
            {
                CollectExtras(centre, village ? TribeRoles.Computer(world) : Player);
            }
        }
        story.Reach(step);
    }

    /// <summary>The cell of the unit or animal with cell code <paramref name="code"/>, or null for anything else.</summary>
    private GridPoint? CellOf(int code)
    {
        int unit = CellCode.UnitOf(code);
        if (unit >= 1 && unit <= world.UnitSlots)
        {
            return new GridPoint(world.Unit(unit).X, world.Unit(unit).Y);
        }
        int animal = CellCode.AnimalOf(code);
        return animal >= 0 && animal < world.AnimalSlots ? new GridPoint(world.Animal(animal).X, world.Animal(animal).Y) : null;
    }

    /// <summary>Seats the council and casts it: the seated units (or the chief for empty seats) play parts 1..4.</summary>
    private void ConveneCouncil(bool witchDoctorFirst)
    {
        council.Convene(witchDoctorFirst);
        for (int seat = 0; seat < ScriptState.MeetingSeats; seat++)
        {
            world.Script.SetActor(seat + 1, council.SeatedOrChief(seat));
        }
    }

    /// <summary>The adults of <paramref name="tribe"/> around <paramref name="centre"/> fill the extra actor slots in spiral order.</summary>
    private void CollectExtras(GridPoint centre, int tribe)
    {
        int slot = SceneStaging.FirstExtra;
        spiral.Visit(centre, ExtrasRounds, cell =>
        {
            MapCell content = world.Map.CellAt(cell.Cell);
            if (content.Content == CellContent.Unit && world.Unit(content.Index).Tribe == tribe
                && world.Unit(content.Index).HitPoints > 0 && world.Unit(content.Index).Age >= UnitAges.Adult)
            {
                world.Script.SetActor(slot++, content.Index);
            }
            return slot >= ScriptState.ActorSlots;
        });
    }
}
