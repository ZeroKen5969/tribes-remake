using System;
using Godot;

namespace Tribes.Client.Platform;

/// <summary>
/// The Wi-Fi multicast lock that LAN game discovery holds while it listens: without it Android
/// drops multicast and broadcast packets meant for other hosts. Calls
/// <c>com.prehistoric.tribes.platform.MulticastLockHelper</c>. Other platforms need no lock, so
/// there every call succeeds without doing anything.
/// </summary>
public static class AndroidMulticast
{
    private const string JavaClassName = "com.prehistoric.tribes.platform.MulticastLockHelper";

    /// <summary>Takes the lock (repeated calls keep one lock). Returns false only when Android refused it.</summary>
    public static bool Acquire() => !AndroidJava.IsAndroid || Call("acquire", withActivity: true);

    /// <summary>Gives the lock back. Returns false only when Android failed to release it.</summary>
    public static bool Release() => !AndroidJava.IsAndroid || Call("release", withActivity: false);

    /// <summary>Whether the lock is held now (always false on platforms without a lock).</summary>
    public static bool IsHeld => AndroidJava.IsAndroid && Call("isHeld", withActivity: false);

    private static bool Call(string method, bool withActivity)
    {
        try
        {
            Variant result = withActivity
                ? AndroidJava.CallWithActivity(JavaClassName, method)
                : AndroidJava.CallStatic(JavaClassName, method);
            return AndroidJava.ToBool(result, method);
        }
        catch (InvalidOperationException e)
        {
            GD.PushWarning($"Multicast lock: {e.Message}");
            return false;
        }
    }
}
