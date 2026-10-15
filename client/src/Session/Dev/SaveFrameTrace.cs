using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using Tribes.Session;

namespace Tribes.Client.Session.Dev;

/// <summary>
/// Development aid ("--session-save-trace"): logs the frame times around every save of the match,
/// <see cref="Before"/> frames before the save began to <see cref="After"/> frames after, as
/// <c>SESSION_FRAMES</c> lines (milliseconds per frame; the frame that took the save is marked with "*").
/// </summary>
public partial class SaveFrameTrace : Node
{
    /// <summary>Frames logged before a save.</summary>
    public const int Before = 20;

    /// <summary>Frames logged after a save.</summary>
    public const int After = 150;

    /// <summary>Frames right after the save that count as "near" it (the save itself, the picture two frames later).</summary>
    public const int NearFrames = 4;

    /// <summary>Frames per hash-cost summary.</summary>
    public const int HashWindow = 600;

    private readonly double[] ring = new double[Before];
    private readonly IMatchRunner? runner;
    private readonly StringBuilder line = new();
    private long last;
    private int count;
    private int remaining = -1;
    private double worst;
    private bool wasSaving;
    private double near;
    private double beforeWorst;
    private int over20;
    private int lastHashes;
    private long lastTick;
    private int tickFrames;
    private double tickSum;
    private double tickWorst;
    private int hashFrames;
    private int otherFrames;
    private double hashFrameSum;
    private double hashFrameWorst;
    private double otherSum;
    private double otherWorst;
    private double hashSum;
    private double hashWorst;

    /// <summary>Needed by Godot.</summary>
    public SaveFrameTrace()
    {
    }

    /// <summary>Traces the saves of <paramref name="runner"/>.</summary>
    public SaveFrameTrace(IMatchRunner runner)
    {
        this.runner = runner;
    }

    /// <summary>Sorts the frame by whether a lockstep state hash was taken in it; prints a summary every few seconds.</summary>
    private void CountHashFrame(double ms)
    {
        if (runner is not NetworkMatchRunner network)
        {
            return;
        }
        int hashes = network.Lockstep.HashCount;
        long tick = network.Simulation.Tick;
        bool ticked = tick != lastTick;
        lastTick = tick;
        if (hashes == lastHashes && ticked)
        {
            // A frame that ran ticks but took no hash: the fair comparison for hash frames.
            tickFrames++;
            tickSum += ms;
            tickWorst = System.Math.Max(tickWorst, ms);
        }
        if (hashes != lastHashes)
        {
            lastHashes = hashes;
            hashFrames++;
            hashFrameSum += ms;
            hashFrameWorst = System.Math.Max(hashFrameWorst, ms);
            hashSum += network.Lockstep.LastHashMilliseconds;
            hashWorst = System.Math.Max(hashWorst, network.Lockstep.LastHashMilliseconds);
        }
        else
        {
            otherFrames++;
            otherSum += ms;
            otherWorst = System.Math.Max(otherWorst, ms);
        }
        if (hashFrames + otherFrames >= HashWindow)
        {
            GD.Print(FormattableString.Invariant(
                $"SESSION_HASH_FRAMES hash frames={hashFrames} mean={hashFrameSum / System.Math.Max(1, hashFrames):0.0}ms worst={hashFrameWorst:0.0}ms | tick frames without hash={tickFrames} mean={tickSum / System.Math.Max(1, tickFrames):0.0}ms worst={tickWorst:0.0}ms | other frames={otherFrames} mean={otherSum / System.Math.Max(1, otherFrames):0.0}ms worst={otherWorst:0.0}ms | hash itself mean={hashSum / System.Math.Max(1, hashFrames):0.00}ms worst={hashWorst:0.00}ms tick={runner.Simulation.Tick}"));
            hashFrames = otherFrames = tickFrames = 0;
            hashFrameSum = hashFrameWorst = otherSum = otherWorst = hashSum = hashWorst = tickSum = tickWorst = 0;
        }
    }

    /// <summary>Marks the frame where a save began (call it right before starting one).</summary>
    public void Mark()
    {
        if (remaining >= 0)
        {
            return;
        }
        line.Clear().Append("SESSION_FRAMES before=");
        for (int i = 0; i < Before; i++)
        {
            double ms = ring[(count + i) % Before];
            line.Append(ms.ToString("0.0", CultureInfo.InvariantCulture)).Append(' ');
        }
        beforeWorst = 0;
        for (int i = 0; i < Before; i++)
        {
            beforeWorst = System.Math.Max(beforeWorst, ring[i]);
        }
        near = 0;
        line.Append("| *");
        remaining = After;
        worst = 0;
        over20 = 0;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        // A save was taken during the last frame: that frame is the one marked.
        bool saving = runner is { PendingSaves: > 0 };
        if (saving && !wasSaving)
        {
            Mark();
        }
        wasSaving = saving;
        if (last != 0)
        {
            double ms = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds;
            CountHashFrame(ms);
            ring[count++ % Before] = ms;
            if (remaining > 0)
            {
                line.Append(ms.ToString("0.0", CultureInfo.InvariantCulture)).Append(' ');
                worst = System.Math.Max(worst, ms);
                if (After - remaining < NearFrames)
                {
                    near = System.Math.Max(near, ms);
                }
                over20 += ms > 20 ? 1 : 0;
                if (--remaining == 0)
                {
                    GD.Print(line.ToString());
                    GD.Print($"SESSION_FRAMES_SUMMARY near={near:0.0}ms (worst of the save frame and the next {NearFrames - 1}) after={After} worst={worst:0.0}ms over20ms={over20} beforeWorst={beforeWorst:0.0}ms tick={runner?.Simulation.Tick}");
                    remaining = -1;
                }
            }
        }
        last = now;
    }
}
