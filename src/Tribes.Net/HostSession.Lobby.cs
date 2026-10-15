using System;
using Tribes.Net.Protocol;

namespace Tribes.Net;

public sealed partial class HostSession
{
    /// <summary>Chooses the host's colour (lobby only); no other slot may use it.</summary>
    /// <param name="colour">Colour id 0 to 7.</param>
    public RequestResult ChooseColour(int colour) => Publish(room.ChooseColour(room.Host.Id, colour));

    /// <summary>Chooses the host's team (versus lobby only).</summary>
    /// <param name="team">Team id 0 to 3.</param>
    public RequestResult ChooseTeam(int team) => Publish(room.ChooseTeam(room.Host.Id, team));

    /// <summary>Adds a computer player to the first free slot, with the first free colour.</summary>
    /// <param name="team">Team id, or -1 for a team of its own.</param>
    /// <param name="aiLevel">Strength, 0 to 255; the game decides what it means.</param>
    /// <returns>The slot, or -1 when no slot is free or the match has started.</returns>
    public int AddComputerPlayer(int team = -1, int aiLevel = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int slot = closed ? -1 : room.AddComputer(team, aiLevel);
        if (slot >= 0)
        {
            PublishRoom();
        }

        return slot;
    }

    /// <summary>Changes a computer player's colour, team and strength (lobby only).</summary>
    /// <param name="slot">The computer player's slot.</param>
    /// <param name="colour">Colour id 0 to 7.</param>
    /// <param name="team">Team id 0 to 3.</param>
    /// <param name="aiLevel">Strength, 0 to 255.</param>
    public RequestResult ConfigureComputerPlayer(int slot, int colour, int team, int aiLevel) =>
        Publish(room.ConfigureComputer(slot, colour, team, aiLevel));

    /// <summary>Removes a computer player (lobby only).</summary>
    /// <param name="slot">The computer player's slot.</param>
    public RequestResult RemoveComputerPlayer(int slot) => Publish(room.RemoveComputer(slot));

    /// <summary>
    /// Removes a person from the room for the rest of its life; they cannot join again. In a
    /// running match their tribe stays in the world without orders; the computer does not play it.
    /// </summary>
    /// <param name="playerId">The person's persistent id.</param>
    public RequestResult Kick(Guid playerId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        PlayerInfo? player = room.Find(playerId);
        if (closed || player is null || player.IsHost || player.Kind == PlayerKind.Computer)
        {
            return RequestResult.NotFound;
        }

        room.Ban(playerId);
        if (FindPeer(playerId) is { } peer)
        {
            peers.Remove(peer.Connection.Id);
            Send(peer, new SessionEndMessage(SessionEndReason.Kicked, "The host removed you from the room."));
            peer.Connection.Disconnect();
        }

        HandleDeparture(playerId);
        return RequestResult.Accepted;
    }

    /// <summary>Whether the match could start now, and if not, why.</summary>
    public RequestResult CanStart() => closed ? RequestResult.NotAllowedNow : room.CanStart();

    /// <summary>
    /// Endless mode: opens or closes the running match to new players (the host "invites" LAN
    /// players by opening it). Room lists show the change at once.
    /// </summary>
    /// <param name="allowed">Whether new players may join.</param>
    public void SetJoinInProgress(bool allowed)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!closed && room.Settings.AllowJoinInProgress != allowed)
        {
            room.SetJoinInProgress(allowed);
            PublishRoom();
        }
    }

    /// <summary>Sends a chat line from the host to everyone.</summary>
    /// <param name="text">The line; control characters are removed and it is cut to <see cref="RoomLimits.MaxChatLength"/>.</param>
    public RequestResult SendChat(string text)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string clean = RoomLimits.CleanText(text, RoomLimits.MaxChatLength);
        if (closed)
        {
            return RequestResult.NotAllowedNow;
        }

        if (clean.Length == 0)
        {
            return RequestResult.InvalidValue;
        }

        BroadcastChat(room.Host, clean);
        return RequestResult.Accepted;
    }

    private RequestResult Publish(RequestResult result)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (closed)
        {
            return RequestResult.NotAllowedNow;
        }

        if (result == RequestResult.Accepted)
        {
            PublishRoom();
        }

        return result;
    }

    private void HandleChat(RemotePeer peer, string text)
    {
        if (!peer.AllowChat(clock.Now))
        {
            Deny(peer, RequestKind.Chat, RequestResult.RateLimited);
            return;
        }

        string clean = RoomLimits.CleanText(text, RoomLimits.MaxChatLength);
        if (clean.Length > 0 && room.Find(peer.PlayerId) is { } sender)
        {
            BroadcastChat(sender, clean);
        }
    }

    private void BroadcastChat(PlayerInfo sender, string text)
    {
        transport.SendToMany(JoinedConnections(), MessageCodec.Encode(new ChatRelayMessage(sender.Id, sender.Name, text)));
        ChatReceived?.Invoke(this, new ChatReceivedEventArgs { SenderId = sender.Id, SenderName = sender.Name, Text = text });
    }
}
