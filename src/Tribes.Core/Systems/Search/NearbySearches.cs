using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Paths;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Search;

/// <summary>
/// The rules' spiral searches for units and scenes: more of a resource to gather, a cell of the
/// right ground to go to, a spot beside a hut for a couple, the adults gathered for a scene.
/// </summary>
internal sealed class NearbySearches
{
    /// <summary>Ground tiles (after variants) a unit may be sent to by <see cref="SendToGround"/>.</summary>
    private const int FirstGroundTile = 95, LastGroundTile = 114;

    /// <summary>A spiral covering the whole map.</summary>
    private const int WholeMapRounds = 127;

    /// <summary>The first actor position filled by <see cref="CollectAdults"/>.</summary>
    private const int FirstGatheredActor = 6;

    /// <summary>The order mode <see cref="UnitOrders.FinishOrder"/> gets for a unit sent by a search.</summary>
    private const int SearchOrder = 2;

    private readonly GameWorld world;
    private readonly SpiralSearch spiral;
    private readonly UnitActions actions;
    private readonly RouteFinder routes;
    private readonly TargetDistances distances;
    private readonly UnitOrders orders;

    /// <summary>Searches in <paramref name="world"/>.</summary>
    public NearbySearches(GameWorld world, SpiralSearch spiral, UnitActions actions, RouteFinder routes,
        TargetDistances distances, UnitOrders orders)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
    }

    /// <summary>
    /// Sends <paramref name="unit"/> to the first cell around <paramref name="centre"/> (within
    /// <paramref name="rounds"/> spiral rounds) holding what it is after
    /// (<see cref="UnitActions.MatchesTarget"/>) that it can reach; returns whether one was found.
    /// </summary>
    public bool SendToMoreWork(Unit unit, GridPoint centre, int rounds)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return spiral.Visit(centre, rounds, cell => actions.MatchesTarget(unit, cell.Code) && TrySend(unit, cell.Cell));
    }

    /// <summary>
    /// Sends <paramref name="unit"/> to the first empty cell of open ground (tiles 95..114) around
    /// <paramref name="centre"/> that it can reach; returns whether one was found.
    /// </summary>
    public bool SendToGround(Unit unit, GridPoint centre, int rounds)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return spiral.Visit(centre, rounds, cell =>
        {
            int tile = world.Map.TileAt(cell.Cell);
            return tile >= FirstGroundTile && tile <= LastGroundTile && cell.Code == CellCode.Empty && TrySend(unit, cell.Cell);
        });
    }

    /// <summary>
    /// Sends the partners of <paramref name="couple"/> side by side near the hut entrance: the
    /// first cell (around the entrance) free or held by the couple, whose right neighbour is free
    /// or held by the woman. <paramref name="unit"/> takes its side by its pair bit (0 left, 1
    /// right); its partner, if on its way to the hut, the other side.
    /// </summary>
    public bool PlaceCouple(Unit unit, Couple couple)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(couple);
        int lastColumn = world.Map.Bounds.Right - 1;
        return spiral.Visit(new GridPoint(couple.EntranceX, couple.EntranceY), WholeMapRounds, cell =>
        {
            bool takenByOthers = cell.Code != CellCode.Empty && cell.Code != CellCode.OfUnit(couple.Man)
                && cell.Code != CellCode.OfUnit(couple.Woman);
            if (takenByOthers || cell.Cell.X >= lastColumn)
            {
                return false;
            }
            int right = CellCode.At(world.Map, new GridPoint(cell.Cell.X + 1, cell.Cell.Y));
            if (right != CellCode.Empty && right != CellCode.OfUnit(couple.Woman))
            {
                return false;
            }
            int side = unit.PairLink & 1;
            int partner = side == 1 ? couple.Man : couple.Woman;
            if (partner >= 1 && partner <= world.UnitSlots && world.Unit(partner).Action == ActionNumbers.FirstCoupleAction)
            {
                world.Unit(partner).DestinationX = cell.Cell.X + 1 - side;
                world.Unit(partner).DestinationY = cell.Cell.Y;
            }
            unit.DestinationX = cell.Cell.X + side;
            unit.DestinationY = cell.Cell.Y;
            return true;
        });
    }

    /// <summary>
    /// Adds the living adults of <paramref name="tribe"/> around <paramref name="centre"/> to the
    /// scene's actors, from position 6 on, in spiral order; returns how many were added (no more
    /// than the actor positions hold).
    /// </summary>
    public int CollectAdults(GridPoint centre, int rounds, int tribe)
    {
        int added = 0;
        spiral.Visit(centre, rounds, cell =>
        {
            int slot = CellCode.UnitOf(cell.Code);
            if (slot > 0 && slot <= world.UnitSlots && world.TribeOfUnit(slot) == tribe
                && world.Unit(slot).HitPoints > 0 && world.Unit(slot).Age >= UnitAges.Adult
                && FirstGatheredActor + added < ScriptState.ActorSlots)
            {
                world.Script.SetActor(FirstGatheredActor + added, slot);
                added++;
            }
            return false;
        });
        return added;
    }

    /// <summary>
    /// Sends <paramref name="unit"/> to <paramref name="cell"/> if it can get there: a route is
    /// planned, and without one the cell must be within the reach of its action. A unit with
    /// orders, or gathering plants, fish or a second kind of food, makes the cell its task site.
    /// </summary>
    private bool TrySend(Unit unit, GridPoint cell)
    {
        unit.DestinationX = cell.X;
        unit.DestinationY = cell.Y;
        unit.TargetIndex = -1;
        routes.RequestRoute(unit);
        bool reachable = unit.PathLength > 0
            || distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Cells <= routes.ActionRange(unit);
        if (!reachable)
        {
            return false;
        }
        if (unit.OrderMode > OrderModes.Free || unit.Action is ActionNumbers.FirstGathering or ActionNumbers.Fishing or GatherSecondFood)
        {
            unit.TaskSiteX = unit.DestinationX;
            unit.TaskSiteY = unit.DestinationY;
        }
        orders.FinishOrder(unit, SearchOrder);
        return true;
    }

    /// <summary>Gathering the second kind of plant food (node kind 4).</summary>
    private const int GatherSecondFood = 15;
}
