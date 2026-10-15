using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Session;

/// <summary>Why a save was made.</summary>
internal enum SaveReason
{
    /// <summary>The player asked for it.</summary>
    Manual,

    /// <summary>The timed autosave.</summary>
    Autosave,

    /// <summary>Saving before leaving or stopping.</summary>
    Leaving,

    /// <summary>A dry run while the match opens: the file is built but not written, nobody is told.</summary>
    WarmUp,
}

/// <summary>A save whose state is taken and which waits to be written.</summary>
/// <param name="Slot">Where it goes (with its header).</param>
/// <param name="Body">The state, taken at a tick boundary.</param>
/// <param name="Reason">Why it was made.</param>
/// <param name="CaptureMs">How long taking the state took, in milliseconds.</param>
/// <param name="Sequence">Its place among all saves of the runner (1, 2, 3...).</param>
internal sealed record SaveJob(SaveSlot Slot, byte[] Body, SaveReason Reason, double CaptureMs, long Sequence);

/// <summary>What became of a <see cref="SaveJob"/>.</summary>
/// <param name="Job">The save.</param>
/// <param name="Error">Why it failed, or null when it was written.</param>
/// <param name="Bytes">Size of the file written.</param>
/// <param name="AssembleMs">Time to build the file (header, checksum).</param>
/// <param name="WriteMs">Time to write it to the store.</param>
internal sealed record SaveResult(SaveJob Job, Exception? Error, int Bytes, double AssembleMs, double WriteMs);

/// <summary>
/// Writes saves and their pictures on a background thread, one at a time and in the order they
/// were queued, so building the file, encoding the picture and the disk writes never hold up a
/// frame. A picture belongs to one save: it is written only while that save is the newest one of
/// its slot (the store removes a slot's old picture whenever the save is replaced), so a slot never
/// shows the picture of another save. Results wait in a queue until the runner takes them.
/// </summary>
internal sealed class SaveWriter
{
    private readonly ISaveStore store;
    private readonly bool background;
    private readonly ConcurrentQueue<SaveResult> results = new();
    private readonly ConcurrentQueue<string> messages = new();
    private readonly Dictionary<string, long> written = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;
    private int pending;

    public SaveWriter(ISaveStore store, bool background)
    {
        this.store = store;
        this.background = background;
    }

    /// <summary>Saves and pictures queued or being written.</summary>
    public int Pending => Volatile.Read(ref pending);

    /// <summary>Queues <paramref name="job"/> after the work already queued.</summary>
    public void Enqueue(SaveJob job) => Run(() => results.Enqueue(Write(job)));

    /// <summary>
    /// Queues the picture of save <paramref name="save"/>: shrunk by halves while it is at least
    /// twice <paramref name="width"/> wide, encoded as PNG and written next to the save. Skipped when
    /// another save of the slot was written in between; a failure only leaves the slot without picture.
    /// </summary>
    public void EnqueuePicture(SaveJob save, Pixels picture, int width) =>
        Run(() => WritePicture(save, picture, width));

    /// <summary>The next log line of the background work, if any (taken on the runner's thread).</summary>
    public bool TryTakeMessage(out string? message) => messages.TryDequeue(out message);

    /// <summary>The next save result, if any.</summary>
    public bool TryTake(out SaveResult? result) => results.TryDequeue(out result);

    /// <summary>Waits until all queued work is done; false when that took longer than <paramref name="timeout"/>.</summary>
    public bool Wait(TimeSpan timeout)
    {
        Task last;
        lock (gate)
        {
            last = tail;
        }
        return last.Wait(timeout);
    }

    private void Run(Action work)
    {
        Interlocked.Increment(ref pending);
        void Step()
        {
            try
            {
                work();
            }
            finally
            {
                Interlocked.Decrement(ref pending);
            }
        }
        if (!background)
        {
            Step();
            return;
        }
        lock (gate)
        {
            tail = tail.ContinueWith(_ => Step(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private SaveResult Write(SaveJob job)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            byte[] file = MatchSaveFile.Write(job.Slot.Header, job.Body);
            long assembled = Stopwatch.GetTimestamp();
            if (job.Reason != SaveReason.WarmUp)
            {
                store.Write(job.Slot, file);
                written[job.Slot.Stem] = job.Sequence;
            }
            return new SaveResult(job, null, file.Length, Ms(started, assembled), Stopwatch.GetElapsedTime(assembled).TotalMilliseconds);
        }
#pragma warning disable CA1031 // Any failure of a save must reach the players as a notice, not vanish on a worker thread.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // The slot's file is in an unknown state: no picture may claim it.
            written.Remove(job.Slot.Stem);
            return new SaveResult(job, e, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0);
        }
    }

    private void WritePicture(SaveJob save, Pixels picture, int width)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            Pixels small = picture;
            while (small.Width >= width * 2 && small.Height >= 2)
            {
                small = Thumbnails.Halve(small);
            }
            byte[] png = Thumbnails.EncodePng(small);
            long encoded = Stopwatch.GetTimestamp();
            bool current = save.Reason != SaveReason.WarmUp && written.TryGetValue(save.Slot.Stem, out long newest) && newest == save.Sequence;
            if (current)
            {
                store.WritePicture(save.Slot, png);
            }
            messages.Enqueue(FormattableString.Invariant(
                $"SESSION_PICTURE_TIMING {save.Slot.Stem} shrink+encode={Ms(started, encoded):0.00} write={Stopwatch.GetElapsedTime(encoded).TotalMilliseconds:0.00} {(save.Reason == SaveReason.WarmUp ? "dry run" : current ? "written" : "skipped (not the newest save)")} (background)"));
        }
#pragma warning disable CA1031 // A picture that cannot be written leaves the slot without one; the save itself stands.
        catch (Exception e) when (e is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            messages.Enqueue($"SESSION picture of {save.Slot.Stem} not written: {e.Message}");
        }
    }

    private static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
}
