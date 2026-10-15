using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>What a unit sent to a cell would do there.</summary>
/// <param name="RefusalHint">The hint explaining a refusal, or <see cref="TargetChecks.NoRefusal"/>.</param>
/// <param name="Category">The category whose target action applies.</param>
/// <param name="Action">The action picked.</param>
/// <param name="Probe">What the unit's tribe sees in the cell.</param>
internal readonly record struct OrderChoice(int RefusalHint, int Category, int Action, CellProbe Probe);

/// <summary>
/// Orders reaching their end: what a unit does at the cell it was sent to (the action for what is
/// there, its long-term task, the entrance of a building), or settling inside the building it is in.
/// </summary>
internal sealed class UnitOrders
{
    /// <summary>The refusal hint "the chief's hut is for the chief", which a walking order ignores.</summary>
    public const int ChiefsHutRefusal = 10;

    /// <summary>The mission where units are sent to fight only.</summary>
    public const int NoWorkMission = MissionNumbers.War;

    /// <summary>Run bonus given to units finishing an order while the story wants them quick.</summary>
    private const int StoryRunBonus = 3;

    /// <summary>The objective of the kidnapping mission from which the computer's units run.</summary>
    private const int RunningEnemyObjective = 1;

    /// <summary>
    /// Story behaviour masks (one bit per tribe) under which every unit finishing an order runs:
    /// only the first tribe scripted, or only the second.
    /// </summary>
    private const int FirstTribeOnlyMask = 1, SecondTribeOnlyMask = 2;

    /// <summary>The sanctuary position of a patient waiting for the witch doctor.</summary>
    private const int PatientPosition = 1;

    /// <summary>Task categories (hunting, gathering bones) whose units go on to carcasses and bones they are sent to.</summary>
    private const int HuntingCategory = 3, BoneGatheringCategory = 7;

    /// <summary>The construction category of actions (builders inside play the busy exit animation).</summary>
    private const int ConstructionCategory = 12;

    /// <summary>Frames without fighting before units take on a peaceful task by themselves.</summary>
    private const int QuietFrames = 50;

