using System;
using System.Collections.Generic;
using Tribes.Net.Photon.Link;

namespace Tribes.Net.Photon;

/// <summary>
/// Flow control. Photon's server drops a client whose outgoing queue grows too large (it disconnects
/// it with "SendBufferFull" somewhere below a megabyte), and a snapshot or a big match start would
/// otherwise reach the server all at once. So each connection queues its events and keeps at most
/// <see cref="PhotonSettings.SendWindowBytes"/> of data in flight; the receiver gives credit back
/// (Credit events) as the data arrives.
/// </summary>
public sealed partial class PhotonTransport
{
    /// <summary>Connections this side closed that still send what was queued before the close (ending with the Close event).</summary>
    private readonly List<PhotonConnection> draining = [];

    /// <summary>Queues <paramref name="message"/> on every target connection and sends what the windows allow.</summary>
    private void Enqueue(IReadOnlyList<PhotonConnection> targets, byte[] message)
    {
        foreach ((byte code, byte[] content) in Wire.Split(message, settings.FragmentBytes))
        {
            var item = new Outgoing(code, content, content.Length);
            foreach (PhotonConnection target in targets)
            {
                target.Outbox.Enqueue(item);
            }
        }

        FlushOutboxes();
    }

    /// <summary>Queues the Close of <paramref name="connection"/> behind its data, so the other side gets everything first.</summary>
    private void EnqueueClose(PhotonConnection connection)
    {
        connection.Outbox.Enqueue(new Outgoing(Wire.Close, Wire.EncodeNonce(connection.Nonce), 0));
        if (!draining.Contains(connection))
        {
            draining.Add(connection);
        }

        FlushOutboxes();
    }

    /// <summary>
    /// Sends the head of every outbox whose window has room, as one event for all connections whose
    /// next item is the same (a message sent to many), until no window has room.
    /// </summary>
    private void FlushOutboxes()
    {
        if (link is not { State: LinkState.Room } current)
        {
            return;
        }

        var groups = new Dictionary<Outgoing, List<int>>(ReferenceEqualityComparer.Instance);
        var ready = new List<PhotonConnection>();
        while (true)
        {
            groups.Clear();
            ready.Clear();
            foreach (PhotonConnection connection in Sending())
            {
                if (connection.Outbox.Count == 0 || connection.Actor <= 0)
                {
                    continue;
                }

                Outgoing head = connection.Outbox.Peek();
                long inFlight = connection.SentBytes - connection.AckedBytes;
                if (inFlight > 0 && inFlight + head.Cost > settings.SendWindowBytes)
                {
                    continue;
                }

                if (!groups.TryGetValue(head, out List<int>? actors))
                {
                    groups[head] = actors = [];
                }

                actors.Add(connection.Actor);
                ready.Add(connection);
            }

            if (ready.Count == 0)
            {
                break;
            }

            foreach ((Outgoing item, List<int> actors) in groups)
            {
                current.RaiseEvent(item.Code, item.Content, actors);
            }

            foreach (PhotonConnection connection in ready)
            {
                connection.SentBytes += connection.Outbox.Dequeue().Cost;
            }
        }

        draining.RemoveAll(c => c.Outbox.Count == 0);
    }

    /// <summary>
    /// Sends everything queued, waiting for credit from the other sides, for at most
    /// <see cref="PhotonSettings.FlushTimeout"/> (when this peer leaves).
    /// </summary>
    private void DrainOutboxes()
    {
        TimeSpan end = clock.Now + settings.FlushTimeout;
        FlushOutboxes();
        while (HasQueued() && link is { State: LinkState.Room } current && clock.Now < end)
        {
            wait(PumpStep);
            ServiceLink(current);
            FlushOutboxes();
        }
    }

    private bool HasQueued()
    {
        foreach (PhotonConnection connection in Sending())
        {
            if (connection.Outbox.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The connections that send: the open ones and those draining after a close.</summary>
    private IEnumerable<PhotonConnection> Sending()
    {
        foreach (PhotonConnection connection in OpenConnections())
        {
            yield return connection;
        }

        foreach (PhotonConnection connection in draining.ToArray())
        {
            // A connection whose Close is queued stays in the open list until it is closed.
            if (connection.State != ConnectionState.Open)
            {
                yield return connection;
            }
        }
    }

    /// <summary>The receiving side: counts what arrived and gives credit back once a fragment's worth came in.</summary>
    private void CountReceived(PhotonConnection connection, int bytes)
    {
        connection.ReceivedBytes += bytes;
        if (connection.ReceivedBytes - connection.CreditedBytes >= settings.FragmentBytes)
        {
            connection.CreditedBytes = connection.ReceivedBytes;
            SendControl(Wire.Credit, Wire.EncodeCredit(connection.Nonce, connection.ReceivedBytes), connection.Actor);
        }
    }

    private void OnCredit(int sender, byte[]? content)
    {
        if (!Wire.TryDecodeCredit(content, out int nonce, out long received))
        {
            return;
        }

        PhotonConnection? connection = ConnectionOf(sender);
        if (connection is null || connection.Nonce != nonce)
        {
            connection = draining.Find(c => c.Actor == sender && c.Nonce == nonce);
        }

        if (connection is not null)
        {
            connection.AckedBytes = Math.Clamp(received, connection.AckedBytes, connection.SentBytes);
            FlushOutboxes();
        }
    }

    /// <summary>The other side is gone: nothing queued for it can be delivered any more.</summary>
    private void DropQueued(PhotonConnection connection)
    {
        connection.Outbox.Clear();
        draining.Remove(connection);
    }
}

/// <summary>An event waiting in one or more outboxes; <see cref="Cost"/> counts against the send window.</summary>
internal sealed class Outgoing(byte code, byte[] content, int cost)
{
    public byte Code { get; } = code;

    public byte[] Content { get; } = content;

    public int Cost { get; } = cost;
}
