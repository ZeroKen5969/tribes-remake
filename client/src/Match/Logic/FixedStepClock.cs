using System;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// A fixed-step clock: real time goes in, whole ticks come out at the game's own rate (scaled by
/// <see cref="Speed"/>), so the rules always step the same amount however fast the screen draws.
/// A long stall (the app was in the background, a slow frame) does not make the game race to
/// catch up: at most <see cref="MaxTicksPerUpdate"/> ticks run per update and the rest is dropped.
/// </summary>
public sealed class FixedStepClock
{
    /// <summary>The original game's frame period: 120 ms.</summary>
    public const double OriginalTickSeconds = 0.12;

    private double speed = 1;
    private double owed;

    /// <summary>A clock ticking every <paramref name="tickSeconds"/> at speed 1.</summary>
    public FixedStepClock(double tickSeconds = OriginalTickSeconds, int maxTicksPerUpdate = 8)
    {
        if (tickSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tickSeconds), tickSeconds, "the tick must last some time");
        }
        if (maxTicksPerUpdate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTicksPerUpdate), maxTicksPerUpdate, "at least one tick per update");
        }
        TickSeconds = tickSeconds;
        MaxTicksPerUpdate = maxTicksPerUpdate;
    }

    /// <summary>Seconds per tick at speed 1.</summary>
    public double TickSeconds { get; }

    /// <summary>Most ticks one update runs.</summary>
    public int MaxTicksPerUpdate { get; }

    /// <summary>How many times faster than the original the game runs (0.25..8).</summary>
    public double Speed
    {
        get => speed;
        set => speed = Math.Clamp(value, 0.25, 8);
    }

    /// <summary>Whether time stands still.</summary>
    public bool Paused { get; set; }

    /// <summary>Real seconds between ticks at the current speed.</summary>
    public double RealTickSeconds => TickSeconds / speed;

    /// <summary>How far the next tick is, 0 (just ticked) .. 1 (due).</summary>
    public double Alpha => Math.Clamp(owed / RealTickSeconds, 0, 1);

    /// <summary>Lets <paramref name="seconds"/> of real time pass; returns the ticks now due.</summary>
    public int Advance(double seconds)
    {
        if (Paused || seconds <= 0 || double.IsNaN(seconds))
        {
            return 0;
        }
        owed += seconds;
        int due = (int)Math.Floor(owed / RealTickSeconds);
        if (due > MaxTicksPerUpdate)
        {
            owed = 0;
            return MaxTicksPerUpdate;
        }
        owed -= due * RealTickSeconds;
        return due;
    }

    /// <summary>Forgets the time owed (after a load or a long pause).</summary>
    public void Reset() => owed = 0;
}
