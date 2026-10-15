using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Paths;

/// <summary>
/// A list of cells used while planning a route: one of the straight lines towards the goal, the
/// trace around an obstacle, or the route put together from them.
/// </summary>
internal sealed class TracePath
{
    /// <summary>Cells a list holds: the longest straight line across the largest map.</summary>
    public const int Size = 96;

    private readonly int[] x = new int[Size];
    private readonly int[] y = new int[Size];
    private int length;

    /// <summary>Cells in the list.</summary>
    public int Length { get => length; set => length = Wrap.Signed8(value); }

    /// <summary>For a straight line: how many of its cells are blocked.</summary>
    public int BlockedCells { get; set; }

    /// <summary>For a trace that got round: the obstacle whose far side it reached.</summary>
    public int RejoinedObstacle { get; set; }

    /// <summary>Cell <paramref name="index"/>.</summary>
    public GridPoint this[int index] => new(x[index], y[index]);

    /// <summary>Becomes as a new path is: no cells, every cell (0, 0), nothing blocked or rejoined.</summary>
    public void Clear()
    {
        System.Array.Clear(x);
        System.Array.Clear(y);
        length = 0;
        BlockedCells = 0;
        RejoinedObstacle = 0;
    }

    /// <summary>Sets cell <paramref name="index"/>.</summary>
    public void Set(int index, GridPoint cell)
    {
        x[index] = cell.X;
        y[index] = cell.Y;
    }

    /// <summary>Appends <paramref name="cell"/>.</summary>
    public void Add(GridPoint cell)
    {
        Set(Length, cell);
        Length++;
    }
}

/// <summary>A stretch of blocked cells on the straight line towards the goal.</summary>
internal sealed class Obstacle
{
    /// <summary>Creates an obstacle entered from <paramref name="entry"/>, the line's cell <paramref name="entryIndex"/> (-1 the start).</summary>
    public Obstacle(GridPoint entry, int entryIndex)
    {
        Entry = entry;
        EntryIndex = entryIndex;
    }

    /// <summary>The last free cell before the obstacle.</summary>
    public GridPoint Entry { get; private set; }

    /// <summary>Becomes a new obstacle entered from <paramref name="entry"/> (as the constructor makes one).</summary>
    public void Restart(GridPoint entry, int entryIndex)
    {
        Entry = entry;
        EntryIndex = entryIndex;
        HasExit = false;
        Exit = default;
        ExitIndex = 0;
    }

    /// <summary>Index of <see cref="Entry"/> on the route being built (-1: the start).</summary>
    public int EntryIndex { get; private set; }

    /// <summary>Whether the line leaves the obstacle again (it does not when the goal is inside it).</summary>
    public bool HasExit { get; private set; }

    /// <summary>The first free cell after the obstacle.</summary>
    public GridPoint Exit { get; private set; }

    /// <summary>Index of <see cref="Exit"/> on the route being built.</summary>
    public int ExitIndex { get; private set; }

    /// <summary>Records where the line leaves the obstacle.</summary>
    public void SetExit(GridPoint exit, int exitIndex)
    {
        HasExit = true;
        Exit = exit;
        ExitIndex = exitIndex;
    }

    /// <summary>Moves the obstacle along the route by <paramref name="cells"/> after the route before it changed length.</summary>
    public void Shift(int cells)
    {
        EntryIndex = Wrap.Signed8(EntryIndex + cells);
        ExitIndex = Wrap.Signed8(ExitIndex + cells);
    }
}

/// <summary>A route segment that was stored.</summary>
/// <param name="Length">Waypoints stored.</param>
/// <param name="Completed">Whether it got round every obstacle on the way.</param>
internal readonly record struct SegmentResult(int Length, bool Completed);

/// <summary>A unit's route request.</summary>
/// <param name="Length">The route's length (waypoints).</param>
/// <param name="Surrounded">Whether the unit turned out to be boxed in by obstacles while tracing round one.</param>
internal readonly record struct RouteResult(int Length, bool Surrounded);
