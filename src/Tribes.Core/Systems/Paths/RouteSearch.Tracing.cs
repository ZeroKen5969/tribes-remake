using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Paths;

/// <summary>Building the straight lines, finding their obstacles, tracing round them and splicing traces in.</summary>
internal sealed partial class RouteSearch
{
    /// <summary>Octant bits of a line: the goal lies to the right, below, or more across than down.</summary>
    private const int OctantRight = 4, OctantDown = 2;

    /// <summary>Row length of the facing-by-delta table (deltas -1..1).</summary>
    private const int DeltaRow = 3;

    private enum StepOutcome
    {
        Moved,
        Stopped,
        Rejoined,
        Surrounded,
    }

    /// <summary>
    /// Builds the three lines from <paramref name="start"/> to <paramref name="goal"/> (straight steps
    /// first, diagonal steps first, and evenly mixed), follows the one with the fewest blocked cells
    /// and records the obstacles on it.
    /// </summary>
    private void FindObstacles(GridPoint start, GridPoint goal)
    {
        BuildLines(start, goal);
        int main = 0;
        for (int i = 1; i < Roles; i++)
        {
            if (paths[i].BlockedCells < paths[main].BlockedCells)
            {
                main = i;
            }
        }
        (role[MainRole], role[LeftRole], role[RightRole]) = (main, (main + 1) % Roles, (main + 2) % Roles);
        obstacleCount = 0;
        TracePath line = paths[main];
        if (line.BlockedCells <= 0)
        {
            return;
        }
        bool blocked = false;
        for (int i = 0; i < line.Length; i++)
        {
            bool before = blocked;
            blocked = mover.Blocks(line[i]);
            if (!before && blocked)
            {
                if (obstacleCount >= MaxObstacles)
                {
                    return;
                }
                GridPoint entry = i == 0 ? start : line[i - 1];
                if (obstacles[obstacleCount] is { } kept)
                {
                    kept.Restart(entry, i - 1);
                }
                else
                {
                    obstacles[obstacleCount] = new Obstacle(entry, i - 1);
                }
                obstacleCount++;
            }
            else if (before && !blocked)
            {
                obstacles[obstacleCount - 1].SetExit(line[i], i);
            }
        }
    }

    private void BuildLines(GridPoint start, GridPoint goal)
    {
        int dx = goal.X - start.X, dy = goal.Y - start.Y;
        int octant = (dx > 0 ? OctantRight : 0) + (dy > 0 ? OctantDown : 0);
        int across = dx < 0 ? -dx : dx, down = dy < 0 ? -dy : dy;
        int straight = down - across, diagonal = across;
        if (across > down)
        {
            octant++;
            (straight, diagonal) = (across - down, down);
        }
        LineStepPattern steps = movement.LineSteps[octant];
        foreach (TracePath path in paths)
        {
            path.Length = 0;
            path.BlockedCells = 0;
        }
        Walk(paths[0], Walk(paths[0], start, steps.Straight, straight), steps.Diagonal, diagonal);
        Walk(paths[1], Walk(paths[1], start, steps.Diagonal, diagonal), steps.Straight, straight);
        WalkMixed(paths[2], start, steps, straight, diagonal);
    }

    private GridPoint Walk(TracePath path, GridPoint from, CellOffset step, int count)
    {
        for (int i = 0; i < count; i++)
        {
            from = new GridPoint(from.X + step.Dx, from.Y + step.Dy);
            Record(path, from);
        }
        return from;
    }

    private void WalkMixed(TracePath path, GridPoint from, LineStepPattern steps, int straight, int diagonal)
    {
        int pace = diagonal > 0 ? straight * Pacing / diagonal : NoDiagonalPace;
        int owed = pace, straightTaken = 0, diagonalTaken = 0;
        for (int i = 0; i < straight + diagonal; i++)
        {
            CellOffset step;
            if ((owed < Pacing || straightTaken >= straight) && diagonalTaken < diagonal)
            {
                step = steps.Diagonal;
                diagonalTaken++;
                owed += pace;
            }
            else
            {
                step = steps.Straight;
                straightTaken++;
                owed -= Pacing;
            }
            from = new GridPoint(from.X + step.Dx, from.Y + step.Dy);
            Record(path, from);
        }
    }

    private void Record(TracePath path, GridPoint cell)
    {
        path.Add(cell);
        if (mover.Blocks(cell))
        {
            path.BlockedCells++;
        }
    }

    /// <summary>Traces round obstacle <paramref name="current"/> with the wall on the left, then on the right.</summary>
    private void TraceAround(int current)
    {
        int rotation = 1;
        for (int side = LeftRole; side <= RightRole; side++)
        {
            TracePath path = paths[role[side]];
            path.Length = 0;
            gotRound[side - LeftRole] = TraceSide(current, path, rotation);
            rotation = -rotation;
        }
    }

