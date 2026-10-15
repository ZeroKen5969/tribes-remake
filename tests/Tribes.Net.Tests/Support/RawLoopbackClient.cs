using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Transport.Loopback;

namespace Tribes.Net.Tests.Support;

/// <summary>A bare loopback connection to the cluster's host that sends and records raw messages.</summary>
internal sealed class RawLoopbackClient : IDisposable
{
    private readonly LoopbackTransport transport;
    private readonly List<byte[]> received = [];
    private bool disconnected;

    public RawLoopbackClient(LoopbackCluster cluster, string name = "raw")
    {
        transport = new LoopbackTransport(cluster.Network, name);
        transport.MessageReceived += (_, e) => received.Add(e.Data.ToArray());
        transport.PeerDisconnected += (_, _) => disconnected = true;
        transport.Connect(LoopbackCluster.HostAddress);
        cluster.RunUntil(
            () =>
            {
                transport.Poll();
                return transport.Connections.Count == 1;
            },
            "the raw connection");
    }

    /// <summary>Messages received so far (polls first).</summary>
    public List<byte[]> Received
    {
        get
        {
            transport.Poll();
            return received;
        }
    }

    /// <summary>The received messages, decoded (polls first).</summary>
    public List<NetMessage> Messages => [.. Received.Select(m => MessageCodec.Decode(m))];

    /// <summary>Whether the host closed the connection (polls first).</summary>
    public bool Disconnected
    {
        get
        {
            transport.Poll();
            return disconnected;
        }
    }

    public void Send(byte[] data) => transport.Connections.Single().Send(data);

    public void Send(NetMessage message) => Send(MessageCodec.Encode(message));

    /// <summary>Introduces itself like a real client of the cluster's versions.</summary>
    public void Hello(Guid id, bool spectate = false) => Send(new HelloMessage(
        NetProtocol.Version, LoopbackCluster.GameVersion, LoopbackCluster.ContentVersion, id, "raw", spectate, -1, Guid.Empty, -1, string.Empty, string.Empty));

    public void Dispose() => transport.Dispose();
}
