using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Combat;

/// <summary>Who strikes a blow: a unit of a tribe, or a wild animal.</summary>
/// <param name="Slot">The unit slot, or the animal slot.</param>
/// <param name="Tribe">The unit's tribe (for an animal: unused).</param>
/// <param name="IsAnimal">Whether the striker is a wild animal.</param>
internal readonly record struct Striker(int Slot, int Tribe, bool IsAnimal)
{
    /// <summary>A blow by <paramref name="unit"/>.</summary>
    public static Striker OfUnit(Unit unit) => new(unit?.Id ?? throw new ArgumentNullException(nameof(unit)), unit.Tribe, false);

    /// <summary>A bite by the animal in slot <paramref name="animal"/>.</summary>
    public static Striker OfAnimal(int animal) => new(animal, 0, true);

    /// <summary>The striker's cell code, by which alarmed units recognise it.</summary>
    public int CellCode => IsAnimal ? Maps.CellCode.OfAnimal(Slot) : Maps.CellCode.OfUnit(Slot);
}

/// <summary>A blow landed: the screen shows the damage over the target.</summary>
/// <param name="Category">The cell category of what was hit (<c>TargetTables.CellCategories</c>).</param>
/// <param name="Slot">The slot of what was hit.</param>
/// <param name="Damage">The hit points taken.</param>
/// <param name="Anchor">For a building, where on it: column offset + 16 * row offset; otherwise as given by the striker.</param>
/// <param name="Style">The look of the damage number (52..61, larger for heavier blows).</param>
public sealed record BlowLanded(int Category, int Slot, int Damage, int Anchor, int Style) : GameEvent
{
    /// <summary>The tribe of the unit that struck, or -1 for a wild animal.</summary>
    public int StrikerTribe { get; init; } = -1;
}

/// <summary>
/// Blows on a cell: whatever is there takes damage, depending on what the striker expected to hit
/// and how far that has moved. The player's units are halved under protection, buildings take
/// half and call for help, units turn on their attacker, and what reaches 0 hit points dies.
/// </summary>
internal sealed class CellDamage
{
    /// <summary>Distance assumed when the striker expected an empty cell.</summary>
    private const int NoTargetDistance = 10;

    /// <summary>Units of the player within this many cells of their chief strike half as hard again (3 halves).</summary>
    private const int ChiefPresence = 4, ChiefBonusHalves = 3, Halves = 2;

    /// <summary>Alarm radius of a building attacked for the first time.</summary>
    private const int BuildingAlarmRadius = 8;

    /// <summary>Bit of <see cref="Building.AlertFlags"/> set while a building is under attack.</summary>
    private const int UnderAttack = 0x80;

    /// <summary>The alarm order mode, and an action alarmed units do not leave.</summary>
    private const int AlarmedOrder = 4, UnalarmableAction = 50;

    /// <summary>Hints and notices about the player's units being hit.</summary>
    private const int ChildHitHint = 16, AnimalAttackHint = 5, UnderAttackNotice = 7, AttackNoticeChannel = 0;

    /// <summary>The damage number's look: base, 3 steps per 4 points of damage, random spread, largest.</summary>
    private const int StyleBase = 52, StyleDamageUnit = 4, StyleSteps = 3, StyleSpreadMask = 255, StyleSpread = 3, StyleLargest = 61;

    /// <summary>A building's anchor packs the row offset above this many bits.</summary>
    private const int AnchorRowShift = 4;

    private readonly GameWorld world;
    private readonly GridDistances distances;
    private readonly CellClassifier cells;
    private readonly SectorSearch sectors;
    private readonly Occupancy occupancy;
    private readonly HintSystem hints;
    private readonly Deaths deaths;
    private readonly Action<GameEvent> raise;
    private readonly IReadOnlyList<CellCategory> categories;

    /// <summary>Lands blows in <paramref name="world"/>.</summary>
    public CellDamage(GameWorld world, GridDistances distances, CellClassifier cells, SectorSearch sectors,
        Occupancy occupancy, HintSystem hints, Deaths deaths, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        this.sectors = sectors ?? throw new ArgumentNullException(nameof(sectors));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        categories = world.Content.Rules.Targets.CellCategories;
    }

