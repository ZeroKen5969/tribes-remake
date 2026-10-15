using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// An animal's decisions, made each frame it stands on a cell centre: reacting to the unit that
/// hit it, getting restless and wandering, looking out, chasing its target, planning short routes
/// and taking the next step, or idling and biting when it has arrived.
/// </summary>
internal sealed class AnimalBehaviour
{
    /// <summary>A chased unit farther than this many cells away is given up.</summary>
    private const int ChaseLimit = 5;

    /// <summary>A chasing animal has arrived when it is this close to its destination; a wandering one must be on it.</summary>
    private const int ChaseReach = 1, WanderReach = 0;

    /// <summary>The roll picking an idle animation (two choices, half and half).</summary>
    private const int IdleRollMask = 0xFFFFFF, IdleRollModulus = 100, IdleRollPerChoice = 50;

    /// <summary>The facing table of one-cell steps has rows of this many (dx + 1) entries by (dy + 1).</summary>
    private const int DeltaSpan = 3;

    /// <summary>The roll of a bite's strength: base damage * (2 + 0..2) / 4.</summary>
    private const int BiteRollMask = 255, BiteRollModulus = 3, BiteBase = 2, BiteDivisor = 4;

    private readonly GameWorld world;
    private readonly AnimalSenses senses;
    private readonly GridDistances grid;
    private readonly TargetDistances distances;
    private readonly RouteFinder routes;
    private readonly CellDamage damage;

    /// <summary>Decides for the animals of <paramref name="world"/>.</summary>
    public AnimalBehaviour(GameWorld world, AnimalSenses senses, GridDistances grid, TargetDistances distances,
        RouteFinder routes, CellDamage damage)
    {
        this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.senses = senses ?? throw new ArgumentNullException(nameof(senses));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.damage = damage ?? throw new ArgumentNullException(nameof(damage));
    }

    /// <summary>
    /// One frame of <paramref name="animal"/>'s life apart from moving: its animation advances (a
    /// finished attack animation bites the cell in front); off a cell centre it keeps walking, on a
    /// centre it decides what to do next.
    /// </summary>
    public void Update(Animal animal)
    {
        ArgumentNullException.ThrowIfNull(animal);
        animal.ActionFrame++;
        if (animal.ActionFrame >= world.Content.Rules.Animations.Animations[animal.Action & 0xFF].FrameCount)
        {
            animal.ActionFrame = 0;
            if (animal.Action == AnimalAnimations.Attack(animal.Kind))
            {
                Bite(animal);
            }
        }
        if (!animal.IsCentred)
        {
            animal.Action = AnimalAnimations.Walk(animal.Kind);
            return;
        }
        animal.StepCounter = 0;
        if (animal.ProvokeTimer > AnimalTimers.RestlessAfter)
        {
            Unrest(animal);
        }
        else if (LooksOut(animal))
        {
            senses.LookOut(animal);
            KeepTarget(animal);
        }
        Steer(animal, distances.FromAnimal(animal, new GridPoint(animal.X, animal.Y)).Measured);
    }

    /// <summary>An animal hit by a unit turns on it (or flees); one that has idled too long wanders off.</summary>
    private void Unrest(Animal animal)
    {
        if (animal.ProvokeTimer > AnimalTimers.Provoked)
        {
            Unit attacker = world.Unit(animal.ProvokeTimer - AnimalTimers.Provoked);
            var at = new GridPoint(attacker.X, attacker.Y);
            senses.React(animal, at, CellCode.OfUnit(attacker.Id), AnimalThreats.Hit, AwayFrom(animal, at));
        }
        else
        {
            senses.Roam(animal, false, 0);
            animal.Action = AnimalAnimations.Stand(animal.Kind);
            animal.ActionFrame = 0;
        }
        animal.ProvokeTimer = 0;
    }

    /// <summary>Animals look out every few frames by their speed, and whenever they are walking; in the pen mission not at its end.</summary>
    private bool LooksOut(Animal animal) =>
        (world.Frame % Kind(animal).SpeedClass == 0 || AnimalAnimations.IsMoving(animal.Action))
        && (world.Mission != AnimalMissions.PenMission || world.Script.ObjectiveStage < AnimalMissions.PenStoryEnd);

    /// <summary>
    /// A chased unit that is still alive on its cell is followed (its cell becomes the destination
    /// and the route is dropped) unless it has got too far away; a dead or vanished unit and a
    /// destroyed building are given up.
    /// </summary>
    private void KeepTarget(Animal animal)
    {
        if (animal.Target > 0)
        {
            Unit unit = world.Unit(CellCode.UnitOf(animal.Target));
            var at = new GridPoint(unit.X, unit.Y);
            if (unit.HitPoints <= 0 || !world.Map.Contains(at) || CellCode.At(world.Map, at) != animal.Target)
            {
                animal.Target = 0;
                return;
            }
            animal.DestinationX = unit.X;
            animal.DestinationY = unit.Y;
            animal.PathLength = 0;
            bool chasesAnyDistance = world.Mission == AnimalMissions.PenMission
                && world.Script.ObjectiveStage == AnimalMissions.PenChaseStage;
            if (Distance(animal) > ChaseLimit && !chasesAnyDistance)
            {
                animal.Target = 0;
                animal.DestinationX = animal.X;
                animal.DestinationY = animal.Y;
            }
        }
        else if (animal.Target < 0 && world.Building(CellCode.BuildingOf(animal.Target)).HitPoints <= 0)
        {
            animal.Target = 0;
        }
    }

