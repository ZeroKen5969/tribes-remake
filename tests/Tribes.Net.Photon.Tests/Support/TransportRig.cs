using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Photon.Link;
using Tribes.Net.Transport;
using Xunit.Sdk;

namespace Tribes.Net.Photon.Tests.Support;

/// <summary>A transport of a <see cref="TransportRig"/> with everything it raised.</summary>
internal sealed class RigPeer
{
    public required string Name { get; init; }

    public required PhotonTransport Transport { get; init; }

    public required List<FakeLink> Links { get; init; }

    public FakeLink Link => Links[^1];

    public List<IConnection> Connected { get; } = [];

    public List<(IConnection Connection, DisconnectReason Reason)> Disconnected { get; } = [];

    public List<(IConnection Connection, byte[] Data)> Messages { get; } = [];

    public int StatusChanges { get; set; }

    public bool Frozen { get; set; }
}

/// <summary>Bare transports on a <see cref="FakePhotonCloud"/>, polled together frame by frame.</summary>
internal sealed class TransportRig : IDisposable
{
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(10);

    public TransportRig()
    {
        Cloud = new FakePhotonCloud(Clock);
    }

    public ManualClock Clock { get; } = new();

    public FakePhotonCloud Cloud { get; }

    public List<RigPeer> Peers { get; } = [];

    public static RoomAdvertisement Advert(string name = "Room", bool inGame = false) => new()
    {
        RoomId = Guid.NewGuid(),
        Name = name,
        HostName = "Host",
        Mode = RoomMode.Versus,
        Players = 1,
        MaxPlayers = 4,
        GameVersion = "1.2.0",
        ContentVersion = "content-7",
        InGame = inGame,
        IsOpen = true,
    };

    public RigPeer Add(string name, int latencyMs = 20, Func<PhotonSettings, PhotonSettings>? configure = null, PhotonSettings? settings = null)
    {
        var links = new List<FakeLink>();
        IPhotonLink Factory()
        {
            var created = (FakeLink)Cloud.CreateLink();
            created.Latency = TimeSpan.FromMilliseconds(latencyMs);
            links.Add(created);
            return created;
        }

        PhotonSettings chosen = settings ?? PhotonSettings.ForAppId(FakePhotonCloud.AppId);
        chosen = configure?.Invoke(chosen) ?? chosen;
        var transport = new PhotonTransport(chosen, Factory, Clock, d => Clock.Advance(d), seed: Peers.Count + 7);
        var peer = new RigPeer { Name = name, Transport = transport, Links = links };
        transport.PeerConnected += (_, e) => peer.Connected.Add(e.Connection);
        transport.PeerDisconnected += (_, e) => peer.Disconnected.Add((e.Connection, e.Reason));
        transport.MessageReceived += (_, e) => peer.Messages.Add((e.Connection, e.Data.ToArray()));
        transport.StatusChanged += (_, _) => peer.StatusChanges++;
        Peers.Add(peer);
        return peer;
    }

    /// <summary>A host whose room exists.</summary>
    public RigPeer Host(string name = "host", int latencyMs = 20, RoomAdvertisement? advert = null)
    {
        RigPeer host = Add(name, latencyMs);
        host.Transport.StartHost(advert ?? Advert());
        RunUntil(() => host.Transport.HostAddress is not null, "the room to exist");
        return host;
    }

    /// <summary>A client connected to <paramref name="host"/>.</summary>
    public RigPeer Client(RigPeer host, string name, int latencyMs = 20)
    {
        RigPeer client = Add(name, latencyMs);
        int before = host.Connected.Count;
        client.Transport.Connect(host.Transport.HostAddress!);
        RunUntil(() => client.Connected.Count == 1 && host.Connected.Count == before + 1, name + " to connect");
        return client;
    }

    public void Step()
    {
        Clock.Advance(Frame);
        foreach (RigPeer peer in Peers.Where(p => !p.Frozen))
        {
            peer.Transport.Poll();
        }
    }

    public void Run(TimeSpan duration)
    {
        for (long frames = duration.Ticks / Frame.Ticks; frames > 0; frames--)
        {
            Step();
        }
    }

    public void RunUntil(Func<bool> condition, string waitingFor, TimeSpan? timeout = null)
    {
        TimeSpan end = Clock.Now + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            if (Clock.Now >= end)
            {
                string states = string.Join("; ", Peers.Select(p => $"{p.Name}: {p.Transport.Role} {p.Transport.Status}/{p.Transport.LastError}"));
                throw new XunitException($"Timed out waiting for {waitingFor}. {states}");
            }

            Step();
        }
    }

    public void Dispose()
    {
        foreach (RigPeer peer in Peers)
        {
            peer.Transport.Dispose();
        }
    }
}
