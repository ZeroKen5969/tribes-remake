using System;

namespace Tribes.Net.Transport;

/// <summary>
/// Smoothed round-trip time and its mean deviation, as TCP estimates them (RFC 6298): each new
/// sample moves the average by an eighth and the deviation by a quarter.
/// </summary>
internal sealed class RttEstimator
{
    private readonly object gate = new();
    private double smoothedTicks;
    private double varianceTicks;
    private bool hasSample;

    public TimeSpan Smoothed
    {
        get
        {
            lock (gate)
            {
                return TimeSpan.FromTicks((long)smoothedTicks);
            }
        }
    }

    public TimeSpan Variance
    {
        get
        {
            lock (gate)
            {
                return TimeSpan.FromTicks((long)varianceTicks);
            }
        }
    }

    /// <summary>Longest believable round trip; longer samples (a forged or garbled ping) are ignored.</summary>
    public static readonly TimeSpan MaxSample = TimeSpan.FromMinutes(1);

    public void AddSample(TimeSpan roundTrip)
    {
        if (roundTrip < TimeSpan.Zero || roundTrip > MaxSample)
        {
            return;
        }

        double sample = roundTrip.Ticks;
        lock (gate)
        {
            if (!hasSample)
            {
                smoothedTicks = sample;
                varianceTicks = sample / 2;
                hasSample = true;
                return;
            }

            varianceTicks = (0.75 * varianceTicks) + (0.25 * Math.Abs(smoothedTicks - sample));
            smoothedTicks = (0.875 * smoothedTicks) + (0.125 * sample);
        }
    }
}

internal enum TransportEventKind
{
    Connected,
    Disconnected,
    Message,
}

/// <summary>An event waiting for the next <see cref="ITransport.Poll"/>.</summary>
internal readonly record struct TransportEvent(
    TransportEventKind Kind,
    IConnection Connection,
    DisconnectReason Reason,
    ReadOnlyMemory<byte> Data);