    /// <summary>
    /// <paramref name="striker"/> strikes <paramref name="cell"/> for <paramref name="damage"/>,
    /// aiming at slot <paramref name="target"/> of cell category <paramref name="expectedCategory"/>.
    /// If that target has moved but is still within <paramref name="reach"/>, the blow follows it.
    /// The damage is divided by 1 + the distance between the cell and the target. What the cell
    /// holds is judged as the striker's tribe sees it (animals see as the player does), so a unit
    /// out of sight is missed; in a match animals see everything). Units and buildings of the
    /// striker's own tribe and of its allies are not hurt. Returns whether the target took damage
    /// and survived.
    /// </summary>
    public bool Strike(Striker striker, GridPoint cell, int expectedCategory, int target, int anchor, int damage, int reach)
    {
        int distance = TargetDistance(cell, expectedCategory, target, out GridPoint targetCell);
        if (distance <= reach && distance > 0)
        {
            cell = targetCell;
        }
        int viewer = !striker.IsAnimal ? striker.Tribe
            : world.Mode == WorldMode.Campaign ? TribeRoles.Player(world) : CellClassifier.NoViewer;
        CellProbe probe = cells.ProbeCell(cell, viewer);
        if (categories[probe.Category].HitPointsProperty is not { } hitPoints
            || EntityProperties.Get(world, hitPoints, probe.Slot) <= 0)
        {
            return false;
        }
        bool sided = probe.Category == CellCategories.Unit || probe.Category == CellCategories.Building;
        if (!striker.IsAnimal && !probe.OtherSide && sided)
        {
            return false;
        }
        damage = Weaken(striker, probe, damage) / (distance + 1);
        if (damage < 1)
        {
            return false;
        }
        Provoke(striker, probe);
        EntityProperties.Set(world, hitPoints, probe.Slot, EntityProperties.Get(world, hitPoints, probe.Slot) - damage);
        if (EntityProperties.Get(world, hitPoints, probe.Slot) <= 0)
        {
            deaths.Die(hitPoints, cell, probe.Slot, updatedLaterThisFrame: !striker.IsAnimal && probe.Slot > striker.Slot,
                killer: striker.IsAnimal ? -1 : striker.Tribe);
            return false;
        }
        ShowBlow(striker, probe, cell, damage, anchor);
        WarnPlayer(striker, probe, cell);
        return true;
    }

    private int TargetDistance(GridPoint cell, int expectedCategory, int target, out GridPoint targetCell)
    {
        targetCell = cell;
        if (expectedCategory == CellCategories.Empty)
        {
            return NoTargetDistance;
        }
        if (expectedCategory == CellCategories.Building || categories[expectedCategory].PositionProperty is not { } position)
        {
            return 0;
        }
        targetCell = HoldsSlot(expectedCategory, target)
            ? new GridPoint(EntityProperties.Get(world, position, target), EntityProperties.Get(world, position + 1, target))
            : default;
        return GridDistances.Cells(distances.Measure(targetCell.X - cell.X, targetCell.Y - cell.Y));
    }

    /// <summary>The damage after the presence of the striker's chief, a building's sturdiness and protection.</summary>
    private int Weaken(Striker striker, CellProbe probe, int damage)
    {
        if (!striker.IsAnimal && TribeRoles.IsPlayer(world, striker.Tribe) && NearChief(world.Unit(striker.Slot), striker.Tribe))
        {
            damage = damage * ChiefBonusHalves / Halves;
        }
        if (probe.Category == CellCategories.Building)
        {
            Building building = world.Building(probe.Slot);
            if ((building.AlertFlags & UnderAttack) == 0)
            {
                sectors.RaiseAlarm(building.Tribe, Centre(building), BuildingAlarmRadius, striker.CellCode);
            }
            building.AlertFlags |= UnderAttack;
            occupancy.Evict(building, all: true);
            damage = Halve(damage);
        }
        if (probe.Category == CellCategories.Unit && (world.Unit(probe.Slot).Status & UnitStatus.Protected) != 0)
        {
            damage = Halve(damage);
        }
        return damage;
    }

