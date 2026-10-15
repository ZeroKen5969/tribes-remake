using System;
using System.Text;

namespace Tribes.Net.Photon;

/// <summary>
/// How <see cref="PhotonTransport"/> and <see cref="PhotonRoomBrowser"/> connect to Photon. The
/// AppId comes from <see cref="FromBuild"/> (embedded at build time) or <see cref="ForAppId"/>; it
/// is never exposed or printed (<see cref="ToString"/> leaves it out).
/// </summary>
public sealed record PhotonSettings
{
    /// <summary>The smallest <see cref="FragmentBytes"/>.</summary>
    public const int MinFragmentBytes = 1024;

    /// <summary>What a fragment adds to its piece of the message (flag and total length).</summary>
    internal const int FragmentHeaderBytes = 16;

    private PhotonSettings(string appId)
    {
        AppId = appId;
        PhotonLog.Conceal(appId);
    }

    /// <summary>
    /// The app version Photon matches rooms by: only clients with the same value see each other's
    /// rooms. It follows the network protocol, not the game version, so rooms of other game
    /// versions still show up (as incompatible).
    /// </summary>
    public static string DefaultAppVersion => "tribes-net-" + NetProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether this build has a Photon AppId embedded.</summary>
    public static bool IsConfigured => EmbeddedPhotonData.AppId().Length > 0;

    /// <summary>The Realtime AppId; internal so that it cannot be printed by accident.</summary>
    internal string AppId { get; }

    /// <summary>Photon's app version; see <see cref="DefaultAppVersion"/>.</summary>
    public string AppVersion { get; init; } = DefaultAppVersion;

    /// <summary>A region code (for example <c>jp</c>, <c>asia</c>, <c>eu</c>), or null for the best region by ping.</summary>
    public string? Region { get; init; }

    /// <summary>Connect with secure WebSockets (TCP port 443) instead of UDP.</summary>
    public bool UseWebSockets { get; init; }

    /// <summary>When UDP cannot connect (blocked by the network), try secure WebSockets on port 443 once.</summary>
    public bool AllowWebSocketFallback { get; init; } = true;

    /// <summary>How long connecting may take: reaching Photon, joining the room and getting an answer from its host.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How often each connection measures its round trip through Photon.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a client goes without any traffic from its host (messages or the transport's
    /// pings, which flow also while a match is paused) before it treats the host as gone, while
    /// its own connection to Photon still works. Photon itself notices a dead client only after
    /// its own, longer timeout. A host whose game stops for longer (for example sent to the
    /// background on a phone) loses the room to the next player. Zero: wait for Photon.
    /// </summary>
    public TimeSpan HostSilenceTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long <see cref="PhotonTransport.TakeOverHost"/> waits for Photon to confirm the new host.</summary>
    public TimeSpan TakeOverTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long shutting down may wait to send what is still queued (a host leaving tells the others).</summary>
    public TimeSpan FlushTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Messages larger than this travel as several Photon events and are put back together on arrival.</summary>
    public int FragmentBytes { get; init; } = 48 * 1024;

    /// <summary>
    /// Most bytes of messages a connection has on the way at once (sent, not yet confirmed by the
    /// receiver); at least twice <see cref="FragmentBytes"/> and a little more (the receiver gives
    /// credit once per fragment's worth, so one fragment can always be waiting for it). Photon's server drops a receiver whose
    /// queue grows near a megabyte, so this stays well below that.
    /// </summary>
    public int SendWindowBytes { get; init; } = 192 * 1024;

    /// <summary>The largest message; at least <see cref="NetProtocol.MaxMessageBytes"/>.</summary>
    public int MaxMessageBytes { get; init; } = NetProtocol.MaxMessageBytes;

    /// <summary>Settings with the AppId embedded in this build, or null when there is none.</summary>
    public static PhotonSettings? FromBuild()
    {
        byte[] data = EmbeddedPhotonData.AppId();
        return data.Length == 0 ? null : new PhotonSettings(AppIdMask.Reveal(data, EmbeddedPhotonData.Mask()));
    }

    /// <summary>Settings for another AppId (tools and tests).</summary>
    /// <param name="appId">A Photon Realtime AppId.</param>
    public static PhotonSettings ForAppId(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return new PhotonSettings(appId.Trim());
    }

    /// <summary>The settings with <paramref name="region"/> (null for the best region).</summary>
    public PhotonSettings WithRegion(string? region) => this with { Region = PhotonRegions.Normalize(region) };

    /// <summary>What is wrong with the settings, or null when they can be used.</summary>
    public string? FindProblem()
    {
        if (Region is { } region && !PhotonRegions.IsRegionCode(region))
        {
            return $"The region \"{region}\" is not a region code.";
        }

        if (string.IsNullOrWhiteSpace(AppVersion))
        {
            return "The app version must not be empty.";
        }

        if (ConnectTimeout <= TimeSpan.Zero || PingInterval <= TimeSpan.Zero || TakeOverTimeout <= TimeSpan.Zero || FlushTimeout < TimeSpan.Zero)
        {
            return "The timeouts and the ping interval must be positive.";
        }

        if (HostSilenceTimeout < TimeSpan.Zero || (HostSilenceTimeout > TimeSpan.Zero && HostSilenceTimeout < 2 * PingInterval))
        {
            return "HostSilenceTimeout must be zero (off) or at least twice the ping interval.";
        }

        if (MaxMessageBytes < NetProtocol.MaxMessageBytes)
        {
            return $"MaxMessageBytes must be at least {NetProtocol.MaxMessageBytes}, the size of the largest session message.";
        }

        if (FragmentBytes < MinFragmentBytes || FragmentBytes > MaxMessageBytes)
        {
            return $"FragmentBytes must be between {MinFragmentBytes} and MaxMessageBytes.";
        }

        return SendWindowBytes < (2 * FragmentBytes) + FragmentHeaderBytes ? $"SendWindowBytes must be at least twice FragmentBytes plus {FragmentHeaderBytes}." : null;
    }

    /// <summary>The settings without the AppId.</summary>
    public override string ToString() =>
        $"PhotonSettings {{ AppVersion = {AppVersion}, Region = {Region ?? "best"}, UseWebSockets = {UseWebSockets}, AllowWebSocketFallback = {AllowWebSocketFallback} }}";
}

/// <summary>
/// The masking of the embedded AppId: XOR with a 32-byte mask and the byte position. The build
/// task in the project file applies the same scheme.
/// </summary>
internal static class AppIdMask
{
    internal static byte[] Apply(byte[] data, byte[] mask)
    {
        if (mask.Length == 0)
        {
            return [];
        }

        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ mask[i % mask.Length] ^ (byte)((i * 0x9D) + 0x3B));
        }

        return result;
    }

    internal static byte[] Conceal(string appId, byte[] mask) => Apply(Encoding.UTF8.GetBytes(appId), mask);

    internal static string Reveal(byte[] data, byte[] mask) => Encoding.UTF8.GetString(Apply(data, mask));
}
