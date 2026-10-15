using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Tribes.Net.Protocol;

namespace Tribes.Net;

/// <summary>
/// Feeds tick batches to a simulation strictly in order, one batch per tick. Batches must arrive
/// without gaps; the simulation steps only when it has the batch for its next tick.
/// </summary>
internal sealed class LockstepEngine
{
    private readonly Queue<TickBatch> queue = new();

    public LockstepEngine(ILockstepSimulation simulation)
    {
        Simulation = simulation;
        NextExpectedTick = simulation.Tick;
    }

    public ILockstepSimulation Simulation { get; }

    /// <summary>The tick of the next batch to accept.</summary>
    public long NextExpectedTick { get; private set; }

    /// <summary>The newest batch received, or the tick before the simulation's when none is waiting.</summary>
    public long LatestTick => NextExpectedTick - 1;

    public int Buffered => queue.Count;

    public static bool IsHashTick(long tick, int interval) => interval > 0 && tick % interval == 0;

    public void Enqueue(TickBatch batch)
    {
        if (batch.Tick < NextExpectedTick)
        {
            return;
        }

        if (batch.Tick > NextExpectedTick)
        {
            throw new ProtocolException($"Expected the batch of tick {NextExpectedTick} but received tick {batch.Tick}.");
        }

        queue.Enqueue(batch);
        NextExpectedTick++;
    }

    public bool TryStep([NotNullWhen(true)] out TickBatch? batch)
    {
        if (!queue.TryDequeue(out batch))
        {
            return false;
        }

        long before = Simulation.Tick;
        if (before != batch.Tick)
        {
            throw new InvalidOperationException($"The simulation is at tick {before} but the next batch is for tick {batch.Tick}.");
        }

        Simulation.Step(batch.Commands);
        if (Simulation.Tick != before + 1)
        {
            throw new InvalidOperationException($"A step must advance the simulation by one tick; from {before} it went to {Simulation.Tick}.");
        }

        return true;
    }

    /// <summary>Drops the batches not yet run, to receive them again from the simulation's tick.</summary>
    public void ClearBuffered()
    {
        queue.Clear();
        NextExpectedTick = Simulation.Tick;
    }
}

/// <summary>Turns elapsed time into a number of ticks due at the current game speed.</summary>
internal sealed class TickPacer
{
    private readonly long tickTicks;
    private TimeSpan last;
    private TimeSpan accumulated;
    private bool running;

    public TickPacer(int tickMilliseconds)
    {
        tickTicks = TimeSpan.FromMilliseconds(tickMilliseconds).Ticks;
        Interval = TimeSpan.FromTicks(tickTicks);
    }

    /// <summary>Real time per tick at the current speed.</summary>
    public TimeSpan Interval { get; private set; }

    public void SetSpeed(int speedPercent) => Interval = TimeSpan.FromTicks(tickTicks * 100 / speedPercent);

    public void Reset(TimeSpan now)
    {
        last = now;
        accumulated = TimeSpan.Zero;
        running = true;
    }

    /// <summary>The ticks due since the last call, at most <paramref name="maxSteps"/>; time beyond that is dropped.</summary>
    public int Advance(TimeSpan now, int maxSteps)
    {
        if (!running)
        {
            Reset(now);
            return 0;
        }

        accumulated += now - last;
        last = now;
        long due = accumulated.Ticks / Interval.Ticks;
        accumulated -= TimeSpan.FromTicks(due * Interval.Ticks);
        return (int)Math.Min(due, maxSteps);
    }

    /// <summary>Gives back one tick that was due but could not run, so it runs as soon as possible.</summary>
    public void CreditOne()
    {
        accumulated += Interval;
        if (accumulated > Interval)
        {
            accumulated = Interval;
        }
    }
}

/// <summary>
/// A client's input delay: enough ticks for a command to reach the host before the host closes
/// the batch it is meant for. Grows at once when the round trip grows, shrinks one tick at a time.
/// </summary>
internal sealed class InputDelayController
{
    private static readonly TimeSpan ShrinkHold = TimeSpan.FromSeconds(2);
    private readonly int minimum;
    private readonly int maximum;
    private TimeSpan lastChange;