    private const int LastDepositItem = 13, NoDistance = 10000, InsideSubState = -3;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> definitions;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly UnitActions actions;
    private readonly CoupleLinks couples;
    private readonly Occupancy occupancy;
    private readonly BuildingSlotTables slots;
    private readonly TargetActionTable targetActions;
    private readonly CellClassifier cells;
    private readonly TargetChecks checks;
    private readonly TargetDistances distances;
    private readonly Passability passability;
    private readonly HintSystem hints;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitOrders(GameWorld world, UnitActions actions, CoupleLinks couples, Occupancy occupancy, BuildingSlotTables slots,
        TargetActionTable targetActions, OrderServices services)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        this.slots = slots ?? throw new ArgumentNullException(nameof(slots));
        this.targetActions = targetActions ?? throw new ArgumentNullException(nameof(targetActions));
        ArgumentNullException.ThrowIfNull(services);
        (cells, checks, distances, passability, hints) =
            (services.Cells, services.Checks, services.Distances, services.Passability, services.Hints);
        definitions = world.Content.Rules.Actions.Actions;
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// <paramref name="unit"/> has carried out its order (<paramref name="mode"/>: 0 its own choice,
    /// 1 a kept order, 2 a walk or forced order, 3 a couple's move). A walking order now keeps, a
    /// forced one frees the unit; its couple may be dissolved; inside a building it settles there,
    /// otherwise it takes up what is at its destination, or stops if it refuses (with a hint for a
    /// kept order).
    /// </summary>
    public void FinishOrder(Unit unit, int mode)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.OrderMode > OrderModes.Keep)
        {
            unit.OrderMode = unit.OrderMode == OrderModes.Walk ? OrderModes.Keep : OrderModes.Free;
            if (StoryWantsRunning(unit))
            {
                unit.RunBonus = StoryRunBonus;
            }
        }
        if (mode < FinishModes.CoupleMove)
        {
            couples.Dissolve(unit, mode);
        }
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            SettleInside(unit);
            return;
        }
        var destination = new GridPoint(unit.DestinationX, unit.DestinationY);
        if (!world.Map.Contains(destination))
        {
            return;
        }
        OrderChoice choice = PickAction(unit, destination, mode > FinishModes.OwnChoice);
        if (choice.RefusalHint != TargetChecks.NoRefusal && (choice.RefusalHint != ChiefsHutRefusal || mode < FinishModes.Walked))
        {
            unit.DestinationX = unit.X;
            unit.DestinationY = unit.Y;
            actions.Stop(unit);
            if (unit.OrderMode == OrderModes.Keep)
            {
                hints.Offer(choice.RefusalHint, unit.Id, unit.Id);
            }
            return;
        }
        TakeOrder(unit, choice, mode);
    }

    /// <summary>
    /// What <paramref name="unit"/> would do at <paramref name="cell"/>, judged by the cell content
    /// it was sent to (which <paramref name="retarget"/> first takes from the cell): the target
    /// action of its own side's content (a store takes deliveries, a finished site is nothing to do),
    /// capturing an enemy child, or attacking the enemy; basic attacks become ranged with a thrown
    /// weapon, and in mission 3 nobody takes up work.
    /// </summary>
    public OrderChoice PickAction(Unit unit, GridPoint cell, bool retarget)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (retarget)
        {
            unit.TargetCellCode = CellCode.At(world.Map, cell);
        }
        CellProbe probe = cells.ProbeTarget(unit, cell, unit.Tribe);
        OrderCheck check = checks.CheckOrder(unit, probe);
        int action = probe.OtherSide ? EnemyAction(probe, check.Category, retarget) : OwnSideAction(unit, probe, check.Category, retarget);
        if (action <= ActionNumbers.LastBasicAttack)
        {
            action += world.Content.Rules.Weapons.Weapons[unit.Weapon].RangedActionOffset;
        }
        if (world.Mission == NoWorkMission && definitions[action].Category != ActionCategoryNumbers.Free)
        {
            action = ActionNumbers.Idle;
        }
        return new OrderChoice(check.RefusalHint, check.Category, action, probe);
    }

    /// <summary>
    /// <paramref name="unit"/> takes up <paramref name="choice"/>: it aims at the cell's content,
    /// may make the action its long-term task (on its own choice, or when sent to a carcass or bones
    /// while its task is hunting or gathering bones, and only in quiet times for idle actions), and
    /// adults start the action. A fisher steps onto the shore; a unit to work inside picks an entrance.
    /// </summary>
    public void TakeOrder(Unit unit, OrderChoice choice, int mode)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int action = choice.Action;
        unit.TargetIndex = choice.Probe.Slot;
        bool allOut = TribeRoles.Army(world, unit.Tribe).AllOut;
        if (action is ActionNumbers.HuntMelee or ActionNumbers.HuntRanged)
        {
            MarkOneOffHunt(unit, mode);
        }
        bool meatOrBonesForHunter = !allOut && action is ActionNumbers.GatherCarcass or ActionNumbers.GatherBones
            && definitions[unit.AssignedTask].Category is HuntingCategory or BoneGatheringCategory;
        bool idleAction = definitions[action].Category == ActionCategoryNumbers.Free;
        bool quiet = TribeRoles.FramesSinceCombat(world, unit.Tribe) >= QuietFrames;
        if ((mode == FinishModes.OwnChoice || meatOrBonesForHunter) && ((!allOut && quiet) || !idleAction))
        {
            AdoptTask(unit, choice, mode);
        }
        if (unit.Age >= UnitAges.Adult && (mode < FinishModes.Walked || meatOrBonesForHunter))
        {
            actions.SetAction(unit, action);
        }
        if (unit.Action == ActionNumbers.Fishing)
        {
            StepOntoShore(unit);
        }
        if (definitions[unit.Action].BuildingInteraction == BuildingInteraction.WorkInside)
        {
            PickEntrance(unit, choice.Probe.SubKind, choice.Probe.Slot);
        }
    }

    /// <summary>
    /// Sends <paramref name="unit"/> to the entrance of <paramref name="building"/> (of kind
    /// <paramref name="kind"/>) nearest to it among those it can step on; the first entrance when none
    /// is free. The facing for the last step in is kept. Only buildings have entrances. An entrance
    /// with no cell on the map (a building at its edge) is never taken: when the first entrance has
    /// none the next counted one with a cell stands in for it, and when none has one the building's
    /// own corner cell. (Cells outside the part in play are cells of the map, as in the original.)
    /// </summary>
    public void PickEntrance(Unit unit, int kind, int building)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (building < 1 || building > world.BuildingSlots || kind < 0 || kind >= kinds.Count || kinds[kind].Operation is not { } operation)
        {
            return;
        }
        Building target = world.Building(building);
        int best = FallbackEntrance(target, operation), bestDistance = NoDistance;
        for (int i = 0; i < operation.EntranceCount; i++)
        {
            CellOffset offset = operation.Entrances[i].Offset;
            var cell = new GridPoint(target.X + offset.Dx, target.Y + offset.Dy);
            if (world.Map.InPlay(cell) && !passability.BlocksUnit(unit, cell))
            {
                int dx = unit.X - cell.X, dy = unit.Y - cell.Y;
                int distance = (dx * dx) + (dy * dy);
                if (bestDistance > distance)
                {
                    (bestDistance, best) = (distance, i);
                }
            }
        }
        BuildingEntrance entrance = operation.Entrances[Math.Max(best, 0)];
        CellOffset way = best >= 0 ? entrance.Offset : default;
        unit.DestinationX = target.X + way.Dx;
        unit.DestinationY = target.Y + way.Dy;
        unit.EntranceFacingOrOrder = entrance.Facing;
    }

    /// <summary>
    /// The entrance taken when none is free: the first (also for kinds without counted entrances),
    /// else the first counted one whose cell is on the map; -1 when none is.
    /// </summary>
    private int FallbackEntrance(Building target, BuildingOperation operation)
    {
        for (int i = 0; i < Math.Max(operation.EntranceCount, 1); i++)
        {
            CellOffset offset = operation.Entrances[i].Offset;
            if (world.Map.Contains(new GridPoint(target.X + offset.Dx, target.Y + offset.Dy)))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// <paramref name="unit"/>, inside a building when its order ends, settles there: the building
    /// becomes its target, it plays the animation of leaving its work position (a builder's while the
    /// building is worked on), and unless it only visits it stays inside. A patient arriving at the
    /// sanctuary calls the witch doctor to work.
    /// </summary>
    public void SettleInside(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        distances.AimAtHoldingBuilding(unit, anyKind: true);
        if (unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots)
        {
            Building building = world.Building(unit.TargetIndex);
            CallWitchDoctor(unit, building);
            bool builder = unit.Age >= UnitAges.Adult && building.Kind < BuildingKindNumbers.PlannedKinds
                && (building.WorkMode != BuildingWorkMode.Normal || definitions[unit.Action].Category == ConstructionCategory);
            int? exit = builder ? slots.BusyExitAnimation(building.Kind, unit.BuildingSlot) : slots.ExitAnimation(building.Kind, unit.BuildingSlot);
            unit.Animation = exit ?? unit.Animation;
        }
        unit.AnimationTick = 0;
        unit.ActionSubState = InsideSubState;
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        if (definitions[unit.Action].BuildingInteraction != BuildingInteraction.Visit)
        {
            actions.SetAction(unit, ActionNumbers.StayInside);
        }
    }

    private bool StoryWantsRunning(Unit unit)
    {
        int mask = world.Script.BehaviourMask;
        return mask is FirstTribeOnlyMask or SecondTribeOnlyMask
            || (world.Mission == MissionNumbers.Kidnapping && TribeRoles.IsComputer(world, unit.Tribe)
                && world.Script.ObjectiveStage == RunningEnemyObjective);
    }

    private int OwnSideAction(Unit unit, CellProbe probe, int category, bool retarget)
    {
        int action = targetActions.ActionFor(category, category != CellCategories.TerrainResource ? probe.SubKind : probe.Slot);
        if (category != CellCategories.Building)
        {
            return probe.SubKind >= BuildingKindNumbers.FirstEndlessKind && IsBuildingCategory(category) ? ActionNumbers.Idle : action;
        }
        if (probe.SubKind >= BuildingKindNumbers.FirstEndlessKind)
        {
            // An endless world's own buildings (the alliance building): a unit sent there just stands by it.
            return ActionNumbers.Idle;
        }
        if (probe.SubKind == BuildingKindNumbers.Store && unit.CarriedAmount > 0 && unit.CarriedItem <= LastDepositItem && !retarget)
        {
            action = world.Content.Rules.Targets.DepositActionByCarriedItem[unit.CarriedItem];
        }
        if (probe.SubKind >= BuildingKindNumbers.FirstSite)
        {
            Building site = world.Building(probe.Slot);
            if (site.HitPoints >= kinds[site.Aux].MaxHitPoints[0])
            {
                action = ActionNumbers.Idle;
            }
        }
        return action;
    }

    private static bool IsBuildingCategory(int category) => category is CellCategories.OccupiedBuilding or CellCategories.BusyBuilding;

    private int EnemyAction(CellProbe probe, int category, bool retarget)
    {
        if (probe.Category != CellCategories.Building && world.Unit(probe.Slot).Age < UnitAges.Adult)
        {
            return ActionNumbers.CaptureChild;
        }
        return Wrap.Signed8(world.Content.Rules.Targets.TargetActions.EnemyAttack[category - 1] + (retarget ? 1 : 0));
    }

    private static void MarkOneOffHunt(Unit unit, int mode)
    {
        bool hunting = unit.Action is ActionNumbers.HuntMelee or ActionNumbers.HuntRanged;
        bool oneOff = unit.Action != ActionNumbers.HuntingTask && mode != FinishModes.OwnChoice
            && (!hunting || (unit.Status & UnitStatus.OneOffHunt) != 0);
        unit.Status = oneOff ? unit.Status | UnitStatus.OneOffHunt : unit.Status & ~UnitStatus.OneOffHunt;
    }

    private void AdoptTask(Unit unit, OrderChoice choice, int mode)
    {
        int action = choice.Action;
        bool workInside = definitions[action].BuildingInteraction == BuildingInteraction.WorkInside;
        if (CoupleLinks.IsOnItsCell(world, unit)
            && (!workInside || (OperatorAt(choice.Probe.Slot) != unit.Id && definitions[action].Category != ActionCategoryNumbers.Free)))
        {
            occupancy.ReleaseWorkplaces(unit);
        }
        if (workInside)
        {
            return;
        }
        bool delivery = action >= ActionNumbers.FirstDelivery && action <= ActionNumbers.LastDelivery;
        unit.AssignedTask = mode == FinishModes.OwnChoice && delivery ? ActionNumbers.Idle : definitions[action].StandingTask;
        unit.TaskSiteX = unit.DestinationX;
        unit.TaskSiteY = unit.DestinationY;
    }

    /// <summary>The operator of building slot <paramref name="slot"/> (0 for numbers that are no building slot).</summary>
    private int OperatorAt(int slot) =>
        slot >= 1 && slot <= world.BuildingSlots ? world.Building(slot).OperatorOrBeds : 0;

    /// <summary>A fisher's destination moves onto the shore beside the fishing spot (or onto its own cell).</summary>
    private void StepOntoShore(Unit unit)
    {
        if (unit.DestinationX > world.Map.Bounds.Left && IsShoreFor(unit, unit.DestinationX - 1))
        {
            unit.DestinationX--;
        }
        if (unit.DestinationX < world.Map.Bounds.Right - 1 && IsShoreFor(unit, unit.DestinationX + 1))
        {
            unit.DestinationX++;
        }
    }

    private bool IsShoreFor(Unit unit, int x)
    {
        var cell = new GridPoint(x, unit.DestinationY);
        if (!world.Map.Contains(cell))
        {
            return false;
        }
        int code = CellCode.At(world.Map, cell);
        return code == CellCode.Shore || code == CellCode.OfUnit(unit.Id);
    }

    private void CallWitchDoctor(Unit unit, Building building)
    {
        if (unit.BuildingSlot != PatientPosition || building.Kind != BuildingKindNumbers.Sanctuary || building.WorkMode != BuildingWorkMode.Normal
            || building.OperatorOrBeds < 1 || building.OperatorOrBeds > world.UnitSlots)
        {
            return;
        }
        Unit doctor = world.Unit(building.OperatorOrBeds);
        doctor.Action = ActionNumbers.WitchDoctor;
        doctor.Animation = AnimationNumbers.WitchDoctorAtWork;
        doctor.AnimationTick = 0;
    }
}

/// <summary>The map and world services the order rules use.</summary>
/// <param name="Cells">Cell classification.</param>
/// <param name="Checks">Order refusals.</param>
/// <param name="Distances">The building a unit is in.</param>
/// <param name="Passability">Which cells stop a unit.</param>
/// <param name="Hints">Hints for the player.</param>
internal sealed record OrderServices(CellClassifier Cells, TargetChecks Checks, TargetDistances Distances,
    Passability Passability, HintSystem Hints);
