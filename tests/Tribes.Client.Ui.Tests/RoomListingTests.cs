using System;
using System.Linq;
using Tribes.Client.Ui.Logic;
using Tribes.Net;
using Tribes.Net.Transport;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The LAN room list: which rooms are shown and what this device's player can do in each —
/// join a room, come back to a match they are in, or watch.
/// </summary>
public sealed class RoomListingTests
{
    private const string Game = "1.0.0";
    private const string Content = "content-r0";
    private static readonly Guid MyKey = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static RoomInfo Room(bool inGame, bool open = false, bool joinable = false, bool? spectators = false,
        PlayerPresence? mine = null, bool listsMembers = true, string game = Game)
    {
        var info = new RoomInfo
        {
            Address = "192.168.1.5:47801",
            Room = new RoomAdvertisement
            {
                RoomId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Name = "Room",
                Mode = joinable ? RoomMode.Endless : RoomMode.Versus,
                Players = 2,
                MaxPlayers = 4,
                GameVersion = game,
                ContentVersion = Content,
                InGame = inGame,
                IsOpen = open,
                JoinableInProgress = joinable,
                SpectatorsWelcome = listsMembers ? spectators : null,
            },
        };
        RoomMember[] members = [new(Guid.NewGuid(), PlayerPresence.Active)];
        if (mine is { } presence)
        {
            members = [.. members, new RoomMember(info.PublicIdOf(MyKey), presence)];
        }
        return info with { Room = info.Room with { Members = listsMembers ? members : null } };
    }

    private static RoomListEntry? Classify(RoomInfo room) => RoomListing.Classify(room, MyKey, Game, Content);

    [Fact]
    public void LobbyWithAFreeSlot_OffersJoin()
    {
        RoomListEntry entry = Classify(Room(inGame: false, open: true))!;

        Assert.Equal(RoomAction.Join, entry.Action);
        Assert.False(entry.InMatch);
        Assert.Null(entry.BadgeKey);
        Assert.Equal("multiplayer.join", entry.ActionKey);
        Assert.False(entry.ConnectsAsSpectator);
    }

    [Fact]
    public void FullLobby_OffersWatching_OrIsHidden()
    {
        Assert.Equal(RoomAction.Spectate, Classify(Room(inGame: false, spectators: true))!.Action);
        Assert.Null(Classify(Room(inGame: false, spectators: false)));
    }

    [Theory]
    [InlineData(PlayerPresence.Absent)]
    [InlineData(PlayerPresence.Active)]
    public void MatchWithMyTribe_OffersComingBack(PlayerPresence mine)
    {
        RoomListEntry entry = Classify(Room(inGame: true, mine: mine))!;

        Assert.Equal(RoomAction.Return, entry.Action);
        Assert.True(entry.InMatch);
        Assert.Equal("multiplayer.badge_in_match", entry.BadgeKey);
        Assert.Equal("multiplayer.return", entry.ActionKey);
        Assert.False(entry.ConnectsAsSpectator);
    }

    [Fact]
    public void MatchIForfeited_OffersOnlyWatching_AsMyself()
    {
        RoomListEntry entry = Classify(Room(inGame: true, joinable: true, open: true, spectators: false, mine: PlayerPresence.Forfeited))!;

        Assert.Equal(RoomAction.Watch, entry.Action);
        Assert.Equal("multiplayer.spectate", entry.ActionKey);
        Assert.False(entry.ConnectsAsSpectator);
    }

    [Fact]
    public void EndlessMatchWithInvitesOpen_OffersJoin()
    {
        RoomListEntry entry = Classify(Room(inGame: true, open: true, joinable: true))!;

        Assert.Equal(RoomAction.Join, entry.Action);
        Assert.True(entry.InMatch);
    }

    [Fact]
    public void EndlessMatchWithInvitesOpenButFull_OffersWatching()
    {
        Assert.Equal(RoomAction.Spectate, Classify(Room(inGame: true, open: false, joinable: true, spectators: true))!.Action);
    }

    [Fact]
    public void OtherPeoplesMatch_OffersWatching_AsASpectator()
    {
        RoomListEntry entry = Classify(Room(inGame: true, spectators: true))!;

        Assert.Equal(RoomAction.Spectate, entry.Action);
        Assert.Equal("multiplayer.spectate", entry.ActionKey);
        Assert.True(entry.ConnectsAsSpectator);
    }

    [Fact]
    public void MatchThatAllowsNothing_IsHidden()
    {
        Assert.Null(Classify(Room(inGame: true, spectators: false)));
    }

    [Fact]
    public void MatchOfAnOlderHost_OffersConnecting_AndTheHostDecides()
    {
        RoomListEntry entry = Classify(Room(inGame: true, listsMembers: false))!;

        Assert.Equal(RoomAction.Connect, entry.Action);
        Assert.Equal("multiplayer.connect_room", entry.ActionKey);
        Assert.False(entry.ConnectsAsSpectator);
    }

    [Fact]
    public void RoomOfAnotherVersion_IsListed_ButCannotBeJoined()
    {
        RoomListEntry entry = Classify(Room(inGame: true, game: "0.9.0", mine: PlayerPresence.Absent))!;

        Assert.Equal(RoomAction.None, entry.Action);
        Assert.False(entry.Compatible);
        Assert.False(entry.CanAct);
    }

    [Fact]
    public void RoomWhoseHostRunsOtherRules_IsListed_ButCannotBeJoined()
    {
        RoomInfo room = Room(inGame: true, mine: PlayerPresence.Absent);
        room = room with { Room = room.Room with { RulesFingerprint = "r-host" } };

        RoomListEntry other = RoomListing.Classify(room, MyKey, Game, Content, "r-mine")!;
        RoomListEntry same = RoomListing.Classify(room, MyKey, Game, Content, "r-host")!;

        Assert.Equal(RoomAction.None, other.Action);
        Assert.False(other.Compatible);
        Assert.Equal("multiplayer.incompatible", other.NoteKey);
        Assert.Equal(RoomAction.Return, same.Action);
        Assert.Single(RoomListing.Build([room], MyKey, Game, Content, "r-mine"), e => !e.Compatible);
    }

    [Fact]
    public void Build_LeavesOutHiddenRooms_AndPutsMyMatchesFirst()
    {
        RoomInfo hidden = Room(inGame: true, spectators: false) with { Address = "hidden" };
        RoomInfo lobby = Room(inGame: false, open: true) with { Address = "lobby" };
        RoomInfo mine = Room(inGame: true, mine: PlayerPresence.Absent) with { Address = "mine" };

        var entries = RoomListing.Build([lobby, hidden, mine], MyKey, Game, Content);

        Assert.Equal(["mine", "lobby"], entries.Select(e => e.Room.Address).ToArray());
    }
}
