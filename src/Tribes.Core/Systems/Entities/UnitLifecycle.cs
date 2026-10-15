using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// What happens to a unit during its life: hit points going up and down, training a stat, dying,
/// its slot being freed when the death animation ends, and going over to another tribe.
/// </summary>
internal sealed class UnitLifecycle
{
    /// <summary>The hint shown to the player when the computer captures one of the player's children.</summary>
    public const int ChildCapturedHint = 17;

    /// <summary>A heal of more than this cures the unit's disease (and makes it immune for a while).</summary>
    private const int CuringHeal = 1;

    /// <summary>Disease exposure of a freshly cured unit.</summary>
    private const int CuredExposure = -100;

    private const int PracticeRoll = 0xFFFFFF, Percent = 100, DiagonalFacingBit = 1;
    private const int FirstUnitProperty = (int)EntityProperty.UnitX, LastUnitProperty = (int)EntityProperty.UnitPairLink;

    private readonly GameWorld world;
    private readonly CoupleLinks couples;
    private readonly Occupancy occupancy;
    private readonly UnitSpawning spawning;
    private readonly HintSystem hints;
    private readonly Action<GameEvent> raise;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitLifecycle(GameWorld world, CoupleLinks couples, Occupancy occupancy, UnitSpawning spawning,
        HintSystem hints, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary>
    /// Changes <paramref name="unit"/>'s hit points by <paramref name="delta"/> within 0..maximum and
    /// returns whether they are at the maximum. A strong heal cures disease (even a unit at its
    /// maximum); otherwise healing a unit already at its maximum changes nothing.
    /// </summary>
    public bool ChangeHitPoints(Unit unit, int delta)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (delta > CuringHeal)
        {
            unit.DiseaseExposure = CuredExposure;
            unit.Status &= ~UnitStatus.Diseased;
        }
        if (unit.HitPoints >= unit.MaxHitPoints && delta > 0)
        {
            return true;
        }
        if (TribeRoles.IsPlayer(world, unit.Tribe) || delta < 0)
        {
            raise(new HitPointsChanged(unit.Id, delta));
        }
        unit.HitPoints = Limit(unit.HitPoints + delta, 0, unit.MaxHitPoints);
        return unit.HitPoints >= unit.MaxHitPoints;
    }

    /// <summary>
    /// One training check of <paramref name="unit"/>'s stat <paramref name="stat"/>: with
    /// <paramref name="chancePercent"/> chance, and while value * step is below cap * step, the stat
    /// changes by <paramref name="step"/> (training maximum hit points heals as much, within the age
    /// limit). Returns whether the stat has reached its cap with a certain chance, i.e. training is
    /// over; a successful training of maximum hit points always returns true.
    /// </summary>
    public bool Practise(Unit unit, int stat, int chancePercent, int cap, int step)
    {
        ArgumentNullException.ThrowIfNull(unit);
        EntityProperty property = world.Content.Rules.Skills.TrainableStats[stat].Property;
        int before = EntityProperties.Get(world, property, unit.Id);
        if (world.Random.NextMasked(PracticeRoll, Percent) < chancePercent && before * step < cap * step)
        {
            EntityProperties.Set(world, property, unit.Id, before + step);
            if (property == EntityProperty.UnitMaxHitPoints)
            {
                int limit = world.Content.Rules.Units.AgeSteps[unit.Age].MaxHitPointsCap;
                if (unit.MaxHitPoints >= limit)
                {
                    unit.MaxHitPoints = limit;
                }
                unit.HitPoints += unit.MaxHitPoints - before;
                return true;
            }
        }
        return EntityProperties.Get(world, property, unit.Id) * step >= cap * step && chancePercent >= Percent;
    }

