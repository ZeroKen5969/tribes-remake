using System;
using System.Collections.Generic;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Gives the free cells of a chunk design tiles so that every two neighbouring cells are a pair the
/// original maps have (wave function collapse on the learnt neighbour rules): the cell with the
/// fewest tiles left takes one, drawn by how often the original maps put it beside the neighbours
/// it has, and the rules are passed on until every cell has one tile. When the rules leave a cell
/// without any tile, the last choices are taken back (each one not made again) up to a budget.
/// </summary>
internal sealed class TileSolver
{
    private const int Cells = ChunkCanvas.Size * ChunkCanvas.Size;

    /// <summary>Choices taken back at most before a solve gives up.</summary>
    private const int BacktrackBudget = 4000;

    private readonly TerrainModel model;
    private readonly PairCounts pairs;
    private readonly TileSet[] domains = new TileSet[Cells];
    private readonly int[] counts = new int[Cells];
    private readonly TileSet[] unions = new TileSet[Cells * TerrainModel.Directions];
    private readonly bool[] unionKnown = new bool[Cells * TerrainModel.Directions];
    private readonly bool[] queued = new bool[Cells];
    private readonly Queue<int> queue = new();
    private readonly List<int> open = new();
    private readonly List<(int Cell, TileSet Domain)> trail = new();
    private readonly Stack<(int Cell, int Tile, int Mark)> choices = new();

    /// <summary>Solves with the rules of <paramref name="model"/>.</summary>
    public TileSolver(TerrainModel model)
    {
        this.model = model;
        pairs = PairCounts.For(model);
    }

    /// <summary>
    /// Gives every cell marked in <paramref name="free"/> (index y * 64 + x) a tile; the other cells
    /// and the ring keep theirs. False, with the free cells unset, when the cells cannot be filled.
    /// </summary>
    public bool Solve(ChunkCanvas canvas, bool[] free, GameRandom random)
    {
        open.Clear();
        queue.Clear();
        trail.Clear();
        choices.Clear();
        for (int i = 0; i < Cells; i++)
        {
            if (free[i])
            {
                open.Add(i);
                SetDomain(i, canvas.MustStayWalkable(i % ChunkCanvas.Size, i / ChunkCanvas.Size) ? model.SeenWalkable : model.Seen);
                canvas[i % ChunkCanvas.Size, i / ChunkCanvas.Size] = ChunkCanvas.Unset;
                Enqueue(i);
            }
        }
        if (!Propagate(canvas, free))
        {
            return Fail(canvas);
        }
        trail.Clear();
        int budget = BacktrackBudget;
        while (Pick(random) is int cell)
        {
            int tile = Draw(canvas, cell, random);
            choices.Push((cell, tile, trail.Count));
            SetDomain(cell, TileSet.Of(tile));
            EnqueueNeighbours(cell, free);
            if (!Propagate(canvas, free) && !Backtrack(canvas, free, ref budget))
            {
                return Fail(canvas);
            }
        }
        foreach (int i in open)
        {
            canvas[i % ChunkCanvas.Size, i / ChunkCanvas.Size] = domains[i].First();
        }
        return true;
    }

    private bool Fail(ChunkCanvas canvas)
    {
        foreach (int i in open)
        {
            canvas[i % ChunkCanvas.Size, i / ChunkCanvas.Size] = ChunkCanvas.Unset;
            queued[i] = false;
        }
        queue.Clear();
        return false;
    }

    /// <summary>
    /// Takes back the last choice (and what followed from it) and rules its tile out of its cell;
    /// further back while that leaves no way on. False when no choice is left or the budget is spent.
    /// </summary>
    private bool Backtrack(ChunkCanvas canvas, bool[] free, ref int budget)
    {
        while (choices.Count > 0 && budget-- > 0)
        {
            (int cell, int tile, int mark) = choices.Pop();
            Undo(mark);
            TileSet left = Without(domains[cell], tile);
            if (left.IsEmpty)
            {
                continue;
            }
            SetDomain(cell, left);
            Enqueue(cell);
            EnqueueNeighbours(cell, free);
            if (Propagate(canvas, free))
            {
                return true;
            }
        }
        return false;
    }

    private void Undo(int mark)
    {
        while (queue.Count > 0)
        {
            queued[queue.Dequeue()] = false;
        }
        for (int i = trail.Count - 1; i >= mark; i--)
        {
            (int cell, TileSet domain) = trail[i];
            Restore(cell, domain);
        }
        trail.RemoveRange(mark, trail.Count - mark);
    }

    private static TileSet Without(TileSet set, int tile)
    {
        TileSet left = default;
        for (int t = set.First(); t >= 0; t = set.Next(t))
        {
            if (t != tile)
            {
                left.Add(t);
            }
        }
        return left;
    }

    private void SetDomain(int cell, TileSet domain)
    {
        trail.Add((cell, domains[cell]));
        Restore(cell, domain);
    }

    private void Restore(int cell, TileSet domain)
    {
        domains[cell] = domain;
        counts[cell] = domain.Count;
        for (int d = 0; d < TerrainModel.Directions; d++)
        {
            unionKnown[(cell * TerrainModel.Directions) + d] = false;
        }
    }

