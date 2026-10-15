using Tribes.Client.Platform;

namespace Tribes.Client.Ui.Net;

/// <summary>
/// Shares Android's Wi-Fi multicast lock between the room browser and a hosted room. The lock
/// itself is not counted (one acquire, one release), so each user takes a lease here and the lock
/// is held while any lease is out. A hosted room handed to the match keeps its lease; whoever
/// closes that session calls <see cref="Release"/>.
/// </summary>
public static class MulticastLease
{
    private static int holders;

    /// <summary>Takes a lease; the first one acquires the lock.</summary>
    public static void Acquire()
    {
        if (holders++ == 0)
        {
            AndroidMulticast.Acquire();
        }
    }

    /// <summary>Gives a lease back; the last one releases the lock.</summary>
    public static void Release()
    {
        if (holders == 0)
        {
            return;
        }
        if (--holders == 0)
        {
            AndroidMulticast.Release();
        }
    }
}
