using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Search;

/// <summary>A cell a spiral search visits.</summary>
/// <param name="Cell">The cell.</param>
/// <param name="Dx">Its column offset from the centre.</param>
/// <param name="Dy">Its row offset from the centre.</param>
/// <param name="Code">The cell code of what fills it (<see cref="CellCode"/>).</param>
/// <param name="Measured">10 * distance + facing from the centre towards it (<see cref="GridDistances.Measure"/>).</param>
internal readonly record struct SpiralCell(GridPoint Cell, int Dx, int Dy, int Code, int Measured)
{
    /// <summary>Its distance from the centre in cells (the movement table's estimate).</summary>
    public int Distance => GridDistances.Cells(Measured);

    /// <summary>The facing that leads from the centre away from this cell.</summary>
    public int AwayFacing => Facings.Opposite(GridDistances.Facing(Measured));
}

/// <summary>The eight facings: 0 east, then clockwise.</summary>
internal static class Facings
{
    /// <summary>Number of facings.</summary>
    public const int Count = 8;

    /// <summary>The facing opposite <paramref name="facing"/>.</summary>
    public static int Opposite(int facing) => (facing + (Count / 2)) & (Count - 1);
}

/// <summary>
/// Looks around a cell ring by ring, the way every "find something nearby" rule of the game does:
/// a square spiral starting east of the centre and turning clockwise (east, south, west, north
/// with growing legs), skipping cells outside the part of the map in play. The centre itself is
/// never visited. The order matters: the first cell that fits wins.
/// </summary>
internal sealed class SpiralSearch
{
    private readonly GameWorld world;
    private readonly GridDistances distances;

    /// <summary>Searches the map of <paramref name="world"/>.</summary>
    public SpiralSearch(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
    }

    /// <summary>
    /// Visits the cells around <paramref name="centre"/> in spiral order until
    /// <paramref name="visit"/> returns true; returns whether it did. Round k of the spiral walks k
    /// cells along the row, then k along the column, alternately forwards (east, south) and
    /// backwards (west, north); <paramref name="rounds"/> rounds reach about rounds / 2 cells away.
    /// </summary>
    public bool Visit(GridPoint centre, int rounds, Func<SpiralCell, bool> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        int dx = 0, dy = 0, sign = 1;
        for (int round = 1; round <= rounds; round++, sign = -sign)
        {
            for (int step = 0; step < round; step++)
            {
                dx += sign;
                if (VisitCell(centre, dx, dy, visit))
                {
                    return true;
                }
            }
            for (int step = 0; step < round; step++)
            {
                dy += sign;
                if (VisitCell(centre, dx, dy, visit))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The first empty cell around <paramref name="centre"/> within <paramref name="rounds"/> spiral rounds, or null.</summary>
    public GridPoint? FindEmptyCell(GridPoint centre, int rounds)
    {
        GridPoint? found = null;
        Visit(centre, rounds, cell =>
        {
            if (cell.Code != CellCode.Empty)
            {
                return false;
            }
            found = cell.Cell;
            return true;
        });
        return found;
    }

    private bool VisitCell(GridPoint centre, int dx, int dy, Func<SpiralCell, bool> visit)
    {
        var cell = new GridPoint(centre.X + dx, centre.Y + dy);
        if (!world.Map.InPlay(cell))
        {
            return false;
        }
        return visit(new SpiralCell(cell, dx, dy, CellCode.At(world.Map, cell), distances.Measure(dx, dy)));
    }
}