    /// <summary>The tiles that may stand in <paramref name="direction"/> of free cell <paramref name="cell"/> (kept until its domain changes).</summary>
    private TileSet AllowedNextTo(int cell, int direction)
    {
        int key = (cell * TerrainModel.Directions) + direction;
        if (!unionKnown[key])
        {
            unions[key] = model.AllowedNextTo(domains[cell], direction);
            unionKnown[key] = true;
        }
        return unions[key];
    }

    /// <summary>The undecided cell with the fewest tiles left (the first such from a random start), or null when all are decided.</summary>
    private int? Pick(GameRandom random)
    {
        int best = -1, fewest = int.MaxValue;
        int start = open.Count == 0 ? 0 : random.NextMasked(int.MaxValue, open.Count);
        for (int k = 0; k < open.Count; k++)
        {
            int i = open[(start + k) % open.Count];
            int count = counts[i];
            if (count > 1 && count < fewest)
            {
                (best, fewest) = (i, count);
            }
        }
        return best < 0 ? null : best;
    }

    /// <summary>A tile of the cell's domain, drawn by how often the original maps pair it with the cell's decided neighbours.</summary>
    private int Draw(ChunkCanvas canvas, int cell, GameRandom random)
    {
        int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
        Span<int> neighbours = stackalloc int[TerrainModel.Directions];
        bool anyNeighbour = false;
        for (int d = 0; d < TerrainModel.Directions; d++)
        {
            neighbours[d] = Decided(canvas, x + TerrainModel.StepX(d), y + TerrainModel.StepY(d));
            anyNeighbour |= neighbours[d] >= 0;
        }
        TileSet domain = domains[cell];
        Span<long> weights = stackalloc long[TileSet.Capacity];
        long total = 0;
        for (int t = domain.First(); t >= 0; t = domain.Next(t))
        {
            long weight = anyNeighbour ? 1 : model.Weights[t] + 1L;
            for (int d = 0; d < TerrainModel.Directions && anyNeighbour; d++)
            {
                weight += neighbours[d] >= 0 ? pairs.Count(neighbours[d], TerrainModel.Opposite(d), t) : 0;
            }
            weights[t] = weight;
            total += weight;
        }
        long roll = (long)((((ulong)(uint)random.NextInt() << 31) | (uint)random.NextInt()) % (ulong)total);
        for (int t = domain.First(); t >= 0; t = domain.Next(t))
        {
            roll -= weights[t];
            if (roll < 0)
            {
                return t;
            }
        }
        return domain.First();
    }

    /// <summary>The tile a canvas cell has or has been given, or a negative value.</summary>
    private int Decided(ChunkCanvas canvas, int x, int y)
    {
        if (!ChunkCanvas.OnCanvas(x, y))
        {
            return ChunkCanvas.Open;
        }
        int tile = canvas[x, y];
        if (tile == ChunkCanvas.Unset && ChunkCanvas.InChunk(x, y))
        {
            int cell = (y * ChunkCanvas.Size) + x;
            return counts[cell] == 1 ? domains[cell].First() : ChunkCanvas.Unset;
        }
        return tile;
    }

    private bool Propagate(ChunkCanvas canvas, bool[] free)
    {
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            queued[cell] = false;
            TileSet revised = Revise(canvas, cell, free);
            if (revised.IsEmpty)
            {
                return false;
            }
            if (!revised.SameAs(domains[cell]))
            {
                SetDomain(cell, revised);
                EnqueueNeighbours(cell, free);
            }
        }
        return true;
    }

    /// <summary>The cell's domain kept to the tiles that fit every neighbour.</summary>
    private TileSet Revise(ChunkCanvas canvas, int cell, bool[] free)
    {
        int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
        TileSet domain = domains[cell];
        for (int d = 0; d < TerrainModel.Directions && !domain.IsEmpty; d++)
        {
            int nx = x + TerrainModel.StepX(d), ny = y + TerrainModel.StepY(d);
            if (!ChunkCanvas.OnCanvas(nx, ny))
            {
                continue;
            }
            int back = TerrainModel.Opposite(d);
            if (ChunkCanvas.InChunk(nx, ny) && free[(ny * ChunkCanvas.Size) + nx])
            {
                domain &= AllowedNextTo((ny * ChunkCanvas.Size) + nx, back);
                continue;
            }
            int tile = canvas[nx, ny];
            if (tile >= 0)
            {
                domain &= model.Allowed(tile, back);
            }
        }
        return domain;
    }

    private void EnqueueNeighbours(int cell, bool[] free)
    {
        int x = cell % ChunkCanvas.Size, y = cell / ChunkCanvas.Size;
        for (int d = 0; d < TerrainModel.Directions; d++)
        {
            int nx = x + TerrainModel.StepX(d), ny = y + TerrainModel.StepY(d);
            if (ChunkCanvas.InChunk(nx, ny) && free[(ny * ChunkCanvas.Size) + nx])
            {
                Enqueue((ny * ChunkCanvas.Size) + nx);
            }
        }
    }

    private void Enqueue(int cell)
    {
        if (!queued[cell])
        {
            queued[cell] = true;
            queue.Enqueue(cell);
        }
    }
}
