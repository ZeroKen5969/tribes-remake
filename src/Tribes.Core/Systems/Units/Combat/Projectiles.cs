using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Combat;

/// <summary>
/// A unit threw a projectile: the presentation draws its arc, which starts
/// <paramref name="StartHeight"/> pixels above the thrower and ends <paramref name="ImpactDrop"/>
/// pixels lower than it started.
/// </summary>
/// <param name="Unit">The thrower.</param>
/// <param name="StartHeight">Height of the throw above the ground, in pixels.</param>
/// <param name="ImpactDrop">How many pixels the projectile drops during the flight.</param>
public sealed record ProjectileThrown(int Unit, int StartHeight, int ImpactDrop) : GameEvent;

/// <summary>
/// Thrown weapons: a throw aims at the thrower's destination (leading a moving target), the
/// projectile flies cell by cell and hurts what is where it lands.
/// </summary>
internal sealed class Projectiles
{
    /// <summary>Pixel size of a cell.</summary>
    private const int CellWidth = 22, CellHeight = 16;

    /// <summary>
    /// The longest flight in steps: the arc the presentation draws has room for this many steps (the
    /// original gives up a longer throw).
    /// </summary>
    private const int MaxFlightSteps = 12;

    /// <summary>Damage floor (in tenths) of a throw at a target standing still.</summary>
    private const int SteadyDamageFloor = 9;

    /// <summary>The damage roll covers floor .. 10 tenths.</summary>
    private const int FullDamageTenths = 10;

    /// <summary>Mask of the damage roll.</summary>
    private const int DamageRollMask = 0xFFFF;

    /// <summary>Ranged skill points per point of projectile damage.</summary>
    private const int RangedSkillPerDamage = 3;

    /// <summary>Cell codes above this are animals.</summary>

    /// <summary>How far the projectile may land from its target and still follow it, in cells.</summary>
    private const int LandingReach = 1;

    /// <summary>
    /// The projectile kind a tower guard shoots when its weapon is not thrown (the stone); the
    /// original reads a speed from outside its projectile table there.
    /// </summary>
    private const int DefaultProjectile = 0;

    private readonly GameWorld world;
    private readonly RouteFinder routes;
    private readonly GridDistances distances;
    private readonly CellDamage damage;
    private readonly Action<GameEvent> raise;
    private readonly MovementTables movement;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly WeaponTables weapons;

    /// <summary>Throws and lands projectiles in <paramref name="world"/>.</summary>
    public Projectiles(GameWorld world, RouteFinder routes, GridDistances distances, CellDamage damage, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.damage = damage ?? throw new ArgumentNullException(nameof(damage));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        movement = world.Content.Rules.Movement;
        actions = world.Content.Rules.Actions.Actions;
        weapons = world.Content.Rules.Weapons;
    }

    /// <summary>
    /// <paramref name="thrower"/> throws its weapon's projectile from <paramref name="from"/> at its
    /// destination, if it has none in flight and the destination is within its action's reach. The
    /// flight takes the pixel distance / the projectile's speed steps; a moving target is led by
    /// where it will be, and the throw is less sure the faster the target moves.
    /// </summary>
    public void Throw(Unit thrower, GridPoint from, int startHeight, int impactDrop)
    {
        ArgumentNullException.ThrowIfNull(thrower);
        if (thrower.ProjectileFlightSteps > 0)
        {
            return;
        }
        int dx = thrower.DestinationX - from.X, dy = thrower.DestinationY - from.Y;
        if (GridDistances.Cells(distances.MeasureAtEdge(dx, dy)) > routes.ActionRange(thrower))
        {
            return;
        }
        ProjectileKind kind = weapons.Projectiles[Math.Max(weapons.Weapons[thrower.Weapon].ProjectileKind, DefaultProjectile)];
        int pixels = PixelDistance(dx, dy);
        int steps = pixels > kind.Speed ? pixels / kind.Speed : 1;
        thrower.ProjectileFlightSteps = steps;
        thrower.ProjectileDamageFloor = SteadyDamageFloor;
        (int leadX, int leadY) = Lead(thrower, steps);
        thrower.ProjectileVelocityX = Velocity(dx, CellWidth, leadX, steps);
        thrower.ProjectileVelocityY = Velocity(dy, CellHeight, leadY, steps);
        if (steps > MaxFlightSteps)
        {
            thrower.ProjectileFlightSteps = 0;
            return;
        }
        thrower.ProjectileFacing = GridDistances.Facing(distances.Measure(dx, dy));
        thrower.ProjectileStep = 0;
        thrower.ProjectileSubX = 0;
        thrower.ProjectileSubY = 0;
        thrower.ProjectileX = from.X;
        thrower.ProjectileY = from.Y;
        raise(new ProjectileThrown(thrower.Id, startHeight, impactDrop));
    }

    /// <summary>
    /// One step of <paramref name="thrower"/>'s projectile: it moves by its speed, crossing into
    /// the next cell at half a cell; after its last step it lands.
    /// </summary>
    public void Fly(Unit thrower)
    {
        ArgumentNullException.ThrowIfNull(thrower);
        if (thrower.ProjectileStep < thrower.ProjectileFlightSteps)
        {
            thrower.ProjectileSubX += thrower.ProjectileVelocityX;
            thrower.ProjectileSubY += thrower.ProjectileVelocityY;
            int cellsX = CellsCrossed(thrower.ProjectileSubX, CellWidth);
            int cellsY = CellsCrossed(thrower.ProjectileSubY, CellHeight);
            thrower.ProjectileX += cellsX;
            thrower.ProjectileY += cellsY;
            thrower.ProjectileSubX -= cellsX * CellWidth;
            thrower.ProjectileSubY -= cellsY * CellHeight;
        }
        thrower.ProjectileStep++;
        if (thrower.ProjectileStep > thrower.ProjectileFlightSteps)
        {
            Land(thrower);
        }
    }

