using System;

namespace Tribes.Net.Photon;

/// <summary>
/// Diagnostics of the Photon transport and of the Photon SDK (its warnings and errors). Every
/// line has the AppId removed before it gets here, so the game may write it to its log.
/// </summary>
public static class PhotonDiagnostics
{
    /// <summary>Receives the lines; null drops them. May be called from Photon's background thread.</summary>
    public static Action<string>? Log { get; set; }
}

/// <summary>Routes Photon's log through <see cref="PhotonDiagnostics"/> with the AppId removed.</summary>
internal static class PhotonLog
{
    private const int SdkPrefixLength = 8;
    private const string Hidden = "***";
    private static readonly object Gate = new();
    private static string[] secrets = [];
    private static bool installed;

    /// <summary>
    /// Makes sure <paramref name="secret"/> (and the prefix the SDK prints of it) never reaches the
    /// log. Values shorter than that prefix are no AppIds (they would blank out ordinary words).
    /// </summary>
    public static void Conceal(string secret)
    {
        if (secret.Length < SdkPrefixLength)
        {
            return;
        }

        lock (Gate)
        {
            string[] next = [.. secrets, secret, secret[..SdkPrefixLength]];
            Array.Sort(next, (a, b) => b.Length.CompareTo(a.Length));
            secrets = next;
        }
    }

    /// <summary><paramref name="text"/> with every concealed value replaced.</summary>
    public static string Redact(string text)
    {
        foreach (string secret in secrets)
        {
            if (secret.Length > 0)
            {
                text = text.Replace(secret, Hidden, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }

    /// <summary>Writes one diagnostic line.</summary>
    public static void Write(string line)
    {
        Action<string>? sink = PhotonDiagnostics.Log;
        sink?.Invoke(Redact(line));
    }

    /// <summary>Sends the SDK's own log here (once per process); its debug and info lines are dropped.</summary>
    public static void InstallSdkLog()
    {
        lock (Gate)
        {
            if (installed)
            {
                return;
            }

            installed = true;
        }

        global::Photon.Realtime.Log.Init(
            error => Write("PHOTON sdk error: " + error),
            warn => Write("PHOTON sdk warning: " + warn),
            _ => { },
            _ => { },
            (exception, message) => Write("PHOTON sdk exception: " + message + " (" + exception.GetType().Name + ": " + exception.Message + ")"));
    }
}