    /// <summary>
    /// <paramref name="unit"/> dies: its death animation starts (hit points -1), it gives up the
    /// cell it was stepping into and the buildings it ran, and the tribe credited with it counts a
    /// kill (<paramref name="killer"/>: the tribe that dealt the blow, -1 for none).
    /// </summary>
    public void Kill(Unit unit, int killer = -1)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ReleaseCellAhead(unit);
        raise(new UnitGone(unit.Id));
        unit.HitPoints = -1;
        occupancy.ReleaseWorkplaces(unit);
        if (world.Script.Active)
        {
            return;
        }
        if (TribeRoles.IsPlayer(world, unit.Tribe))
        {
            hints.Notice(unit.Tribe, EntityNotices.UnitDied, unit.X, unit.Y, unit.Id);
        }
        if (TribeRoles.CreditedFor(world, unit.Tribe, killer) is >= 0 and int credited)
        {
            world.Tribes[credited][TribeCounter.UnitsKilled]++;
        }
    }

    /// <summary>
    /// Frees <paramref name="unit"/>'s slot after its death animation: its couple is dissolved, its
    /// cell is cleared (a trap it stood on comes back), the tribe has one member less and its bed is free.
    /// </summary>
    public void FreeSlot(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        couples.Dissolve(unit, 0);
        unit.HitPoints = 0;
        unit.ProjectileFlightSteps = 0;
        var cell = new GridPoint(unit.X, unit.Y);
        if (world.Map.Contains(cell))
        {
            int underneath = unit.NextOccupantOrCellObject;
            world.Map.SetCell(cell, CellCode.IsStandingObject(underneath) ? CellCode.FromCode(underneath, world) : MapCell.Empty);
        }
        TribeState tribe = world.Tribes[unit.Tribe];
        tribe[TribeCounter.Population]--;
        tribe.SetCounter((int)TribeCounter.Males + unit.Gender, tribe.Counter((int)TribeCounter.Males + unit.Gender) - 1);
        occupancy.FreeHome(unit);
    }

    /// <summary>
    /// Moves <paramref name="unit"/> over to <paramref name="tribe"/> (a captured child): it is
    /// copied into the first free slot of that tribe, which takes its cell, and gets a portrait and
    /// a name there; its old slot is freed (a player losing the child is told). Returns the new
    /// unit, or null when the tribe has no room (only in the campaign, where slots are limited).
    /// </summary>
    public Unit? ChangeTribe(Unit unit, int tribe)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (world.FreeUnitSlot(tribe) is not { } slot)
        {
            return null;
        }
        if (TribeRoles.IsPlayer(world, unit.Tribe))
        {
            raise(new UnitGone(unit.Id));
            hints.Offer(unit.Tribe, ChildCapturedHint, world.Tribes[unit.Tribe][TribeCounter.Chief], unit.Id);
        }
        unit.TargetIndex = 0;
        CopyUnit(unit, slot);
        CountTransfer(slot, unit.Tribe, tribe);
        unit.HitPoints = 0;
        world.Map.SetCell(new GridPoint(slot.X, slot.Y), MapCell.UnitAt(slot.Id));
        spawning.PickPortrait(slot);
        spawning.PickName(slot);
        return slot;
    }

    /// <summary><paramref name="value"/> raised to <paramref name="low"/>, else cut to <paramref name="high"/>.</summary>
    private static int Limit(int value, int low, int high) => value < low ? low : Math.Min(value, high);

    /// <summary>
    /// A unit that dies half way into the next cell gives the cell back: it was reserved for the
    /// unit (or held a trap the unit was about to step on, which comes back).
    /// </summary>
    private void ReleaseCellAhead(Unit unit)
    {
        DirectionStep step = world.Content.Rules.Movement.Directions[unit.Facing];
        var ahead = new GridPoint(unit.X + step.Dx, unit.Y + step.Dy);
        SpeedClass speed = world.Content.Rules.Movement.SpeedClasses[unit.MoveDelay - unit.RunBonus];
        int frames = ((unit.Facing & DiagonalFacingBit) == 0 ? speed.Straight : speed.Diagonal).StepFrames;
        if (unit.AnimationNumber > AnimationNumbers.LastWalking || unit.StepTicks >= (frames + 1) / 2
            || !world.Map.Contains(ahead) || world.Map.CellAt(ahead).Content != CellContent.Reserved)
        {
            return;
        }
        int trap = unit.NextOccupantOrCellObject;
        world.Map.SetCell(ahead, CellCode.IsSteppingObject(trap) ? CellCode.FromCode(-trap, world) : MapCell.Empty);
    }

    private void CopyUnit(Unit from, Unit to)
    {
        for (int number = FirstUnitProperty; number <= LastUnitProperty; number++)
        {
            var property = (EntityProperty)number;
            if (EntityProperties.IsHeldByModel(property) && !EntityProperties.IsConstant(property))
            {
                EntityProperties.Set(world, property, to.Id, EntityProperties.Get(world, property, from.Id));
            }
        }
        to.Path.CopyFrom(from.Path);
    }

    private void CountTransfer(Unit unit, int from, int to)
    {
        int gender = (int)TribeCounter.Males + unit.Gender;
        TribeState gaining = world.Tribes[to], losing = world.Tribes[from];
        gaining[TribeCounter.Population]++;
        losing[TribeCounter.Population]--;
        gaining.SetCounter(gender, gaining.Counter(gender) + 1);
        losing.SetCounter(gender, losing.Counter(gender) - 1);
    }
}
