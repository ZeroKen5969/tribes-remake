using System;
using System.Collections.Generic;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Keeps the land of an endless world in one piece. Every chunk has walkable connectors in the
/// middle of its four sides, at the same places on every chunk, so the connectors of two
/// neighbouring chunks touch; inside each chunk all connectors are joined by walkable cells
/// (walking from cell to cell up, down, left or right). Where the design leaves a connector cut off,
/// a path of plain ground is carved to it. So every walkable cell joined to a connector can reach
/// every other such cell of the land made, and the land beyond.
/// </summary>
internal sealed class ChunkConnectivity
{
    /// <summary>The first cell of a connector along its side.</summary>
    public const int ConnectorStart = 28;

    /// <summary>Cells of a connector.</summary>
    public const int ConnectorLength = 8;

    /// <summary>
    /// Cells along each side of the plain ground square in every corner: where four chunks meet,
    /// two neighbours made apart from each other touch the same cells, so their corners are alike.
    /// </summary>
    public const int CornerSize = 1;

    private const int Cells = ChunkCanvas.Size * ChunkCanvas.Size;
    private const int Last = ChunkCanvas.Size - 1;
    private const int NotReached = -1;

    /// <summary>The cost of carving through a thicket, rock or resource tile, and through water or a cliff.</summary>
    private const int ClearCost = 1, FillCost = 12;

    private readonly TerrainModel model;
    private readonly int[] component = new int[Cells];
    private readonly int[] cost = new int[Cells];
    private readonly int[] previous = new int[Cells];

    /// <summary>Uses the walkable tiles of <paramref name="model"/>.</summary>
    public ChunkConnectivity(TerrainModel model) => this.model = model;

    /// <summary>The cells of the four connectors (top, bottom, left, right side), in that order.</summary>
    public static IEnumerable<(int X, int Y)> ConnectorCells()
    {
        for (int i = ConnectorStart; i < ConnectorStart + ConnectorLength; i++)
        {
            yield return (i, 0);
        }
        for (int i = ConnectorStart; i < ConnectorStart + ConnectorLength; i++)
        {
            yield return (i, Last);
        }
        for (int i = ConnectorStart; i < ConnectorStart + ConnectorLength; i++)
        {
            yield return (0, i);
        }
        for (int i = ConnectorStart; i < ConnectorStart + ConnectorLength; i++)
        {
            yield return (Last, i);
        }
    }

    /// <summary>Locks every connector cell and the corner cells as plain ground.</summary>
    public void LockConnectors(ChunkCanvas canvas)
    {
        foreach ((int x, int y) in ConnectorCells())
        {
            canvas.Lock(x, y, model.Ground);
        }
        for (int y = 0; y < CornerSize; y++)
        {
            for (int x = 0; x < CornerSize; x++)
            {
                canvas.Lock(x, y, model.Ground);
                canvas.Lock(Last - x, y, model.Ground);
                canvas.Lock(x, Last - y, model.Ground);
                canvas.Lock(Last - x, Last - y, model.Ground);
            }
        }
    }

