using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>A cell next to a building where units go in.</summary>
/// <param name="Offset">The cell, relative to the building's top-left cell.</param>
/// <param name="Facing">
/// The facing of the last half step into the building (only 1, 3, 5 and 7 occur where it matters).
/// </param>
public sealed record BuildingEntrance(CellOffset Offset, int Facing);

/// <summary>One training a building gives the units inside it.</summary>
/// <param name="Stat">The stat trained (index into <see cref="SkillTables.TrainableStats"/>).</param>
/// <param name="ChancePercent">Chance of each training check succeeding.</param>
/// <param name="Cap">The stat is trained while value * Step &lt; Cap * Step.</param>
/// <param name="Step">Change per success (training maximum hit points also heals as much).</param>
public sealed record TrainingEffect(int Stat, int ChancePercent, int Cap, int Step);

/// <summary>A work position inside a building (<see cref="World.Unit.BuildingSlot"/>).</summary>
/// <param name="IdleAction">The action of a unit in the position on entering, or when there is no work.</param>
/// <param name="WorkAction">
/// The action when there is work (a site below its stage limit, food in a kitchen...).
/// </param>
/// <param name="EnterAnimation">The animation set when a unit takes the position.</param>
/// <param name="ExitAnimation">
/// The animation played when it leaves; a unit already playing it is not sent out.
/// </param>
public sealed record BuildingSlot(int IdleAction, int WorkAction, int EnterAnimation, int ExitAnimation);

/// <summary>How units use a building of kinds 0..19 (buildings and construction sites).</summary>
/// <param name="EntranceCount">
/// How many of <see cref="Entrances"/> are tried (the free one nearest to the unit wins); 0 uses
/// the first without checking it.
/// </param>
/// <param name="Entrances">Three entrance cells; only the first <see cref="EntranceCount"/> count.</param>
/// <param name="ExitOrigin">
/// Where a leaving unit starts looking for a free cell, relative to the top-left cell.
/// </param>
/// <param name="ExitFacing">The facing of a unit that has left.</param>
/// <param name="UseAction">The action a unit takes when sent to the building of its own tribe.</param>
/// <param name="UseActionWhenOccupied">The action when the building is already run by another unit.</param>
/// <param name="ActionWhileBusy">The action when the building is being upgraded or repaired.</param>
/// <param name="TrainingEffects">The trainings the building gives, by level 1..3 (index level - 1).</param>
/// <param name="TrainsOnceAtEnd">
/// Whether the trainings are applied once when the action ends (otherwise every work step).
/// </param>
/// <param name="TrainsAllOccupants">Whether every unit inside benefits (otherwise only the operator).</param>
/// <param name="Slots">Work positions while the building works normally, by position.</param>
/// <param name="SlotsWhileBusy">Work positions while it is upgraded or repaired, by position.</param>
/// <param name="SlotPlacesWhileBusy">
/// For kinds 0..11 while upgraded or repaired: the place among the
/// <see cref="BuildingKind.WorkerSlotsWhileBusy"/> places each work position takes; empty for other kinds.
/// </param>
/// <param name="PlaceSlotsWhileBusy">The reverse of <see cref="SlotPlacesWhileBusy"/>: the work position of each place.</param>
public sealed record BuildingOperation(int EntranceCount, IReadOnlyList<BuildingEntrance> Entrances,
    CellOffset ExitOrigin, int ExitFacing, int UseAction, int UseActionWhenOccupied, int ActionWhileBusy,
    IReadOnlyList<IReadOnlyList<TrainingEffect>> TrainingEffects, bool TrainsOnceAtEnd, bool TrainsAllOccupants,
    IReadOnlyList<BuildingSlot> Slots, IReadOnlyList<BuildingSlot> SlotsWhileBusy,
    IReadOnlyList<int> SlotPlacesWhileBusy, IReadOnlyList<int> PlaceSlotsWhileBusy);