    /// <summary>
    /// Follows the obstacle's edge from its entry, turning by <paramref name="rotation"/>, until it
    /// reaches the far side of this or a later obstacle (true), comes back, leaves the map or is boxed in.
    /// </summary>
    private bool TraceSide(int current, TracePath path, int rotation)
    {
        Obstacle obstacle = obstacles[current];
        GridPoint entry = obstacle.Entry;
        GridPoint first = paths[role[MainRole]][obstacle.EntryIndex + 1];
        int facing = movement.FacingByDeltaRowMajor[(first.X - entry.X + 1) + ((first.Y - entry.Y + 1) * DeltaRow)];
        GridPoint at = entry;
        for (int step = 0; step < MaxTraceSteps; step++)
        {
            StepOutcome outcome = TurnAndStep(current, path, rotation, entry, ref at, ref facing);
            if (outcome != StepOutcome.Moved)
            {
                Surrounded |= outcome == StepOutcome.Surrounded;
                return outcome == StepOutcome.Rejoined;
            }
            if (at == entry)
            {
                return false;
            }
            int mark = ExitMarkAt(at);
            if (mark > current)
            {
                path.RejoinedObstacle = mark - 1;
                return true;
            }
            path.Add(at);
            facing = (facing - (2 * rotation)) & FacingMask;
        }
        return false;
    }

    /// <summary>
    /// Turns from <paramref name="facing"/> by <paramref name="rotation"/> until a free cell is ahead
    /// and steps into it. Before a diagonal step the cell beside it is checked for an obstacle's far side.
    /// </summary>
    private StepOutcome TurnAndStep(int current, TracePath path, int rotation, GridPoint entry, ref GridPoint at, ref int facing)
    {
        for (int turn = 0; turn < Facings; turn++)
        {
            DirectionStep ahead = movement.Directions[facing];
            var next = new GridPoint(at.X + ahead.Dx, at.Y + ahead.Dy);
            if (!playable.Contains(next))
            {
                return StepOutcome.Stopped;
            }
            if (!mover.Blocks(next))
            {
                if ((facing & 1) != 0)
                {
                    DirectionStep beside = movement.Directions[(facing + rotation) & FacingMask];
                    var corner = new GridPoint(at.X + beside.Dx, at.Y + beside.Dy);
                    int mark = ExitMarkAt(corner);
                    if (mark > current)
                    {
                        path.RejoinedObstacle = mark - 1;
                        return StepOutcome.Rejoined;
                    }
                    if (corner == entry)
                    {
                        return StepOutcome.Stopped;
                    }
                }
                at = next;
                return StepOutcome.Moved;
            }
            facing = (facing + rotation) & FacingMask;
        }
        return StepOutcome.Surrounded;
    }

    /// <summary>
    /// Replaces the route from obstacle <paramref name="current"/>'s entry by the shorter trace that got
    /// round; with <paramref name="rejoinLine"/> the rest of the old route after the obstacle the trace
    /// reached follows, and the obstacles after it move along. Returns the obstacle to go on after.
    /// The copies take one cell past their end, as the original does; that cell lies beyond the new
    /// route's length and is only ever read as the middle of an empty trace.
    /// </summary>
    private int Splice(int current, bool rejoinLine, int limit)
    {
        if (!gotRound[0] || (paths[role[LeftRole]].Length > paths[role[RightRole]].Length && gotRound[1]))
        {
            (role[LeftRole], role[RightRole]) = (role[RightRole], role[LeftRole]);
        }
        TracePath chosen = paths[role[LeftRole]], spliced = paths[role[RightRole]], old = paths[role[MainRole]];
        int entryIndex = obstacles[current].EntryIndex;
        for (int i = 0; i <= entryIndex; i++)
        {
            spliced.Set(i, old[i]);
        }
        spliced.Length = entryIndex + 1;
        Append(spliced, chosen, 0, chosen.Length, limit);
        (role[MainRole], role[RightRole]) = (role[RightRole], role[MainRole]);
        if (!rejoinLine || spliced.Length >= limit)
        {
            return current;
        }
        int rejoined = chosen.RejoinedObstacle;
        int exitIndex = obstacles[rejoined].ExitIndex;
        int shift = chosen.Length - exitIndex + entryIndex + 1;
        Append(spliced, old, exitIndex, old.Length - exitIndex, limit);
        for (int i = rejoined + 1; i < obstacleCount; i++)
        {
            obstacles[i].Shift(shift);
        }
        return rejoined;
    }

    /// <summary>Appends <paramref name="count"/> cells of <paramref name="source"/> from <paramref name="from"/> (and one more), up to <paramref name="limit"/> in all.</summary>
    private static void Append(TracePath target, TracePath source, int from, int count, int limit)
    {
        int length = target.Length;
        for (int i = 0; i <= count && i + length < limit; i++)
        {
            target.Set(i + length, source[i + from]);
        }
        target.Length = GridMath.Clamp(length + count, 0, limit);
    }
}