    /// <summary>
    /// The projectile lands and hurts what it was aimed at, following it one cell: (ranged skill / 3 +
    /// the weapon's bonus) × a roll of floor..10 tenths.
    /// </summary>
    private void Land(Unit thrower)
    {
        thrower.ProjectileFlightSteps = 0;
        int category = ExpectedCategory(thrower.Action);
        int floor = thrower.ProjectileDamageFloor;
        int roll = world.Random.NextMasked(DamageRollMask, FullDamageTenths + 1 - floor) + floor;
        int hurt = ((thrower.RangedSkill / RangedSkillPerDamage) + weapons.Weapons[thrower.Weapon].DamageBonus) * roll
            / FullDamageTenths;
        damage.Strike(Striker.OfUnit(thrower), new GridPoint(thrower.ProjectileX, thrower.ProjectileY), category,
            thrower.TargetIndex, 0, hurt, LandingReach);
    }

    /// <summary>The cell category a throw of <paramref name="action"/> is aimed at.</summary>
    private int ExpectedCategory(int action) => action switch
    {
        <= ActionNumbers.LastBasicAttack => actions[action].AttackTargetCategory ?? CellCategories.Empty,
        ActionNumbers.TowerGuard => CellCategories.Unit,
        ActionNumbers.TowerShot => CellCategories.Building,
        ActionNumbers.TowerAnimalGuard => CellCategories.Animal,
        _ => CellCategories.Empty,
    };

    /// <summary>
    /// Where a moving target will be after <paramref name="steps"/> steps, in pixels from where it
    /// is; also lowers the thrower's damage floor to the target's speed class.
    /// </summary>
    private (int X, int Y) Lead(Unit thrower, int steps)
    {
        if ((actions[thrower.Action].Traits & ActionTraits.FollowsTarget) == 0
            || MovingTarget(thrower) is not { } target || target.SpeedClass <= 0)
        {
            return (0, 0);
        }
        thrower.ProjectileDamageFloor = target.SpeedClass;
        DirectionStep direction = movement.Directions[target.Facing];
        SpeedClass speed = movement.SpeedClasses[target.SpeedClass];
        int stepFrames = (target.Facing & 1) == 0 ? speed.Straight.StepFrames : speed.Diagonal.StepFrames;
        return (target.OffsetX + (direction.Dx * CellWidth * steps / stepFrames),
            target.OffsetY + (direction.Dy * CellHeight * steps / stepFrames));
    }

    /// <summary>The thrower's target while it walks, or null when it stands still.</summary>
    private MovingTargetState? MovingTarget(Unit thrower)
    {
        int target = thrower.TargetIndex;
        if (CellCode.AnimalOf(thrower.TargetCellCode) >= 0)
        {
            if (target < 0 || target >= world.AnimalSlots)
            {
                return null;
            }
            Animal animal = world.Animal(target);
            return AnimalAnimations.IsMoving(animal.Action)
                ? new MovingTargetState(world.Content.Rules.Animals.Kinds[animal.Kind].SpeedClass, animal.Facing,
                    animal.OffsetX, animal.OffsetY)
                : null;
        }
        if (target < 1 || target > world.UnitSlots)
        {
            return null;
        }
        Unit unit = world.Unit(target);
        return unit.AnimationNumber <= AnimationNumbers.LastWalking
            ? new MovingTargetState(unit.MoveDelay - unit.RunBonus, unit.Facing, unit.OffsetX, unit.OffsetY)
            : null;
    }

    /// <summary>
    /// The pixel distance of a cell <paramref name="dx"/>, <paramref name="dy"/> away; further than
    /// the distance table reaches, the table's edge is taken.
    /// </summary>
    private int PixelDistance(int dx, int dy)
    {
        IReadOnlyList<IReadOnlyList<int>> table = movement.CellDistances;
        int row = Math.Min(Math.Abs(dy), table.Count - 1);
        return table[row][Math.Min(Math.Abs(dx), table[row].Count - 1)];
    }

    /// <summary>Pixels per step that cover <paramref name="cells"/> cells plus a lead in <paramref name="steps"/> steps, rounded away from 0.</summary>
    private static int Velocity(int cells, int cellSize, int lead, int steps)
    {
        int rounding = (cells > 0 ? 1 : -1) * steps / 2;
        return ((cells * cellSize) + lead + rounding) / steps;
    }

    /// <summary>Whole cells a pixel offset has crossed (more than half a cell counts as the next cell).</summary>
    private static int CellsCrossed(int offset, int cellSize)
    {
        const int CellsBelowZero = 3;
        return ((offset + (cellSize / 2) + (CellsBelowZero * cellSize)) / cellSize) - CellsBelowZero;
    }

    /// <summary>How a moving target moves: speed class, facing and pixel offset within its cell.</summary>
    private readonly record struct MovingTargetState(int SpeedClass, int Facing, int OffsetX, int OffsetY);
}