/// <summary>How a construction site of one target kind (0..11) is built.</summary>
/// <param name="DeliveryActions">
/// The fetch action (38..43) of each material delivery, in order; the site needs this many deliveries.
/// </param>
/// <param name="ProgressHitPoints">
/// The site's hit point limit after each number of deliveries (0 up to all): the first is the
/// starting 5, the last the finished building's.
/// </param>
public sealed record ConstructionPlan(IReadOnlyList<int> DeliveryActions, IReadOnlyList<int> ProgressHitPoints);

/// <summary>
/// A building kind (<see cref="World.Building.Kind"/>): 0..11 the buildings (armoury, gym, hut,
/// chief's hut, kitchen, range, store, disco, sanctuary, pia center, nursery, tower), 12..19
/// construction sites of different footprints, 20..22 the traps (net, spring, pit), 23 unknown,
/// 24..25 placed only by a story scene. Levels are 1..3 (sites are always 3).
/// </summary>
/// <param name="Id">Kind number 0..25.</param>
/// <param name="WorkerSlots">
/// Units that can work or live inside at levels 1..3 while it works normally (for a hut: beds;
/// for a site: builders at level 3).
/// </param>
/// <param name="Unknown3">
/// A column of the original's table nothing reads (it holds the kind number + 1); kept as data.
/// </param>
/// <param name="Width">Footprint width in cells.</param>
/// <param name="Height">Footprint height in cells.</param>
/// <param name="ManagerSlot">
/// The work position kept for the operator (the building does nothing without one), or -1 when it
/// needs no operator.
/// </param>
/// <param name="MaxHitPoints">
/// Hit point limit at levels 1..3; the level 1 value is also a new building's hit points, and a
/// site is finished when its hit points reach its target kind's level 1 value.
/// </param>
/// <param name="BuildCost">
/// Wood, stones and bones a construction costs; a repair costs BuildCost * missing hit points / (2 * MaxHitPoints).
/// For kinds 12..17 the same cells hold the armoury menu's weapon costs (<see cref="WeaponDisplay.Cost"/>).
/// </param>
/// <param name="UpgradeCost">
/// Wood, stones and bones an upgrade costs, whatever the level. For kinds 12..17 the first two
/// cells hold the armoury menu's weapon display instead (<see cref="WeaponDisplay.IsThrown"/>,
/// <see cref="WeaponDisplay.Attack"/>).
/// </param>
/// <param name="SiteKind">For kinds 0..11, the construction site kind placed to build it; 0 for the others.</param>
/// <param name="Unknown17">A column nothing reads (kinds 0..11 hold 1, 2, 0, 3, 4 ... 11); kept as data.</param>
/// <param name="CostTypeCount">How many of the costs are non-zero; only used to centre the cost display.</param>
/// <param name="WorkerSlotsWhileBusy">The worker slots while it is upgraded or repaired.</param>
/// <param name="HitPointBarOffset">How far up the hit point bar is drawn, in steps of 8 pixels.</param>
/// <param name="InsideHappinessBonus">
/// Added to the happiness of a unit inside at each mood update, at levels 1..3 (kept within 0..99).
/// </param>
/// <param name="Operation">How units use the building (kinds 0..19), or null.</param>
/// <param name="Construction">How a site for this kind is built (kinds 0..11), or null.</param>
public sealed record BuildingKind(int Id, IReadOnlyList<int> WorkerSlots, int Unknown3, int Width, int Height,
    int ManagerSlot, IReadOnlyList<int> MaxHitPoints, IReadOnlyList<int> BuildCost, IReadOnlyList<int> UpgradeCost,
    int SiteKind, int Unknown17, int CostTypeCount, int WorkerSlotsWhileBusy, int HitPointBarOffset,
    IReadOnlyList<int> InsideHappinessBonus, BuildingOperation? Operation, ConstructionPlan? Construction);

/// <summary>The building kinds.</summary>
/// <param name="Kinds">Building kinds by number.</param>
public sealed record BuildingTables(IReadOnlyList<BuildingKind> Kinds);
