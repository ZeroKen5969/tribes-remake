using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// What a unit is doing: switching actions, what it carries and how it walks or stands. Systems
/// switch a unit's action through <see cref="SetAction"/>, which resets the action's progress the
/// way the action table expects (a few rules of the original set the action number directly).
/// </summary>
internal sealed class UnitActions
{
    /// <summary>The weapon kind that is a pile of stones: units carrying it walk as with a load.</summary>
    public const int StoneWeapon = WeaponNumbers.Stone;

    /// <summary>Mask of the movement tick counter in <see cref="Unit.StepAndActionTicks"/>.</summary>
    private const int StepTickMask = 0x0F;

    private const int NoCarriedItem = 0;
    private const int KeepsCarriedItem = -1;

    /// <summary>Node kind 0 marks a free node slot; real kinds start at 1.</summary>
    private const int FirstNodeKind = 1;


    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> actions;
    private readonly HashSet<int> nodeGatheringActions;
    private readonly CellCategory nodeCells;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitActions(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        actions = world.Content.Rules.Actions.Actions;
        IReadOnlyList<int> gathering = world.Content.Rules.Targets.GatherActionByNodeKind;
        nodeGatheringActions = new HashSet<int>();
        for (int kind = FirstNodeKind; kind < gathering.Count; kind++)
        {
            nodeGatheringActions.Add(gathering[kind]);
        }
        nodeCells = world.Content.Rules.Targets.CellCategories[Map.CellCategories.ResourceNode];
    }

    /// <summary>The definition of <paramref name="action"/>.</summary>
    public ActionDefinition Definition(int action) => actions[action];

    /// <summary>
    /// Switches <paramref name="unit"/> to <paramref name="action"/>. A new action starts from
    /// scratch (no progress, no action ticks) and decides what the unit carries; the effect
    /// recipient is always set again from the action's definition.
    /// </summary>
    public void SetAction(Unit unit, int action)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Action != action)
        {
            unit.WorkProgress = 0;
            unit.Action = action;
            unit.StepAndActionTicks &= StepTickMask;
            if (action < ActionNumbers.FirstInterruption || action > ActionNumbers.LastInterruption)
            {
                UpdateCarriedItem(unit);
            }
        }
        EntityProperty? source = actions[action].EffectObjectProperty;
        unit.EffectRecipient = source is { } property ? EntityProperties.Get(world, property, unit.Id) : unit.Id;
    }

    /// <summary>
    /// Makes the carried item match the unit's action: an action with an item of its own replaces
    /// a different one (dropping the amount); an action without one empties the hands once the
    /// amount is used up. A builder sitting at its site keeps what it brought.
    /// </summary>
    public void UpdateCarriedItem(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Action == ActionNumbers.WaitForMaterial && IsMaterialWaitAnimation(unit.AnimationNumber))
        {
            return;
        }
        int item = actions[unit.Action].CarriedItem;
        if (item != KeepsCarriedItem)
        {
            if (unit.CarriedItem != item)
            {
                unit.CarriedAmount = 0;
                unit.CarriedItem = item;
            }
        }
        else if (unit.CarriedAmount == 0)
        {
            unit.CarriedItem = NoCarriedItem;
        }
    }

    /// <summary>
    /// The walking animation of <paramref name="unit"/>: a child's walk, crawling while hidden,
    /// walking with a load, running or walking. Children, loaded units and crawlers stop running.
    /// </summary>
    public static int WalkingAnimation(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Age < UnitAges.Adult)
        {
            unit.RunBonus = 0;
            return AnimationNumbers.ChildWalk;
        }
        bool hidden = (unit.Status & UnitStatus.Hidden) != 0;
        bool runsFreely = unit.RunBonus != 0 && unit.CarriedItem == NoCarriedItem && unit.Weapon != StoneWeapon;
        if (hidden && !runsFreely)
        {
            unit.RunBonus = 0;
            return AnimationNumbers.Crawl;
        }
        if (unit.CarriedItem != NoCarriedItem || unit.Weapon == StoneWeapon)
        {
            unit.RunBonus = 0;
            return AnimationNumbers.CarryWalk;
        }
        return unit.RunBonus > 0 ? AnimationNumbers.Run : AnimationNumbers.Walk;
    }

    /// <summary>Lets <paramref name="unit"/> stand still: the standing animation of its way of walking.</summary>
    public static void Stand(Unit unit) =>
        unit.Animation = (WalkingAnimation(unit) % AnimationNumbers.WalkKinds) + AnimationNumbers.FirstStanding;

    /// <summary>Drops whatever <paramref name="unit"/> was doing: it idles (as a child or an adult).</summary>
    public void Stop(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        SetAction(unit, unit.Age >= UnitAges.Adult ? ActionNumbers.Idle : ActionNumbers.ChildIdle);
    }

    /// <summary>
    /// Whether the content of a cell (given as its cell code) is what <paramref name="unit"/> is
    /// after: the very target it was sent to, or for a gatherer any resource node of the kind it
    /// gathers that still has something (carcasses and bones count as one kind).
    /// </summary>
    public bool MatchesTarget(Unit unit, int cellCode)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (cellCode == unit.TargetCellCode && !nodeGatheringActions.Contains(unit.Action))
        {
            return true;
        }
        int slot = cellCode >= nodeCells.CodeMin && cellCode <= nodeCells.CodeMax
            ? (cellCode * nodeCells.IndexMultiplier) + nodeCells.IndexOffset
            : Maps.CellCode.NodeOf(cellCode);
        if (unit.Action < ActionNumbers.FirstGathering || unit.Action > ActionNumbers.LastGathering
            || slot < 0 || slot >= world.Map.ResourceNodes.Count)
        {
            return false;
        }
        Maps.ResourceNode node = world.Map.ResourceNodes[slot];
        int gathered = world.Content.Rules.Targets.GatherActionByNodeKind[node.Kind];
        return (IsCarcassOrBones(unit.Action) && IsCarcassOrBones(gathered) && node.Amount > 0)
            || (node.Stage > 0 && gathered == unit.Action);
    }

    private static bool IsCarcassOrBones(int action) =>
        action is ActionNumbers.GatherCarcass or ActionNumbers.GatherBones;

    private static bool IsMaterialWaitAnimation(int animation) =>
        animation >= AnimationNumbers.FirstMaterialWait && animation <= AnimationNumbers.LastMaterialWait;
}
