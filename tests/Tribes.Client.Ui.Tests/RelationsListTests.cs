using System.Linq;
using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The tribes panel of an endless world: this player's tribe first, then allies, tribes that
/// invite this one, the other people's tribes, computer tribes, and tribes that are out; each row
/// says how the tribe stands and offers what the rules allow now.
/// </summary>
public sealed class RelationsListTests
{
    private static TribeFacts Tribe(int tribe, bool local = false, bool ally = false, bool @out = false, bool computer = false,
        bool absent = false, bool invitedByUs = false, bool invitesUs = false, bool canInvite = false, bool canAnswer = false,
        bool canLeave = false, bool canGive = false) =>
        new(tribe, local, ally, @out, computer, absent, invitedByUs, invitesUs, canInvite, canAnswer, canLeave, canGive);

    [Fact]
    public void Rows_go_self_allies_inviters_people_computers_then_out()
    {
        var rows = RelationsList.Build(
        [
            Tribe(0, computer: true),
            Tribe(1, @out: true),
            Tribe(2, local: true),
            Tribe(3),
            Tribe(4, ally: true),
            Tribe(5, invitesUs: true),
            Tribe(6, computer: true),
            Tribe(7, ally: true),
        ]);

        Assert.Equal(new[] { 2, 4, 7, 5, 3, 0, 6, 1 }, rows.Select(r => r.Tribe));
    }

    [Theory]
    [InlineData(true, false, false, false, false, "relations.status.you")]
    [InlineData(false, true, false, false, false, "relations.status.ally")]
    [InlineData(false, false, true, false, false, "relations.status.out")]
    [InlineData(false, false, false, true, false, "relations.status.computer")]
    [InlineData(false, false, false, false, true, "relations.status.offline")]
    [InlineData(false, false, false, false, false, "relations.status.enemy")]
    public void Each_row_says_how_the_tribe_stands(bool local, bool ally, bool @out, bool computer, bool absent, string key)
    {
        RelationRow row = RelationsList.Build([Tribe(3, local: local, ally: ally, @out: @out, computer: computer, absent: absent)]).Single();

        Assert.Equal(key, row.StatusKey);
    }

    [Fact]
    public void Open_invitations_are_shown_on_both_sides()
    {
        Assert.Equal("relations.status.invited_by_you", RelationsList.Build([Tribe(3, invitedByUs: true)]).Single().StatusKey);
        Assert.Equal("relations.status.invites_you", RelationsList.Build([Tribe(3, invitesUs: true)]).Single().StatusKey);
    }

    [Fact]
    public void A_tribe_offers_what_the_rules_allow()
    {
        Assert.Equal(new[] { RelationAction.Invite }, RelationsList.Build([Tribe(3, canInvite: true)]).Single().Actions);
        Assert.Equal(new[] { RelationAction.Accept, RelationAction.Decline },
            RelationsList.Build([Tribe(3, invitesUs: true, canAnswer: true)]).Single().Actions);
        Assert.Equal(new[] { RelationAction.Give, RelationAction.Leave },
            RelationsList.Build([Tribe(3, ally: true, canLeave: true, canGive: true)]).Single().Actions);
        Assert.Empty(RelationsList.Build([Tribe(3)]).Single().Actions);
    }

    [Fact]
    public void Ones_own_tribe_and_tribes_that_are_out_offer_nothing()
    {
        Assert.Empty(RelationsList.Build([Tribe(2, local: true, canInvite: true, canGive: true)]).Single().Actions);
        Assert.Empty(RelationsList.Build([Tribe(1, @out: true, canInvite: true, canLeave: true)]).Single().Actions);
    }

    [Fact]
    public void Without_a_finished_hall_the_panel_says_to_build_one()
    {
        Assert.Equal("relations.need_hall", RelationsList.HallNote(hasHall: false, spectating: false));
        Assert.Null(RelationsList.HallNote(hasHall: true, spectating: false));
        Assert.Null(RelationsList.HallNote(hasHall: false, spectating: true));
    }
}
