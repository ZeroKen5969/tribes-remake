using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Resources;

/// <summary>
/// The resource nodes over time: carcasses rot, plants and fish grow back, a carcass eaten up
/// leaves bones, bones eaten up disappear, and the units gathering a node that has run out stop.
/// Each node's stage (how full it looks, also read when units pick a node) follows its amount.
/// </summary>
internal sealed class NodeUpkeep
{
    /// <summary>A carcass loses one unit of food on frames where (frame &amp; this) is 1.</summary>
    public const int RotPeriodMask = 127;

    /// <summary>Plants and fish grow one unit on frames where (frame &amp; this) is 1.</summary>
    public const int GrowthPeriodMask = 511;

    /// <summary>A node is checked for running out on frames where (frame &amp; this) equals (slot &amp; this).</summary>
    public const int DepletionCheckMask = 7;

    /// <summary>The frame phase of rotting and growing.</summary>
    private const int UpkeepPhase = 1;

    /// <summary>Stage limits are tenths of the full amount.</summary>
    private const int Tenths = 10;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly Action<Unit> findNextWork;

    /// <summary>
    /// Keeps the nodes of <paramref name="world"/>; <paramref name="findNextWork"/> lets a unit
    /// that stopped gathering decide what to do next.
    /// </summary>
    public NodeUpkeep(GameWorld world, UnitActions actions, Action<Unit> findNextWork)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.findNextWork = findNextWork ?? throw new ArgumentNullException(nameof(findNextWork));
    }

    /// <summary>One frame of every node, in slot order.</summary>
    public void RunFrame()
    {
        foreach (ResourceNode node in world.Map.ResourceNodes)
        {
            if (node.InUse)
            {
                Update(node);
            }
        }
    }

    /// <summary>One frame of <paramref name="node"/>: rotting or growing, the depletion check, the stage.</summary>
    public void Update(ResourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int frame = world.Frame;
        if ((frame & RotPeriodMask) == UpkeepPhase && node.Kind == NodeKinds.Carcass)
        {
            if (node.Amount > 0)
            {
                node.Amount--;
            }
        }
        else if ((frame & GrowthPeriodMask) == UpkeepPhase && node.Kind != NodeKinds.Bones && node.Amount < FullAmount(node.Kind))
        {
            node.Amount++;
        }
        if ((frame & DepletionCheckMask) == (node.Index & DepletionCheckMask))
        {
            CheckDepleted(node);
        }
        UpdateStage(node);
    }

    /// <summary>
    /// A node that has run out: bones disappear (the cell is freed), a carcass turns into bones,
    /// other nodes stay to grow back; the units gathering it stop.
    /// </summary>
    public void CheckDepleted(ResourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Amount != 0)
        {
            return;
        }
        if (node.Kind == NodeKinds.Bones)
        {
            node.Kind = 0;
            world.Map.SetCell(node.Position, MapCell.Empty);
        }
        if (node.Kind == NodeKinds.Carcass)
        {
            node.Kind = NodeKinds.Bones;
            node.Amount = FullAmount(NodeKinds.Bones);
        }
        StopGatherers(node);
    }

    /// <summary>
    /// The units gathering at the node's cell (a fisher anywhere on its row) drop it; one carrying
    /// meat or bones takes them home and decides what to do next.
    /// </summary>
    public void StopGatherers(ResourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            bool gathering = unit.Action >= ActionNumbers.FirstGathering && unit.Action <= ActionNumbers.LastGathering;
            if (!gathering || unit.DestinationY != node.Y || (unit.DestinationX != node.X && unit.Action != ActionNumbers.Fishing))
            {
                continue;
            }
            if (unit.TargetIndex >= 0)
            {
                unit.TargetIndex = -1;
                unit.PathLength = 0;
            }
            if ((unit.Action == ActionNumbers.GatherBones || unit.Action == ActionNumbers.GatherCarcass) && unit.CarriedAmount > 0)
            {
                actions.SetAction(unit, world.Content.Rules.Actions.Actions[unit.Action].NextActionOnComplete);
                findNextWork(unit);
            }
        }
    }

    /// <summary>The node's stage: the first whose share of the full amount the amount does not exceed.</summary>
    private void UpdateStage(ResourceNode node)
    {
        IReadOnlyList<int> tenths = world.Content.Rules.Targets.DepletionStageTenths;
        int full = FullAmount(node.Kind);
        for (int stage = 0; stage < tenths.Count; stage++)
        {
            if (node.Amount <= full * tenths[stage] / Tenths)
            {
                node.Stage = stage;
                return;
            }
        }
    }

    /// <summary>The amount a node of <paramref name="kind"/> holds when full (0 for a free slot).</summary>
    private int FullAmount(int kind) =>
        world.Content.ResourceNodeKinds.TryGetValue(kind, out ResourceNodeKind? definition) ? definition.MaxAmount : 0;
}
