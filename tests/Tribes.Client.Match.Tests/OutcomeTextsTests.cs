using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Commands;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>A refused placement mentions the building limit only where tribes have one.</summary>
public sealed class OutcomeTextsTests
{
    [Fact]
    public void Blocked_mentions_the_limit_only_with_one()
    {
        Assert.Equal("hud.outcome.Blocked", OutcomeTexts.Key(OrderOutcome.Blocked, buildingLimit: true));
        Assert.Equal("hud.outcome.Blocked.unlimited", OutcomeTexts.Key(OrderOutcome.Blocked, buildingLimit: false));
    }

    [Fact]
    public void Other_outcomes_keep_their_own_text()
    {
        Assert.Equal("hud.outcome.NotOffered", OutcomeTexts.Key(OrderOutcome.NotOffered, buildingLimit: false));
        Assert.Equal("hud.outcome.OutOfMap", OutcomeTexts.Key(OrderOutcome.OutOfMap, buildingLimit: true));
    }
}