    /// <summary>
    /// Heads for the destination (<paramref name="measured"/> = 10 * distance + facing to it): when
    /// the route is used up a new short one is planned (by the animal's pace, or at once when
    /// chasing; with no route and room to move it wanders elsewhere), then the next step is taken.
    /// An arrived animal stands, idles and, with a target, attacks.
    /// </summary>
    private void Steer(Animal animal, int measured)
    {
        bool chasing = animal.Target != 0;
        if (GridDistances.Cells(measured) - (chasing ? ChaseReach : WanderReach) <= 0)
        {
            Arrive(animal, measured);
            return;
        }
        if (animal.PathIndex >= animal.PathLength)
        {
            if (world.Frame % Kind(animal).SpeedClass == 0 || chasing)
            {
                animal.PathLength = 0;
                animal.PathIndex = 0;
                RouteResult route = routes.PlanAnimalRoute(animal, chasing ? ChaseReach : WanderReach);
                animal.PathLength = route.Length;
                if (animal.PathLength == 0 && !route.Surrounded)
                {
                    senses.Roam(animal, false, 0);
                }
            }
            if (animal.Action > AnimalAnimations.LastMoving || animal.PathIndex >= animal.PathLength)
            {
                StandStill(animal);
            }
        }
        if (animal.PathIndex < animal.PathLength)
        {
            TakeStep(animal);
        }
    }

    /// <summary>The next waypoint: if it is taken the route is dropped, otherwise the animal turns to it and reserves it.</summary>
    private void TakeStep(Animal animal)
    {
        int x = animal.PathXAt(animal.PathIndex);
        int y = animal.PathYAt(animal.PathIndex);
        var next = new GridPoint(x, y);
        if (!world.Map.Contains(next) || !world.Map.CellAt(next).IsEmpty)
        {
            animal.PathLength = 0;
            animal.PathIndex = 0;
            StandStill(animal);
            return;
        }
        MovementTables movement = world.Content.Rules.Movement;
        animal.Facing = movement.FacingByDeltaColumnMajor[((x - animal.X + 1) * DeltaSpan) + (y - animal.Y + 1)];
        animal.Action = AnimalAnimations.Walk(animal.Kind);
        DirectionStep step = movement.Directions[animal.Facing];
        world.Map.SetCell(new GridPoint(animal.X + step.Dx, animal.Y + step.Dy), MapCell.Of(CellContent.Reserved));
        animal.PathIndex++;
        animal.ProvokeTimer = 0;
    }

    /// <summary>
    /// At the destination: each time the animation comes round the animal picks its next
    /// animation; without a target it counts idle frames (and grows restless), otherwise it faces
    /// its destination.
    /// </summary>
    private void Arrive(Animal animal, int measured)
    {
        if (AnimalAnimations.IsMoving(animal.Action))
        {
            animal.ActionFrame = 0;
        }
        if (animal.ActionFrame != 0)
        {
            return;
        }
        if (animal.ProvokeTimer == 0)
        {
            ChooseAnimation(animal);
        }
        if (animal.Target == 0 && animal.ProvokeTimer < AnimalTimers.Provoked)
        {
            animal.ProvokeTimer++;
            return;
        }
        animal.ProvokeTimer = 0;
        animal.Facing = GridDistances.Facing(measured);
    }

    /// <summary>
    /// An idle animal plays one of its idle animations; one with a target attacks. In the migration
    /// mission an animal on its way settles in the last habitat.
    /// </summary>
    private void ChooseAnimation(Animal animal)
    {
        if (animal.Target == 0)
        {
            int roll = world.Random.NextMasked(IdleRollMask, IdleRollModulus);
            animal.Action = Kind(animal).IdleAnimations[roll / IdleRollPerChoice];
        }
        else
        {
            animal.Action = AnimalAnimations.Attack(animal.Kind);
        }
        if (world.Mission == AnimalMissions.MigrationMission && animal.SpawnZone == AnimalMissions.MigratingZone)
        {
            animal.SpawnZone = AnimalMissions.MigratedZone;
        }
    }

    /// <summary>The bite of a finished attack animation, on the cell in front.</summary>
    private void Bite(Animal animal)
    {
        DirectionStep front = world.Content.Rules.Movement.Directions[animal.Facing];
        var cell = new GridPoint(animal.X + front.Dx, animal.Y + front.Dy);
        int category = animal.Target > 0 ? CellCategories.Unit : CellCategories.Building;
        int strength = Kind(animal).AttackDamage * (BiteBase + world.Random.NextMasked(BiteRollMask, BiteRollModulus)) / BiteDivisor;
        int target = animal.Target > 0 ? CellCode.UnitOf(animal.Target) : CellCode.BuildingOf(animal.Target);
        damage.Strike(Striker.OfAnimal(animal.Index), cell, category, target, 0, strength, 0);
    }

    private static void StandStill(Animal animal)
    {
        animal.Action = AnimalAnimations.Stand(animal.Kind);
        animal.ActionFrame = 0;
    }

    private int Distance(Animal animal) => distances.FromAnimal(animal, new GridPoint(animal.X, animal.Y)).Cells;

    /// <summary>The facing leading from the animal away from <paramref name="cell"/>.</summary>
    private int AwayFrom(Animal animal, GridPoint cell)
    {
        int towards = GridDistances.Facing(grid.Measure(cell.X - animal.X, cell.Y - animal.Y));
        return Search.Facings.Opposite(towards);
    }

    private AnimalKind Kind(Animal animal) => world.Content.Rules.Animals.Kinds[animal.Kind];
}
