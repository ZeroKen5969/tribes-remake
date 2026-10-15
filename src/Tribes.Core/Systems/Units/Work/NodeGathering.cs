using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>A gatherer going on with its work: the same spot while it lasts, else the nearest node of the same kind.</summary>
internal sealed class NodeGathering
{
    /// <summary>Gatherers of trees and rocks (whose work does not use up a node) are left out.</summary>
    private const int GatherWood = 11, GatherStones = 14;

    /// <summary>Gatherers of meat and bones go back to hunting when there is none left.</summary>
    private const int GatherMeat = ActionNumbers.GatherCarcass, GatherBones = ActionNumbers.GatherBones;

    /// <summary>Nodes are looked for within this many cells of the task site (the unit's sight from where it stands).</summary>
    private const int TaskSiteReach = 9;

    /// <summary>Further than any node that is looked for.</summary>
    private const int TooFar = 12;

    /// <summary>A unit without work searches this many spiral rounds around a random cell near it.</summary>
    private const int IdleSearchRounds = 19, WanderMask = 0xFF, WanderSpan = 6, WanderOffset = 3;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly GridDistances distances;
    private readonly NearbySearches searches;
    private readonly TargetCycle targets;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>Lets the units of <paramref name="world"/> go on gathering.</summary>
    public NodeGathering(GameWorld world, UnitActions actions, UnitOrders orders, GridDistances distances, NearbySearches searches,
        TargetCycle targets)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.searches = searches ?? throw new ArgumentNullException(nameof(searches));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// The turn's gatherer goes back to its task site while there is something there, else to the
    /// nearest node of the same kind near the site; with nothing left it delivers what it carries,
    /// and a gatherer of meat or bones goes back to hunting. A unit that gathers no nodes looks for
    /// work around a random cell near it.
    /// </summary>
    public void GoOn(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if (unit.Action is < ActionNumbers.FirstGathering or > ActionNumbers.LastGathering or GatherWood or GatherStones)
        {
            int x = world.Map.ClampColumn(unit.X - WanderOffset + world.Random.NextMasked(WanderMask, WanderSpan));
            int y = world.Map.ClampRow(unit.Y - WanderOffset + world.Random.NextMasked(WanderMask, WanderSpan));
            turn.ClearThreat();
            searches.SendToMoreWork(unit, new GridPoint(x, y), IdleSearchRounds);
            return;
        }
        unit.DestinationX = unit.TaskSiteX;
        unit.DestinationY = unit.TaskSiteY;
        if (actions.MatchesTarget(unit, CellCode.At(world.Map, new GridPoint(unit.TaskSiteX, unit.TaskSiteY))))
        {
            orders.FinishOrder(unit, OrderModes.Keep);
            return;
        }
        if (GoToNearbyNode(unit, false))
        {
            return;
        }
        if (unit.CarriedAmount > 0)
        {
            actions.SetAction(unit, definitions[unit.Action].NextActionOnComplete);
            targets.NextTarget(turn);
        }
        else if (unit.Action is GatherMeat or GatherBones)
        {
            actions.SetAction(unit, ActionNumbers.HuntingTask);
            unit.AssignedTask = GatherMeat;
            unit.TargetIndex = 0;
        }
    }

    /// <summary>
    /// Sends <paramref name="unit"/> to the nearest resource node it can work on, within its sight of
    /// where it stands (<paramref name="fromHere"/>) or within 9 cells of its task site; returns
    /// whether there was one.
    /// </summary>
    public bool GoToNearbyNode(Unit unit, bool fromHere)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int fromX = fromHere ? unit.X : unit.TaskSiteX, fromY = fromHere ? unit.Y : unit.TaskSiteY;
        int reach = fromHere ? unit.SightRadius : TaskSiteReach;
        int best = TooFar;
        GridPoint found = default;
        foreach (ResourceNode node in world.Map.ResourceNodes)
        {
            if (node.Kind == 0)
            {
                continue;
            }
            int cells = GridDistances.Cells(distances.MeasureAtEdge(node.X - fromX, node.Y - fromY));
            if (cells <= reach && cells < best && actions.MatchesTarget(unit, CellCode.OfNode(node.Index)))
            {
                found = new GridPoint(node.X, node.Y);
                best = cells;
            }
        }
        if (best >= TooFar)
        {
            return false;
        }
        unit.DestinationX = found.X;
        unit.DestinationY = found.Y;
        orders.FinishOrder(unit, OrderModes.Walk);
        return true;
    }
}
