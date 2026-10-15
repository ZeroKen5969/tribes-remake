using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Paths;

/// <summary>
/// Route planning for units and animals. A unit's route is planned in segments between waypoints
/// (the middle of each obstacle's trace) and stored in <see cref="Unit.Path"/>; an animal's is a
/// single short segment.
/// </summary>
internal sealed class RouteFinder
{
    /// <summary>Waypoints a unit's route is cut to in the war mission.</summary>
    private const int ShortRouteLength = 2;

    /// <summary>Waypoints besides the obstacles' ones: the start and the goal.</summary>
    private const int EndWaypoints = 2;

    /// <summary>A unit blocked more often than this starts counting again when it asks for a new route.</summary>
    private const int BlockedCountKept = 1;

    /// <summary>
    /// The farthest a route is planned at once (cells along the longer axis): the planner's lists
    /// hold a straight line across the original's largest map. A goal farther away, which only an
    /// endless world has, is planned towards in steps: the route ends on the way and the unit plans
    /// again from there.
    /// </summary>
    public const int PlanningReach = TracePath.Size - 1;

    private readonly GameWorld world;
    private readonly Passability passability;
    private readonly TargetDistances targets;
    private readonly GridPoint[] routeWaypoints = new GridPoint[RouteSearch.MaxObstacles + EndWaypoints];
    private UnitRouteMover? unitMover;
    private RouteSearch? unitSearch;
    private AnimalRouteMover? animalMover;
    private RouteSearch? animalSearch;

    /// <summary>Plans routes in <paramref name="world"/>.</summary>
    public RouteFinder(GameWorld world, Passability passability, TargetDistances targets)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>
    /// Plans a new route for <paramref name="unit"/> from its cell to its destination and makes it
    /// the route it walks (<see cref="Unit.PathLength"/>).
    /// </summary>
    public RouteResult RequestRoute(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.PathBlock > BlockedCountKept)
        {
            unit.PathBlock = 0;
        }
        RouteResult route = FindRoute(unit, new GridPoint(unit.X, unit.Y), new GridPoint(unit.DestinationX, unit.DestinationY));
        unit.PathLength = route.Length;
        return route;
    }

    /// <summary>
    /// Plans <paramref name="unit"/>'s route from <paramref name="from"/> to <paramref name="to"/>
    /// segment by segment (stopping after a segment that could not get round an obstacle) and
    /// returns its length; the waypoints are stored in the unit's path.
    /// </summary>
    public RouteResult FindRoute(Unit unit, GridPoint from, GridPoint to)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RouteSearch search = UnitSearch(unit);
        int range = ActionRange(unit);
        GridPoint[] waypoints = routeWaypoints;
        int segments = search.PlanWaypoints(from, WithinReach(from, to), range, waypoints);
        int length = 0;
        for (int i = 0; i < segments; i++)
        {
            SegmentResult segment = search.PlanSegment(waypoints[i], waypoints[i + 1], length, range);
            length = Wrap.Signed8(length + segment.Length);
            if (!segment.Completed)
            {
                break;
            }
        }
        if (world.Mission == MissionNumbers.War && length > ShortRouteLength)
        {
            length = ShortRouteLength;
        }
        return new RouteResult(length, search.Surrounded);
    }

    /// <summary>
    /// The waypoints <paramref name="unit"/>'s route from <paramref name="from"/> to <paramref name="to"/>
    /// is planned through; returns the number of segments (waypoints written: that many + 1).
    /// </summary>
    public int PlanWaypoints(Unit unit, GridPoint from, GridPoint to, GridPoint[] waypoints)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RouteSearch search = UnitSearch(unit);
        return search.PlanWaypoints(from, WithinReach(from, to), ActionRange(unit), waypoints);
    }

    /// <summary>
    /// Plans one segment of <paramref name="unit"/>'s route, stored from waypoint
    /// <paramref name="offset"/> on; the unit then starts the route standing.
    /// </summary>
    public SegmentResult PlanSegment(Unit unit, GridPoint from, GridPoint to, int offset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RouteSearch search = UnitSearch(unit);
        return search.PlanSegment(from, WithinReach(from, to), offset, ActionRange(unit));
    }

    /// <summary>
    /// Plans <paramref name="animal"/>'s short route from its cell to its destination into its path
    /// and returns its length (the caller makes it the animal's route). <paramref name="range"/> is
    /// how close to the destination is close enough when an obstacle cannot be passed.
    /// </summary>
    public RouteResult PlanAnimalRoute(Animal animal, int range)
    {
        ArgumentNullException.ThrowIfNull(animal);
        RouteSearch search = AnimalSearch(animal);
        SegmentResult segment = search.PlanSegment(new GridPoint(animal.X, animal.Y),
            new GridPoint(animal.DestinationX, animal.DestinationY), 0, range);
        return new RouteResult(segment.Length, search.Surrounded);
    }

    /// <summary>How near (in cells) <paramref name="unit"/> must come to its goal for its action: the action's range.</summary>
    public int ActionRange(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ValueSource range = world.Content.Rules.Actions.Actions[unit.Action].Range;
        if (range.Constant is { } constant)
        {
            return constant;
        }
        return range.Property is { } property
            ? EntityProperties.Get(world, property, unit.Id)
            : throw new InvalidOperationException($"action {unit.Action} has no range");
    }

    /// <summary><paramref name="to"/>, or the point on the way to it at <see cref="PlanningReach"/> when it is farther.</summary>
    public static GridPoint WithinReach(GridPoint from, GridPoint to)
    {
        long dx = to.X - from.X, dy = to.Y - from.Y;
        long far = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return far <= PlanningReach ? to : new GridPoint(from.X + (int)(dx * PlanningReach / far), from.Y + (int)(dy * PlanningReach / far));
    }

    private RouteSearch NewSearch(RouteMover mover) =>
        new(mover, world.Content.Rules.Movement, world.Map.Bounds);

    /// <summary>The planner for <paramref name="unit"/>: one search used again for every unit (each plan starts it over).</summary>
    private RouteSearch UnitSearch(Unit unit)
    {
        if (unitMover is null)
        {
            unitMover = new UnitRouteMover(unit, passability, targets);
            unitSearch = NewSearch(unitMover);
        }
        unitMover.MoveFor(unit);
        unitSearch!.Restart(world.Map.Bounds);
        return unitSearch;
    }

    /// <summary>The planner for <paramref name="animal"/> (see <see cref="UnitSearch"/>).</summary>
    private RouteSearch AnimalSearch(Animal animal)
    {
        if (animalMover is null)
        {
            animalMover = new AnimalRouteMover(animal, passability, targets);
            animalSearch = NewSearch(animalMover);
        }
        animalMover.MoveFor(animal);
        animalSearch!.Restart(world.Map.Bounds);
        return animalSearch;
    }
}
