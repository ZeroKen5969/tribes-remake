using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Paths;

/// <summary>
/// One route search. Three straight lines lead from the start to the goal; the one crossing the
/// fewest blocked cells is followed, and each obstacle on it is traced round on both sides (the
/// wall on the left, then on the right) until the trace reaches the far side of this or a later
/// obstacle. The shorter successful trace is spliced into the route. When neither side gets round,
/// the route ends at the traced cell nearest to the goal.
/// </summary>
internal sealed partial class RouteSearch
{
    /// <summary>Most obstacles one straight line is split into.</summary>
    public const int MaxObstacles = 10;

    /// <summary>Most steps a trace round one obstacle takes.</summary>
    private const int MaxTraceSteps = 80;

    /// <summary>The interleaved line takes a diagonal step whenever this much straight progress is owed.</summary>
    private const int Pacing = 50;

    /// <summary>Pace of the interleaved line when it has no diagonal steps.</summary>
    private const int NoDiagonalPace = 1000000;

    /// <summary>The facings.</summary>
    private const int Facings = 8, FacingMask = 7;

    /// <summary>The routes the three straight lines and the traces are built in, by role.</summary>
    private const int MainRole = 0, LeftRole = 1, RightRole = 2, Roles = 3;

    private readonly RouteMover mover;
    private readonly MovementTables movement;
    private GridRect playable;
    private readonly TracePath[] paths = { new(), new(), new() };
    private readonly int[] role = new int[Roles];
    private readonly bool[] gotRound = new bool[2];
    private readonly Obstacle[] obstacles = new Obstacle[MaxObstacles];
    private int obstacleCount;

    /// <summary>Plans for <paramref name="mover"/> on a map whose part in play is <paramref name="playable"/>.</summary>
    public RouteSearch(RouteMover mover, MovementTables movement, GridRect playable)
    {
        this.mover = mover ?? throw new ArgumentNullException(nameof(mover));
        this.movement = movement ?? throw new ArgumentNullException(nameof(movement));
        this.playable = playable;
    }

    /// <summary>Whether a trace found the mover boxed in (all eight neighbours blocked).</summary>
    public bool Surrounded { get; private set; }

    /// <summary>Starts over as a new search for the same mover would, on a map whose part in play is now <paramref name="area"/>.</summary>
    public void Restart(GridRect area)
    {
        playable = area;
        Surrounded = false;
        foreach (TracePath path in paths)
        {
            path.Clear();
        }
        Array.Clear(role);
        Array.Clear(gotRound);
        obstacleCount = 0;
    }

    /// <summary>
    /// Plans from <paramref name="start"/> to <paramref name="goal"/> and stores the route from
    /// waypoint <paramref name="offset"/> on, keeping within the mover's capacity.
    /// <paramref name="range"/> is how close to the goal is close enough when an obstacle cannot be passed.
    /// </summary>
    public SegmentResult PlanSegment(GridPoint start, GridPoint goal, int offset, int range)
    {
        int limit = mover.Capacity;
        // Only an endless world has goals beyond the planner's lists: they are planned towards in steps.
        goal = RouteFinder.WithinReach(start, goal);
        FindObstacles(start, goal);
        bool completed = true;
        for (int current = 0; current < obstacleCount; current++)
        {
            TraceAround(current);
            if (!gotRound[0] && !gotRound[1])
            {
                EndAtApproach(current, range, limit);
                completed = false;
                break;
            }
            current = Splice(current, true, limit);
            if (paths[role[MainRole]].Length + offset >= limit)
            {
                break;
            }
        }
        TracePath route = paths[role[MainRole]];
        if (route.Length + offset >= limit)
        {
            route.Length = limit - offset;
        }
        for (int i = 0; i < route.Length; i++)
        {
            mover.SetWaypoint(i + offset, route[i]);
        }
        mover.RouteStored();
        return new SegmentResult(route.Length, completed);
    }

    /// <summary>
    /// The waypoints a long route is split into: the start, the middle of the trace round each
    /// obstacle, and the goal (or, when an obstacle cannot be passed, the cell nearest to the goal).
    /// Returns the number of segments; <paramref name="waypoints"/> receives that many + 1 cells.
    /// </summary>
    public int PlanWaypoints(GridPoint start, GridPoint goal, int range, GridPoint[] waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        waypoints[0] = start;
        int count = 1;
        goal = RouteFinder.WithinReach(start, goal);
        FindObstacles(start, goal);
        for (int current = 0; current < obstacleCount; current++)
        {
            TraceAround(current);
            if (!gotRound[0] && !gotRound[1])
            {
                int best = FindApproach(current, range);
                waypoints[count] = best < 0 ? obstacles[current].Entry : SideOf(best)[best >> 1];
                return count;
            }
            TracePath chosen = paths[role[(!gotRound[0] || paths[role[LeftRole]].Length >= paths[role[RightRole]].Length) && gotRound[1] ? RightRole : LeftRole]];
            waypoints[count] = chosen[chosen.Length / 2];
            current = chosen.RejoinedObstacle;
            count++;
        }
        waypoints[count] = goal;
        return count;
    }

    /// <summary>Ends the route at the traced cell nearest to the goal, or at the obstacle's entry.</summary>
    private void EndAtApproach(int current, int range, int limit)
    {
        int best = FindApproach(current, range);
        if (best < 0)
        {
            paths[role[MainRole]].Length = obstacles[current].EntryIndex + 1;
            return;
        }
        SideOf(best).Length = (best >> 1) + 1;
        gotRound[best & 1] = true;
        Splice(current, false, limit);
        gotRound[best & 1] = false;
    }

    /// <summary>The trace a candidate number refers to: even numbers the left trace, odd the right.</summary>
    private TracePath SideOf(int candidate) => paths[role[(candidate & 1) + LeftRole]];

    /// <summary>
    /// The candidate (2 * index + side) of the traced cell nearest to the goal, alternating between
    /// the two traces; -1 when the obstacle's entry is close enough already or nothing is nearer.
    /// Stops at the first cell within <paramref name="range"/>.
    /// </summary>
    private int FindApproach(int current, int range)
    {
        GridPoint entry = obstacles[current].Entry;
        GridPoint goal = mover.Goal(entry).Goal;
        int best = GridMath.SquaredDistance(goal.X, goal.Y, entry.X, entry.Y);
        int bestCandidate = -1;
        // A squared distance against a range in cells, as in the original.
        if (best <= range)
        {
            return bestCandidate;
        }
        TracePath left = paths[role[LeftRole]], right = paths[role[RightRole]];
        int candidates = (left.Length > right.Length ? left.Length : right.Length) * 2;
        for (int candidate = 0; candidate < candidates; candidate++)
        {
            TracePath side = (candidate & 1) == 0 ? left : right;
            if (side.Length <= candidate >> 1)
            {
                continue;
            }
            GridPoint cell = side[candidate >> 1];
            int distance = GridMath.SquaredDistance(goal.X, goal.Y, cell.X, cell.Y);
            if (distance < best)
            {
                best = distance;
                bestCandidate = candidate;
                GoalDistance seen = mover.Goal(cell);
                goal = seen.Goal;
                if (seen.Cells <= range)
                {
                    return bestCandidate;
                }
            }
        }
        return bestCandidate;
    }

    /// <summary>The obstacle (number + 1) whose far side is <paramref name="cell"/>, or 0.</summary>
    private int ExitMarkAt(GridPoint cell)
    {
        for (int i = 0; i < obstacleCount; i++)
        {
            if (obstacles[i].HasExit && obstacles[i].Exit == cell)
            {
                return i + 1;
            }
        }
        return 0;
    }
}