    /// <summary>A unit hit turns on the striker if it is free to; an animal hit by a unit remembers who did it.</summary>
    private void Provoke(Striker striker, CellProbe probe)
    {
        if (probe.Category == CellCategories.Unit)
        {
            Unit victim = world.Unit(probe.Slot);
            if (victim.HitPoints > 0 && victim.Age >= UnitAges.Adult && victim.OrderMode == OrderModes.Free
                && victim.Action > ActionNumbers.LastBasicAttack && victim.Action != ActionNumbers.StayInside
                && victim.Action != ActionNumbers.LeaveBuilding && victim.Action != UnalarmableAction)
            {
                victim.OrderMode = AlarmedOrder;
                victim.TargetCellCode = striker.CellCode;
            }
        }
        if (probe.Category == CellCategories.Animal && !striker.IsAnimal && world.Animal(probe.Slot).Target <= 0)
        {
            world.Animal(probe.Slot).ProvokeTimer = Wildlife.AnimalTimers.Provoked + striker.Slot;
        }
    }

    private void ShowBlow(Striker striker, CellProbe probe, GridPoint cell, int damage, int anchor)
    {
        if (probe.Category == CellCategories.Building)
        {
            Building building = world.Building(probe.Slot);
            anchor = cell.X - building.X + ((cell.Y - building.Y) << AnchorRowShift);
        }
        int style = GridMath.Clamp(StyleBase + (damage / StyleDamageUnit * StyleSteps) + world.Random.NextMasked(StyleSpreadMask, StyleSpread),
            StyleBase, StyleLargest);
        raise(new BlowLanded(probe.Category, probe.Slot, damage, anchor, style) { StrikerTribe = striker.IsAnimal ? -1 : striker.Tribe });
    }

    private void WarnPlayer(Striker striker, CellProbe probe, GridPoint cell)
    {
        if (probe.Category != CellCategories.Unit || !TribeRoles.IsPlayer(world, world.TribeOfUnit(probe.Slot)))
        {
            return;
        }
        Unit victim = world.Unit(probe.Slot);
        HintState state = TribeRoles.Hints(world, victim.Tribe);
        if (victim.Age < UnitAges.Adult)
        {
            hints.Offer(victim.Tribe, ChildHitHint, world.Tribes[victim.Tribe][TribeCounter.Chief], victim.Id);
        }
        else if (state.Cooldown(AttackNoticeChannel) < 0)
        {
            state.SetCooldown(AttackNoticeChannel, 0);
            hints.Notice(victim.Tribe, UnderAttackNotice, cell.X, cell.Y, 0);
        }
        else if (striker.IsAnimal && victim.Action != ActionNumbers.HuntMelee && victim.Action != ActionNumbers.HuntRanged)
        {
            hints.Offer(AnimalAttackHint, victim.Id, victim.Id);
        }
    }

    private bool NearChief(Unit striker, int tribe)
    {
        int chief = world.Tribes[tribe][TribeCounter.Chief];
        if (chief < 1 || chief > world.UnitSlots)
        {
            return false;
        }
        Unit head = world.Unit(chief);
        return Math.Abs(striker.X - head.X) <= ChiefPresence && Math.Abs(striker.Y - head.Y) <= ChiefPresence;
    }

    private GridPoint Centre(Building building)
    {
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        return new GridPoint(building.X + (kind.Width / 2), building.Y + (kind.Height / 2));
    }

    /// <summary>
    /// Whether <paramref name="slot"/> is a slot of <paramref name="category"/>; the original reads
    /// the unused slot 0 otherwise, which stands at cell 0, 0.
    /// </summary>
    private bool HoldsSlot(int category, int slot) => category switch
    {
        CellCategories.Unit => slot >= 1 && slot <= world.UnitSlots,
        CellCategories.Animal => slot >= 0 && slot < world.AnimalSlots,
        _ => false,
    };

    private static int Halve(int damage) => damage > 1 ? damage / Halves : damage;
}
