using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Logic;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// The tribes panel reads an endless world through <see cref="AllianceFacts"/>: who is this
/// player's, who is a person or the computer, and what the rules let this player do now; the
/// panel's orders reach the rules (which refuse them while no alliance hall stands).
/// </summary>
public sealed class AllianceFactsTests
{
    private static GameSimulation World(int humans, int computers) =>
        GameSimulation.StartEndless(Repo.Instance.Content, new EndlessSetup
        {
            Seed = 21,
            Tribes = Enumerable.Range(0, humans).Select(h => new MatchTribe(TribeController.Human(h))).ToArray(),
            ComputerTribes = computers,
            NewTribes = 0,
        });

    [Fact]
    public void Every_tribe_is_known_and_this_players_is_marked()
    {
        GameSimulation game = World(humans: 2, computers: 1);

        var facts = AllianceFacts.For(game.World, local: 1);

        Assert.Equal(game.World.Tribes.Count, facts.Count);
        Assert.True(facts.Single(f => f.Tribe == 1).IsLocal);
        Assert.Equal(1, facts.Count(f => f.IsComputer));
        Assert.All(facts, f => Assert.False(f.IsAlly));
        Assert.All(facts, f => Assert.False(f.IsOut));
    }

    [Fact]
    public void Nothing_can_be_done_before_the_alliance_hall_stands()
    {
        GameSimulation game = World(humans: 2, computers: 1);

        var facts = AllianceFacts.For(game.World, local: 0);

        Assert.All(facts, f => Assert.False(f.CanInvite || f.CanAnswer || f.CanLeave || f.CanGive));
        Assert.False(AllianceRules.HasAllianceBuilding(game.World, 0));
        RelationRow other = RelationsList.Build(facts).Single(r => r.Tribe == 1);
        Assert.Equal("relations.status.enemy", other.StatusKey);
        Assert.Empty(other.Actions);
    }

    [Fact]
    public void A_watcher_is_nobodys_tribe()
    {
        GameSimulation game = World(humans: 2, computers: 0);

        var facts = AllianceFacts.For(game.World, local: -1);

        Assert.All(facts, f => Assert.False(f.IsLocal || f.CanInvite || f.CanGive));
    }

    [Fact]
    public void The_panels_orders_reach_the_rules()
    {
        GameSimulation game = World(humans: 2, computers: 0);

        TickResult result = game.Step(TickInput.WithOrders(AllianceOrders.Invite(0, 1), AllianceOrders.Gift(0, 1, TribeCounter.Wood, 5)));

        Assert.Equal(new[] { OrderOutcome.NotAvailable, OrderOutcome.NotAvailable }, result.Outcomes);
    }

    [Fact]
    public void The_panels_orders_survive_the_network_codec()
    {
        foreach (GameOrder order in new[]
        {
            AllianceOrders.Invite(0, 1), AllianceOrders.Answer(1, 0, accept: true), AllianceOrders.Leave(0, 1),
            AllianceOrders.Gift(0, 1, TribeCounter.Stones, 12),
        })
        {
            Assert.True(GameOrderCodec.TryDecode(GameOrderCodec.Encode(order), order.Tribe, out GameOrder? decoded));
            Assert.Equal(order, decoded);
        }
    }
}
