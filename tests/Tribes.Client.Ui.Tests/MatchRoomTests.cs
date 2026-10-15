using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// How many tribes a room or custom game may start on a map: a custom map has room for as many
/// camps as the rules find on it; a campaign map only has the original two tribes. Computer tribes
/// can only be added while there is room, a room never starts with more tribes than the map holds,
/// and an endless world has no limit.
/// </summary>
public sealed class MatchRoomTests
{
    private const int FirstCustom = 7;

    [Fact]
    public void A_custom_map_holds_its_camp_capacity()
    {
        Assert.Equal(10, MatchRoom.TribeLimit(7, FirstCustom, campCapacity: 10));
        Assert.Equal(7, MatchRoom.TribeLimit(11, FirstCustom, campCapacity: 7));
    }

    [Fact]
    public void A_campaign_map_holds_the_original_two_tribes()
    {
        Assert.True(MatchRoom.IsCampaignMap(0, FirstCustom));
        Assert.True(MatchRoom.IsCampaignMap(6, FirstCustom));
        Assert.False(MatchRoom.IsCampaignMap(7, FirstCustom));
        Assert.Equal(2, MatchRoom.TribeLimit(3, FirstCustom, campCapacity: 20));
    }

    [Fact]
    public void An_endless_world_has_no_limit()
    {
        Assert.Equal(int.MaxValue, MatchRoom.TribeLimit(-1, FirstCustom, campCapacity: 10));
        Assert.Equal(MatchProblem.None, MatchRoom.Check(-1, FirstCustom, campCapacity: 10, humans: 4, tribes: 400));
    }

    [Theory]
    [InlineData(10, 3, 1, 3)] // three free slots, room for nine more camps
    [InlineData(10, 0, 4, 0)] // no free slot
    [InlineData(3, 3, 1, 2)] // a small map: room for two more
    [InlineData(2, 3, 2, 0)] // the map is full
    [InlineData(2, 3, 5, 0)] // more than the map holds already
    public void Computers_can_be_added_while_there_are_slots_and_room(int limit, int freeSlots, int tribes, int allowed)
    {
        Assert.Equal(allowed, MatchRoom.ComputersAllowed(limit, freeSlots, tribes));
    }

    [Fact]
    public void A_room_within_the_map_can_start()
    {
        Assert.Equal(MatchProblem.None, MatchRoom.Check(8, FirstCustom, campCapacity: 20, humans: 4, tribes: 4));
        Assert.Equal(MatchProblem.None, MatchRoom.Check(11, FirstCustom, campCapacity: 7, humans: 3, tribes: 7));
    }

    [Fact]
    public void A_room_with_more_tribes_than_the_map_holds_cannot_start()
    {
        Assert.Equal(MatchProblem.MapFull, MatchRoom.Check(11, FirstCustom, campCapacity: 7, humans: 3, tribes: 8));
    }

    [Fact]
    public void A_campaign_map_takes_at_most_two_people()
    {
        Assert.Equal(MatchProblem.None, MatchRoom.Check(2, FirstCustom, campCapacity: 0, humans: 2, tribes: 2));
        Assert.Equal(MatchProblem.CampaignMapTooManyPeople, MatchRoom.Check(2, FirstCustom, campCapacity: 0, humans: 3, tribes: 3));
        Assert.Equal(MatchProblem.MapFull, MatchRoom.Check(2, FirstCustom, campCapacity: 0, humans: 1, tribes: 3));
    }

    [Fact]
    public void Rooms_offer_only_the_custom_maps()
    {
        int[] menu = { 0, 2, 3, 4, 5, 7, 8, 9, 10, 11 };

        Assert.Equal(new[] { 7, 8, 9, 10, 11 }, MatchRoom.RoomMaps(menu, FirstCustom));
    }

    [Theory]
    [InlineData(2, 2, false, true)]
    [InlineData(1, 2, false, false)] // another player's notice
    [InlineData(2, 2, true, false)] // spectators get no player's notices
    [InlineData(-1, -1, false, false)] // no tribe
    public void Messages_go_to_the_tribe_they_are_for(int messageTribe, int localTribe, bool spectating, bool shown)
    {
        Assert.Equal(shown, MatchRoom.IsFor(messageTribe, localTribe, spectating));
    }
}
