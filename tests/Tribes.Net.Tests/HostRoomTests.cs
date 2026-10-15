using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Net;
using Xunit;

namespace Tribes.Net.Tests;

public sealed class HostRoomTests
{
    private static readonly Guid HostId = Guid.NewGuid();

    private static HostRoom Room(RoomMode mode = RoomMode.Versus, int maxPlayers = 4) =>
        new(Guid.NewGuid(), new RoomSettings { Mode = mode, MaxPlayers = maxPlayers }, HostId, "Host", 3);

    [Fact]
    public void Admission_RejectsIdsOfOtherParticipants()
    {
        HostRoom room = Room();
        int computer = room.AddComputer(-1, 0);
        Guid computerId = room.InSlot(computer)!.Id;

        Assert.Equal(RejectReason.InvalidRequest, room.Admit(HostId, "x", false, -1).Reason);
        Assert.Equal(RejectReason.InvalidRequest, room.Admit(computerId, "x", false, -1).Reason);
        Assert.Equal(3, room.Host.Colour);
    }

    [Fact]
    public void Colours_StayUnique_AsPlayersArrive()
    {
        HostRoom room = Room(maxPlayers: 4);
        var ids = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            Guid id = Guid.NewGuid();
            ids.Add(id);
            Assert.Equal(AdmitOutcome.Joined, room.Admit(id, "p", false, 3).Outcome);
        }

        Assert.Equal(4, room.State.Players.Select(p => p.Colour).Distinct().Count());
        Assert.Equal(RequestResult.NotAPlayer, room.ChooseColour(Guid.NewGuid(), 6));
        Assert.Equal(RequestResult.NotAPlayer, room.SetReady(Guid.NewGuid(), true));
        Assert.Equal(RequestResult.NotAPlayer, room.ChooseTeam(Guid.NewGuid(), 1));
        Assert.Equal(AdmitOutcome.Reconnected, room.Admit(ids[0], "renamed", false, -1).Outcome);
        Assert.Equal("renamed", room.Find(ids[0])!.Name);
    }

    [Fact]
    public void Restore_MarksActivePlayersAbsent_AndDropsSpectators()
    {
        HostRoom original = Room(RoomMode.Endless);
        Guid active = Guid.NewGuid();
        Guid forfeited = Guid.NewGuid();
        Guid watcher = Guid.NewGuid();
        original.Admit(active, "a", false, -1);
        original.Admit(forfeited, "f", false, -1);
        original.Admit(watcher, "w", true, -1);
        original.AddComputer(-1, 1);
        original.BeginMatch();
        original.SetPresence(forfeited, PlayerPresence.Forfeited);
        original.Ban(Guid.NewGuid());

        HostRoom restored = HostRoom.Restore(original.State, original.Banned, HostId, out List<int> absent);

        Assert.Equal([original.Find(active)!.Slot], absent);
        Assert.Equal(PlayerPresence.Absent, restored.Find(active)!.Presence);
        Assert.Equal(PlayerPresence.Forfeited, restored.Find(forfeited)!.Presence);
        Assert.Null(restored.Find(watcher));
        Assert.True(restored.Host.IsConnected);
        Assert.Single(restored.Banned);
        Assert.Equal(RoomPhase.InGame, restored.Phase);
        Assert.Throws<ArgumentException>(() => HostRoom.Restore(original.State, [], Guid.NewGuid(), out _));
    }

    [Fact]
    public void LobbyRules_DependOnThePhase()
    {
        HostRoom room = Room();
        Guid id = Guid.NewGuid();
        room.Admit(id, "p", false, -1);
        room.SetReady(id, true);
        Assert.Equal(RequestResult.Accepted, room.CanStart());
        room.BeginMatch();

        Assert.Equal(RequestResult.NotAllowedNow, room.ChooseColour(id, 6));
        Assert.Equal(RequestResult.NotAllowedNow, room.ChooseTeam(id, 1));
        Assert.Equal(RequestResult.NotAllowedNow, room.SetReady(id, false));
        Assert.Equal(RequestResult.NotAllowedNow, room.ConfigureComputer(1, 1, 1, 1));
        Assert.Equal(RequestResult.NotAllowedNow, room.CanStart());
        Assert.Equal(-1, room.AddComputer(0, 0));
        room.Remove(HostId);
        Assert.NotNull(room.Find(HostId));
        room.Close();
        Assert.Equal(RoomPhase.Closed, room.State.Phase);
    }
}
