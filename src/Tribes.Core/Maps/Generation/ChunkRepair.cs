using System;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Mends the seams of a chunk design: every pair of neighbouring cells the original maps never have
/// (where two pieces meet, at the edge of a neighbouring chunk, beside a locked cell) is solved
/// again with the cells around it; when that fails the area grows, at last to the whole chunk.
/// Locked cells and the neighbouring chunks never change.
/// </summary>
internal sealed class ChunkRepair
{
    private const int Cells = ChunkCanvas.Size * ChunkCanvas.Size;

    /// <summary>How far around a misfit the cells are solved again, step by step when solving fails.</summary>
    private static readonly int[] Reaches = { 1, 1, 2, 3, 5, 8, 12, 16 };

    private readonly TerrainModel model;
    private readonly TileSolver solver;
    private readonly bool[] misfit = new bool[Cells];
    private readonly bool[] free = new bool[Cells];
    private readonly int[] backup = new int[Cells];

    /// <summary>Mends designs by the rules of <paramref name="model"/>.</summary>
    public ChunkRepair(TerrainModel model)
    {
        this.model = model;
        solver = new TileSolver(model);
    }

    /// <summary>
    /// Mends the chunk; false when even the whole chunk could not be solved (the misfits are then
    /// left as plain ground).
    /// </summary>
    public bool Mend(ChunkCanvas canvas, GameRandom random)
    {
        if (!FindMisfits(canvas))
        {
            return true;
        }
        foreach (int reach in Reaches)
        {
            MarkAround(canvas, reach);
            if (TrySolve(canvas, random) && !FindMisfits(canvas))
            {
                return true;
            }
        }
        MarkWholeChunk(canvas);
        if (TrySolve(canvas, random))
        {
            return true;
        }
        PlainGroundAtMisfits(canvas);
        return false;
    }

    /// <summary>Solves the free cells; when that fails they get their tiles back.</summary>
    private bool TrySolve(ChunkCanvas canvas, GameRandom random)
    {
        for (int i = 0; i < Cells; i++)
        {
            backup[i] = canvas[i % ChunkCanvas.Size, i / ChunkCanvas.Size];
        }
        if (solver.Solve(canvas, free, random))
        {
            return true;
        }
        for (int i = 0; i < Cells; i++)
        {
            canvas[i % ChunkCanvas.Size, i / ChunkCanvas.Size] = backup[i];
        }
        return false;
    }

    /// <summary>Marks the chunk cells of every misfit pair; whether there is any.</summary>
    private bool FindMisfits(ChunkCanvas canvas)
    {
        Array.Clear(misfit);
        bool any = false;
        for (int y = -1; y < ChunkCanvas.Size; y++)
        {
            for (int x = -1; x < ChunkCanvas.Size; x++)
            {
                any |= CheckPair(canvas, x, y, x + 1, y, TerrainModel.Right);
                any |= CheckPair(canvas, x, y, x, y + 1, TerrainModel.Down);
            }
        }
        return any;
    }

    private bool CheckPair(ChunkCanvas canvas, int x, int y, int nx, int ny, int direction)
    {
        if (!ChunkCanvas.OnCanvas(nx, ny) || (!ChunkCanvas.InChunk(x, y) && !ChunkCanvas.InChunk(nx, ny)))
        {
            return false;
        }
        int a = canvas[x, y], b = canvas[nx, ny];
        if (a < 0 || b < 0 || model.Fits(a, direction, b))
        {
            return false;
        }
        Mark(x, y);
        Mark(nx, ny);
        return true;
    }

    private void Mark(int x, int y)
    {
        if (ChunkCanvas.InChunk(x, y))
        {
            misfit[(y * ChunkCanvas.Size) + x] = true;
        }
    }

    /// <summary>Frees the unlocked cells within <paramref name="reach"/> of a misfit.</summary>
    private void MarkAround(ChunkCanvas canvas, int reach)
    {
        Array.Clear(free);
        for (int i = 0; i < Cells; i++)
        {
            if (!misfit[i])
            {
                continue;
            }
            int x = i % ChunkCanvas.Size, y = i / ChunkCanvas.Size;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int fx = x + dx, fy = y + dy;
                    if (ChunkCanvas.InChunk(fx, fy) && !canvas.IsLocked(fx, fy))
                    {
                        free[(fy * ChunkCanvas.Size) + fx] = true;
                    }
                }
            }
        }
    }

    private void MarkWholeChunk(ChunkCanvas canvas)
    {
        for (int i = 0; i < Cells; i++)
        {
            free[i] = !canvas.IsLocked(i % ChunkCanvas.Size, i / ChunkCanvas.Size);
        }
    }

    /// <summary>The last resort: the unlocked cells of the misfits left become plain ground.</summary>
    private void PlainGroundAtMisfits(ChunkCanvas canvas)
    {
        FindMisfits(canvas);
        for (int i = 0; i < Cells; i++)
        {
            int x = i % ChunkCanvas.Size, y = i / ChunkCanvas.Size;
            if (misfit[i] && !canvas.IsLocked(x, y))
            {
                canvas[x, y] = model.Ground;
            }
        }
    }
}
