using System;
using System.Collections.Generic;
using Tribes.Net.Transport;

namespace Tribes.Net.Photon;

/// <summary>Where a <see cref="PhotonConnection"/> is.</summary>
internal enum ConnectionState
{
    /// <summary>A client's connection waiting for the host's Accept.</summary>
    Pending,

    /// <summary>Messages flow.</summary>
    Open,

    /// <summary>Ended; its disconnect event was raised or is queued.</summary>
    Closed,
}

/// <summary>
/// One logical connection between two actors of a Photon room: the host keeps one per client, a
/// client one to its host. Opened with an Open/Accept handshake, ended with a Close event or when
/// either actor leaves the room.
/// </summary>
internal sealed class PhotonConnection : IConnection
{
    private readonly RttEstimator rtt = new();

    public PhotonConnection(PhotonTransport owner, int id, int nonce, int actor, string remoteAddress, ConnectionState state, int maxMessageBytes)
    {
        Owner = owner;
        Id = id;
        Nonce = nonce;
        Actor = actor;
        RemoteAddress = remoteAddress;
        State = state;
        Assembler = new FragmentAssembler(maxMessageBytes);
    }

    public int Id { get; }

    public string RemoteAddress { get; private set; }

    public bool IsConnected => State == ConnectionState.Open;

    public TimeSpan RoundTripTime => rtt.Smoothed;

    public TimeSpan RoundTripVariance => rtt.Variance;

    /// <summary>The transport this connection belongs to.</summary>
    internal PhotonTransport Owner { get; }

    /// <summary>Whether this side closed it: nothing more is delivered on it.</summary>
    internal bool ClosedByThisSide { get; set; }

    /// <summary>The number both sides know this connection by (from the client's Open).</summary>
    internal int Nonce { get; }

    /// <summary>The other side's actor number; 0 while a client has not picked its host yet.</summary>
    internal int Actor { get; private set; }

    internal ConnectionState State { get; set; }

    internal FragmentAssembler Assembler { get; }

    /// <summary>When this side last pinged the other.</summary>
    internal TimeSpan LastPing { get; set; }

    /// <summary>Events waiting for room in the send window.</summary>
    internal Queue<Outgoing> Outbox { get; } = new();

    /// <summary>Bytes of Data and Fragment events sent on this connection so far.</summary>
    internal long SentBytes { get; set; }

    /// <summary>How many of <see cref="SentBytes"/> the other side said it received.</summary>
    internal long AckedBytes { get; set; }

    /// <summary>Bytes of Data and Fragment events received on this connection so far.</summary>
    internal long ReceivedBytes { get; set; }

    /// <summary>The <see cref="ReceivedBytes"/> last reported back as credit.</summary>
    internal long CreditedBytes { get; set; }

    public void Send(ReadOnlySpan<byte> message) => Owner.SendOn(this, message);

    public void Disconnect() => Owner.CloseLocally(this);

    /// <summary>A client's connection learns which actor hosts the room.</summary>
    internal void BindActor(int actor)
    {
        Actor = actor;
        RemoteAddress = PhotonAddress.Actor(actor);
    }

    internal void AddRttSample(TimeSpan sample) => rtt.AddSample(sample);
}
