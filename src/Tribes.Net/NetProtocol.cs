using System;
using System.Diagnostics;

namespace Tribes.Net;

/// <summary>Constants shared by every peer of a network match.</summary>
public static class NetProtocol
{
    /// <summary>
    /// Version of the wire format. A peer with a different version cannot play with this one; the
    /// host rejects it before anything else is exchanged.
    /// </summary>
    public const int Version = 2;

    /// <summary>TCP port a LAN host listens on unless told otherwise.</summary>
    public const int DefaultLanPort = 47801;

    /// <summary>UDP port LAN hosts answer room queries on.</summary>
    public const int DefaultDiscoveryPort = 47800;

    /// <summary>
    /// The largest session message. Every transport must deliver messages up to this size; the
    /// sessions never send larger ones (snapshots and game setup are bounded or cut into pieces).
    /// </summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <summary>Largest game setup a match start can carry (map, seed, options).</summary>
    public const int MaxGameSetupBytes = 256 * 1024;
}

/// <summary>
/// The time source of the network sessions. Sessions read it on every update, so tests can drive
/// a whole match with <see cref="ManualClock"/> instead of waiting in real time.
/// </summary>
public interface IClock
{
    /// <summary>Time since an arbitrary fixed start; never goes backwards.</summary>
    TimeSpan Now { get; }
}

/// <summary>The real monotonic clock.</summary>
public sealed class SystemClock : IClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    /// <summary>A shared instance.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public TimeSpan Now => stopwatch.Elapsed;
}

/// <summary>A clock that only moves when told to, for tests and replays.</summary>
public sealed class ManualClock : IClock
{
    /// <inheritdoc />
    public TimeSpan Now { get; private set; }

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far; must not be negative.</param>
    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        Now += amount;
    }
}

/// <summary>
/// Thrown when bytes received from the network, or a saved match, do not follow the format.
/// Sessions catch it and close the offending connection; it reaches the game only from
/// <see cref="HostSession.ResumeFromSave"/> and <see cref="HostSession.ReadSaveInfo"/>.
/// </summary>
public sealed class ProtocolException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ProtocolException()
        : base("The data does not follow the network format.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong with the data.</param>
    public ProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the error that revealed the problem.</summary>
    /// <param name="message">What was wrong with the data.</param>
    /// <param name="innerException">The underlying error.</param>
    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