    public InputDelayController(int minimum, int maximum)
    {
        this.minimum = minimum;
        this.maximum = maximum;
        Current = minimum;
    }

    public int Current { get; private set; }

    public static int Compute(TimeSpan roundTrip, TimeSpan variance, TimeSpan tickInterval, int minimum, int maximum)
    {
        // In doubles, and capped before the conversion, so no round trip can overflow.
        double ticks = (roundTrip.Ticks + (2.0 * variance.Ticks)) / tickInterval.Ticks;
        return (int)Math.Clamp(Math.Ceiling(ticks), minimum, maximum);
    }

    public void Update(TimeSpan now, TimeSpan roundTrip, TimeSpan variance, TimeSpan tickInterval)
    {
        int wanted = Compute(roundTrip, variance, tickInterval, minimum, maximum);
        if (wanted > Current)
        {
            Current = wanted;
            lastChange = now;
        }
        else if (wanted < Current && now - lastChange >= ShrinkHold)
        {
            Current--;
            lastChange = now;
        }
    }
}

internal enum HashCheck
{
    Unknown,
    Match,
    Mismatch,
}

/// <summary>The host's own state hashes of recent checked ticks.</summary>
internal sealed class HashLedger
{
    private readonly Dictionary<long, uint> hashes = [];
    private readonly Queue<long> order = new();
    private readonly int capacity;

    public HashLedger(int capacity)
    {
        this.capacity = capacity;
    }

    public void Record(long tick, uint hash)
    {
        if (hashes.TryAdd(tick, hash))
        {
            order.Enqueue(tick);
        }

        while (order.Count > capacity)
        {
            hashes.Remove(order.Dequeue());
        }
    }

    /// <summary>The hashes kept, oldest first.</summary>
    public IEnumerable<(long Tick, uint Hash)> Entries => order.Select(tick => (tick, hashes[tick]));

    public HashCheck Check(long tick, uint hash, out uint expected)
    {
        if (!hashes.TryGetValue(tick, out expected))
        {
            return HashCheck.Unknown;
        }

        return expected == hash ? HashCheck.Match : HashCheck.Mismatch;
    }
}

/// <summary>The host's encoded batches of recent ticks, for players who reconnect quickly.</summary>
internal sealed class BatchHistory
{
    private readonly List<(long Tick, byte[] Message)> batches = [];
    private readonly int capacity;

    public BatchHistory(int capacity)
    {
        this.capacity = capacity;
    }

    /// <summary>The batches kept, oldest first.</summary>
    public IReadOnlyList<(long Tick, byte[] Message)> Entries => batches;

    /// <summary>Keeps a batch. One received again replaces the batch of that tick and everything after it.</summary>
    public void Add(long tick, byte[] message)
    {
        if (capacity == 0)
        {
            return;
        }

        if (batches.Count > 0 && batches[^1].Tick >= tick)
        {
            batches.RemoveAll(b => b.Tick >= tick);
        }

        batches.Add((tick, message));
        if (batches.Count > capacity)
        {
            batches.RemoveRange(0, batches.Count - capacity);
        }
    }

    public void Clear() => batches.Clear();

    /// <summary>Forgets the batches of <paramref name="tick"/> and later (they will be received again).</summary>
    public void DropFrom(long tick) => batches.RemoveAll(b => b.Tick >= tick);

    /// <summary>The batches of ticks <paramref name="from"/> to <paramref name="toExclusive"/> - 1, or null when some are gone.</summary>
    public List<byte[]>? Range(long from, long toExclusive)
    {
        var result = new List<byte[]>();
        if (from == toExclusive)
        {
            return result;
        }

        if (batches.Count == 0 || from < batches[0].Tick)
        {
            return null;
        }

        foreach ((long tick, byte[] message) in batches)
        {
            if (tick >= from && tick < toExclusive)
            {
                result.Add(message);
            }
        }

        return result.Count == toExclusive - from ? result : null;
    }
}
