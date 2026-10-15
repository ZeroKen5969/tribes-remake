using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// What an animal notices and where it goes: its lookout for people and the player's occupied
/// buildings, fleeing from or turning on what provoked it, and wandering about its habitat.
/// </summary>
internal sealed class AnimalSenses
{
    /// <summary>The lookout spiral (it reaches 3 cells away).</summary>
    private const int LookoutRounds = 6;

    /// <summary>Only what is at most this many cells away is noticed.</summary>
    private const int NoticeDistance = 3;

    /// <summary>A spiral covering the whole map, to move a destination to the nearest free cell.</summary>
    private const int FreeCellRounds = 127;

    /// <summary>The roll of a wandering step: 3 bits of column offset, 3 of row offset, each biased by -4.</summary>
    private const int WanderRollMask = 0xFFFFFF, WanderRollModulus = 63, OffsetBits = 3, OffsetMask = 7, OffsetBias = 4;

    /// <summary>The pen of the pen mission: columns 20..27, rows 0..6.</summary>
    private const int PenLeft = 20, PenRight = 27, PenBottom = 6;

    private readonly GameWorld world;
    private readonly SpiralSearch spiral;

    /// <summary>Senses for the animals of <paramref name="world"/>.</summary>
    public AnimalSenses(GameWorld world, SpiralSearch spiral)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
    }

    /// <summary>
    /// The animal looks around (up to 3 cells): an occupied building of the player is noticed while
    /// the animal has no target; a living unit while it has no unit target, or always if the unit
    /// is the computer's. The animal reacts to each (see <see cref="React"/>); a computer unit ends
    /// the lookout — unless the animal fled from it and had to look for a free cell to flee to, in
    /// which case the original goes on looking (it judges the unit by the last cell the free-cell
    /// search examined).
    /// </summary>
    public void LookOut(Animal animal)
    {
        ArgumentNullException.ThrowIfNull(animal);
        spiral.Visit(new GridPoint(animal.X, animal.Y), LookoutRounds, cell =>
        {
            if (cell.Distance > NoticeDistance)
            {
                return false;
            }
            if (IsPlayerBuilding(cell.Code) && animal.Target == 0)
            {
                Building building = world.Building(CellCode.BuildingOf(cell.Code));
                if (building.HitPoints > 0 && building.OccupantCount != 0)
                {
                    React(animal, cell.Cell, cell.Code, AnimalThreats.Sighting, cell.AwayFacing);
                }
                return false;
            }
            if (!IsUnit(cell.Code) || world.Unit(CellCode.UnitOf(cell.Code)).HitPoints <= 0)
            {
                return false;
            }
            bool computerUnit = !TribeRoles.IsPlayer(world, world.TribeOfUnit(CellCode.UnitOf(cell.Code)));
            if (animal.Target > 0 && !computerUnit)
            {
                return false;
            }
            bool movedAway = React(animal, cell.Cell, cell.Code, AnimalThreats.Sighting, cell.AwayFacing);
            return computerUnit && !movedAway;
        });
    }

    /// <summary>
    /// The animal reacts to what fills <paramref name="at"/> (cell code <paramref name="code"/>),
    /// provoked by <paramref name="threat"/>: if it is timid enough
    /// (<c>AnimalKind.FleeThreshold</c> at most the threat) and has no target yet, it flees one step
    /// towards <paramref name="awayFacing"/>; otherwise it makes the content its target and heads
    /// for it. Returns whether the flight had to be moved to the nearest free cell.
    /// </summary>
    public bool React(Animal animal, GridPoint at, int code, int threat, int awayFacing)
    {
        ArgumentNullException.ThrowIfNull(animal);
        if (Kind(animal).FleeThreshold <= threat && animal.Target == 0)
        {
            return Roam(animal, true, awayFacing);
        }
        animal.DestinationX = at.X;
        animal.DestinationY = at.Y;
        animal.Target = code;
        return false;
    }

    /// <summary>
    /// Picks the animal's next destination and drops its route: fleeing, one cell towards
    /// <paramref name="awayFacing"/>; wandering, a random cell up to 4 cells away, kept within the
    /// wander radius of its habitat. A destination that is not free moves to the nearest free cell
    /// (whether it did is returned). Animals of the first habitat inside the pen of the pen mission
    /// stay where they are; the random roll is drawn in every case.
    /// </summary>
    public bool Roam(Animal animal, bool flee, int awayFacing)
    {
        ArgumentNullException.ThrowIfNull(animal);
        animal.PathLength = 0;
        int roll = world.Random.NextMasked(WanderRollMask, WanderRollModulus);
        if (!Roams(animal))
        {
            animal.DestinationX = animal.X;
            animal.DestinationY = animal.Y;
            return false;
        }
        GridPoint destination = flee ? FleeStep(animal, awayFacing) : WanderStep(animal, roll);
        animal.DestinationX = destination.X;
        animal.DestinationY = destination.Y;
        var cell = new GridPoint(animal.DestinationX, animal.DestinationY);
        if (world.Map.Contains(cell) && world.Map.CellAt(cell).IsEmpty)
        {
            return false;
        }
        if (spiral.FindEmptyCell(cell, FreeCellRounds) is { } free)
        {
            animal.DestinationX = free.X;
            animal.DestinationY = free.Y;
        }
        return true;
    }

    private GridPoint FleeStep(Animal animal, int awayFacing)
    {
        DirectionStep away = world.Content.Rules.Movement.Directions[awayFacing];
        return new GridPoint(world.Map.ClampColumn(animal.X + away.Dx), world.Map.ClampRow(animal.Y + away.Dy));
    }

    private GridPoint WanderStep(Animal animal, int roll)
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        GridPoint home = world.Endless != null ? Endless.EndlessWildlife.HomeOf(world, animal.SpawnZone) : mission.AnimalSpawnPoints[animal.SpawnZone].Cell;
        int radius = mission.AnimalWanderRadius;
        int dx = (roll & OffsetMask) - OffsetBias;
        int dy = ((roll >> OffsetBits) & OffsetMask) - OffsetBias;
        return new GridPoint(
            Map.GridMath.Clamp(animal.X + dx, home.X - radius, home.X + radius),
            Map.GridMath.Clamp(animal.Y + dy, home.Y - radius, home.Y + radius));
    }

    private bool Roams(Animal animal) =>
        world.Mission != AnimalMissions.PenMission || animal.SpawnZone != AnimalMissions.HomeZone
        || animal.X > PenRight || animal.X < PenLeft || animal.Y > PenBottom;

    private AnimalKind Kind(Animal animal) => world.Content.Rules.Animals.Kinds[animal.Kind];

    private bool IsUnit(int code) => CellCode.UnitOf(code) is > 0 and int unit && unit <= world.UnitSlots;

    private bool IsPlayerBuilding(int code) =>
        CellCode.BuildingOf(code) is > 0 and int building && building <= world.BuildingSlots
        && TribeRoles.IsPlayer(world, world.TribeOfBuilding(building));
}
