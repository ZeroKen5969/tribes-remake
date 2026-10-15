using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// Chunks made off the frame: a chunk asked for is designed on a worker thread (one design after
/// another, in the order asked) from the ring it had when it was asked for, and joins the world on
/// its due frame (<see cref="LandOrder"/>); if its design is not done by then the frame waits for
/// it. What a chunk becomes depends only on the world's state when it was asked for, so every peer,
/// and a world loaded from a save made in between, gets the same land on the same frame.
/// </summary>
internal sealed class LandWorks
{
    private readonly GameWorld world;
    private readonly TerrainGenerator designer;
    private readonly Dictionary<ChunkCoord, Job> jobs = new();
    private Task last = Task.CompletedTask;

    /// <summary>Makes chunks of <paramref name="world"/> off the frame.</summary>
    public LandWorks(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        designer = new TerrainGenerator(world.Content);
    }

    private EndlessState State => world.Endless!;

    /// <summary>Whether <paramref name="chunk"/> is being made.</summary>
    public bool IsOrdered(ChunkCoord chunk)
    {
        Resume();
        return jobs.ContainsKey(chunk);
    }

    /// <summary>Whether any chunk is being made.</summary>
    public bool Busy => State.LandOrders.Count > 0;

    /// <summary>
    /// Asks for <paramref name="chunk"/> (not made nor asked for yet): it joins the world
    /// <c>leadFrames</c> from now, and at least one frame after the chunk asked for before it.
    /// </summary>
    public void Order(ChunkCoord chunk, bool byPeople)
    {
        Resume();
        var ring = new int[ChunkCanvas.Ring.Count];
        for (int i = 0; i < ring.Length; i++)
        {
            (int x, int y) = ChunkCanvas.Ring[i];
            var cell = new GridPoint(chunk.Origin.X + x, chunk.Origin.Y + y);
            ring[i] = world.Map.Contains(cell) ? designer.RingTile(world.Map, chunk, x, y)
                : jobs.ContainsKey(ChunkCoord.Of(cell)) ? LandOrder.FromOrder
                : LandOrder.Open;
        }
        IReadOnlyList<LandOrder> orders = State.LandOrders;
        int due = world.Frame + world.Content.Endless!.Land.LeadFrames;
        if (orders.Count > 0)
        {
            due = Math.Max(due, orders[^1].DueFrame + 1);
        }
        var order = new LandOrder(chunk, due, byPeople, ring);
        State.Order(order);
        Start(order);
    }

    /// <summary>The first chunk being made when it is due by now (it must then <see cref="Join"/>), or null.</summary>
    public LandOrder? Due()
    {
        IReadOnlyList<LandOrder> orders = State.LandOrders;
        return orders.Count > 0 && orders[0].DueFrame <= world.Frame ? orders[0] : null;
    }

    /// <summary>The first chunk being made joins the world now (waiting for its design if need be); returns its order.</summary>
    /// <exception cref="InvalidOperationException">No chunk is being made.</exception>
    public LandOrder Join(TerrainGenerator builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Resume();
        if (State.LandOrders.Count == 0)
        {
            throw new InvalidOperationException("no chunk is being made");
        }
        LandOrder order = State.LandOrders[0];
        Job job = jobs[order.Coord];
        job.Done.GetAwaiter().GetResult();
        builder.Build(world.Map, order.Coord, job.Canvas, job.Random);
        State.RemoveFirstOrder();
        jobs.Remove(order.Coord);
        foreach (LandOrder later in State.LandOrders)
        {
            TakeRingFrom(later, order.Coord, job.Canvas);
        }
        return order;
    }

    /// <summary>After a load: the chunks the saved world was making are designed again (the same way).</summary>
    private void Resume()
    {
        if (jobs.Count == State.LandOrders.Count)
        {
            return;
        }
        jobs.Clear();
        last = Task.CompletedTask;
        foreach (LandOrder order in State.LandOrders)
        {
            Start(order);
        }
    }

    private void Start(LandOrder order)
    {
        var sources = new Dictionary<ChunkCoord, Job>();
        for (int i = 0; i < order.Ring.Length; i++)
        {
            if (order.Ring[i] == LandOrder.FromOrder)
            {
                ChunkCoord from = ChunkCoord.Of(RingCell(order.Coord, i));
                sources[from] = jobs.TryGetValue(from, out Job? source)
                    ? source
                    : throw new System.IO.InvalidDataException($"land order {order.Coord}: no chunk {from} is being made");
            }
        }
        var job = new Job(order.Coord, (int[])order.Ring.Clone(), sources, TerrainGenerator.RandomOf(order.Coord, State.Seed));
        job.Done = last.ContinueWith(_ => Design(job), TaskScheduler.Default);
        last = job.Done;
        jobs[order.Coord] = job;
    }

    /// <summary>The design of a job (on the worker; the jobs it takes ring cells from are designed already).</summary>
    private void Design(Job job)
    {
        for (int i = 0; i < job.Ring.Length; i++)
        {
            (int x, int y) = ChunkCanvas.Ring[i];
            int tile = job.Ring[i];
            if (tile == LandOrder.FromOrder)
            {
                GridPoint cell = RingCell(job.Coord, i);
                Job source = job.Sources[ChunkCoord.Of(cell)];
                tile = source.Canvas[cell.X - source.Coord.Origin.X, cell.Y - source.Coord.Origin.Y];
            }
            job.Canvas[x, y] = tile;
        }
        designer.Design(job.Canvas, job.Random);
    }

    /// <summary>The ring cells of <paramref name="order"/> in chunk <paramref name="made"/> (just joined) take its design.</summary>
    private static void TakeRingFrom(LandOrder order, ChunkCoord made, ChunkCanvas design)
    {
        for (int i = 0; i < order.Ring.Length; i++)
        {
            if (order.Ring[i] != LandOrder.FromOrder)
            {
                continue;
            }
            GridPoint cell = RingCell(order.Coord, i);
            if (ChunkCoord.Of(cell) == made)
            {
                order.Ring[i] = design[cell.X - made.Origin.X, cell.Y - made.Origin.Y];
            }
        }
    }

    private static GridPoint RingCell(ChunkCoord chunk, int index)
    {
        (int x, int y) = ChunkCanvas.Ring[index];
        return new GridPoint(chunk.Origin.X + x, chunk.Origin.Y + y);
    }

    /// <summary>One chunk's design under way.</summary>
    private sealed class Job
    {
        public Job(ChunkCoord coord, int[] ring, Dictionary<ChunkCoord, Job> sources, GameRandom random)
        {
            Coord = coord;
            Ring = ring;
            Sources = sources;
            Random = random;
        }

        public ChunkCoord Coord { get; }

        /// <summary>The ring as it was asked for (the worker's own copy).</summary>
        public int[] Ring { get; }

        /// <summary>The jobs whose designs fill the ring cells marked <see cref="LandOrder.FromOrder"/>.</summary>
        public Dictionary<ChunkCoord, Job> Sources { get; }

        public GameRandom Random { get; }

        public ChunkCanvas Canvas { get; } = new();

        public Task Done { get; set; } = Task.CompletedTask;
    }
}
