using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace Tribes.Net.Transport.Lan;

/// <summary>Settings of a <see cref="LanTransport"/>.</summary>
public sealed record LanTransportOptions
{
    /// <summary>Address the host listens on; <see cref="IPAddress.Any"/> for every network.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>TCP port the host listens on; 0 picks a free one (see <see cref="LanTransport.LocalPort"/>).</summary>
    public int Port { get; init; } = NetProtocol.DefaultLanPort;

    /// <summary>Whether the host answers room queries from <see cref="LanRoomBrowser"/>.</summary>
    public bool EnableDiscovery { get; init; } = true;

    /// <summary>Address the host's discovery socket listens on.</summary>
    public IPAddress DiscoveryBindAddress { get; init; } = IPAddress.Any;

    /// <summary>UDP port of room queries; must match the browsers'.</summary>
    public int DiscoveryPort { get; init; } = NetProtocol.DefaultDiscoveryPort;

    /// <summary>Port a client uses when the address has none.</summary>
    public int DefaultConnectPort { get; init; } = NetProtocol.DefaultLanPort;

    /// <summary>A connection must be made and introduced within this time.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Interval of keep-alive pings, which also measure the round-trip time.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A connection that receives nothing for this long is closed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest message; larger frames close the connection. At least <see cref="NetProtocol.MaxMessageBytes"/>, the size every session message fits in.</summary>
    public int MaxMessageBytes { get; init; } = NetProtocol.MaxMessageBytes;

    internal string? FindProblem()
    {
        if (Port is < 0 or > IPEndPoint.MaxPort || DiscoveryPort is < 1 or > IPEndPoint.MaxPort
            || DefaultConnectPort is < 1 or > IPEndPoint.MaxPort)
        {
            return "Ports must be within 0..65535 (discovery and connect ports at least 1).";
        }

        if (PingInterval <= TimeSpan.Zero || Timeout <= PingInterval || ConnectTimeout <= TimeSpan.Zero)
        {
            return "PingInterval and ConnectTimeout must be positive and Timeout longer than PingInterval.";
        }

        return MaxMessageBytes < NetProtocol.MaxMessageBytes ? $"MaxMessageBytes must be at least {NetProtocol.MaxMessageBytes}." : null;
    }
}

/// <summary>Settings of a <see cref="LanRoomBrowser"/>.</summary>
public sealed record LanDiscoveryOptions
{
    /// <summary>UDP port hosts answer queries on.</summary>
    public int Port { get; init; } = NetProtocol.DefaultDiscoveryPort;

    /// <summary>Address the browser's socket binds to.</summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    /// <summary>Whether to broadcast queries to every local network.</summary>
    public bool UseBroadcast { get; init; } = true;

    /// <summary>More addresses to query directly, such as 127.0.0.1 or a known host.</summary>
    public IReadOnlyList<IPAddress> ExtraTargets { get; init; } = [];

    /// <summary>Interval between queries.</summary>
    public TimeSpan QueryInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>A room not heard from for this long leaves the list.</summary>
    public TimeSpan RoomTimeout { get; init; } = TimeSpan.FromSeconds(4);
}

/// <summary>Parses "host", "host:port", "ip:port" and "[ipv6]:port".</summary>
internal static class LanAddress
{
    /// <summary>An endpoint for display; IPv4 addresses seen through a dual-stack socket lose their IPv6 form.</summary>
    public static string Format(EndPoint? endpoint, string fallback)
    {
        if (endpoint is not IPEndPoint ip)
        {
            return endpoint?.ToString() ?? fallback;
        }

        IPAddress address = ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address;
        return new IPEndPoint(address, ip.Port).ToString();
    }

    public static bool TryParse(string address, int defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort;
        string text = address.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (IPEndPoint.TryParse(text, out IPEndPoint? endpoint))
        {
            host = endpoint.Address.ToString();
            port = endpoint.Port == 0 ? defaultPort : endpoint.Port;
            return true;
        }

        int colon = text.LastIndexOf(':');
        if (colon < 0)
        {
            host = text;
            return Uri.CheckHostName(host) != UriHostNameType.Unknown;
        }

        if (text.IndexOf(':', StringComparison.Ordinal) != colon
            || !int.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > IPEndPoint.MaxPort)
        {
            return false;
        }

        host = text[..colon];
        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}