    /// <summary>Whether every connector cell is in the walkable area of the first one.</summary>
    public bool Connected(ChunkCanvas canvas)
    {
        Label(canvas);
        int main = MainComponent();
        foreach ((int x, int y) in ConnectorCells())
        {
            if (component[(y * ChunkCanvas.Size) + x] != main)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Carves locked paths of plain ground from the first connector's walkable area to every
    /// connector cell outside it (the cheapest path: the fewest cells that are not walkable; it
    /// carves no cell on the chunk's edge).
    /// </summary>
    public void Carve(ChunkCanvas canvas)
    {
        foreach ((int x, int y) in ConnectorCells())
        {
            Label(canvas);
            int main = MainComponent();
            int target = (y * ChunkCanvas.Size) + x;
            if (component[target] != main)
            {
                CarveTo(canvas, main, target);
            }
        }
    }

    private int MainComponent()
    {
        (int x, int y) = (ConnectorStart, 0);
        return component[(y * ChunkCanvas.Size) + x];
    }

    /// <summary>Numbers the walkable areas of the chunk (non-walkable cells get <see cref="NotReached"/>).</summary>
    private void Label(ChunkCanvas canvas)
    {
        Array.Fill(component, NotReached);
        var queue = new Queue<int>();
        int next = 0;
        for (int start = 0; start < Cells; start++)
        {
            if (component[start] != NotReached || !Walkable(canvas, start))
            {
                continue;
            }
            component[start] = next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (int neighbour in Neighbours(cell))
                {
                    if (component[neighbour] == NotReached && Walkable(canvas, neighbour))
                    {
                        component[neighbour] = next;
                        queue.Enqueue(neighbour);
                    }
                }
            }
            next++;
        }
    }

    /// <summary>
    /// The cheapest path from area <paramref name="main"/> (never empty: the first connector cell is
    /// locked ground) to <paramref name="target"/>, kept walkable from then on (the seams around it may
    /// still choose its tiles among walkable ones). Walking costs nothing, clearing
    /// a thicket, rock or resource tile costs <see cref="ClearCost"/>, filling water or a cliff
    /// <see cref="FillCost"/>; the chunk's edge is never carved. Cells of equal cost are taken in
    /// the order they were reached, so the path is the same on every device.
    /// </summary>
    private void CarveTo(ChunkCanvas canvas, int main, int target)
    {
        Array.Fill(cost, int.MaxValue);
        Array.Fill(previous, NotReached);
        var buckets = new List<Queue<int>>();
        for (int cell = 0; cell < Cells; cell++)
        {
            if (component[cell] == main)
            {
                cost[cell] = 0;
                Bucket(buckets, 0).Enqueue(cell);
            }
        }
        for (int level = 0; level < buckets.Count && cost[target] == int.MaxValue; level++)
        {
            Queue<int> bucket = buckets[level];
            while (bucket.Count > 0)
            {
                int cell = bucket.Dequeue();
                if (cost[cell] != level)
                {
                    continue;
                }
                foreach (int neighbour in Neighbours(cell))
                {
                    int step = StepCost(canvas, neighbour);
                    if (step >= 0 && level + step < cost[neighbour])
                    {
                        cost[neighbour] = level + step;
                        previous[neighbour] = cell;
                        Bucket(buckets, level + step).Enqueue(neighbour);
                    }
                }
            }
        }
        for (int cell = target; cell != NotReached; cell = previous[cell])
        {
            int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
            canvas.KeepWalkable(x, y, Walkable(canvas, cell) ? canvas[x, y] : model.Ground);
        }
    }

    /// <summary>What it costs to make <paramref name="cell"/> part of the path; negative when it may not be.</summary>
    private int StepCost(ChunkCanvas canvas, int cell)
    {
        if (Walkable(canvas, cell))
        {
            return 0;
        }
        if (OnEdge(cell))
        {
            // A carved cell on the edge would sit beside the neighbouring chunk, which never changes.
            return -1;
        }
        int tile = canvas[cell % ChunkCanvas.Size, cell / ChunkCanvas.Size];
        return tile >= 0 && model.Clearable[tile] ? ClearCost : FillCost;
    }

    private static Queue<int> Bucket(List<Queue<int>> buckets, int level)
    {
        while (buckets.Count <= level)
        {
            buckets.Add(new Queue<int>());
        }
        return buckets[level];
    }

    private static bool OnEdge(int cell)
    {
        int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
        return x == 0 || y == 0 || x == Last || y == Last;
    }

    private bool Walkable(ChunkCanvas canvas, int cell)
    {
        int tile = canvas[cell % ChunkCanvas.Size, cell / ChunkCanvas.Size];
        return tile >= 0 && model.Walkable[tile];
    }

    private static IEnumerable<int> Neighbours(int cell)
    {
        int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
        if (x > 0)
        {
            yield return cell - 1;
        }
        if (x < Last)
        {
            yield return cell + 1;
        }
        if (y > 0)
        {
            yield return cell - ChunkCanvas.Size;
        }
        if (y < Last)
        {
            yield return cell + ChunkCanvas.Size;
        }
    }
}
