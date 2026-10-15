using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Systems.Endless;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// The building panel shows what a building is on the map: a construction site the building it
/// becomes (also the alliance building, past the original's kinds), every other building its kind.
/// </summary>
public sealed class ShownKindTests
{
    [Fact]
    public void Buildings_show_their_own_kind_whatever_their_aux()
    {
        for (int kind = 0; kind < RuleNumbers.PlannedKinds; kind++)
        {
            Assert.Equal(kind, RuleNumbers.ShownKind(kind, aux: 0));
            Assert.Equal(kind, RuleNumbers.ShownKind(kind, aux: 7));
        }
        Assert.Equal(AllianceBuilding.Kind, RuleNumbers.ShownKind(AllianceBuilding.Kind, aux: 3));
    }

    [Fact]
    public void Sites_show_the_building_they_become()
    {
        for (int site = RuleNumbers.FirstSite; site <= RuleNumbers.LastSite; site++)
        {
            for (int target = 0; target < RuleNumbers.PlannedKinds; target++)
            {
                Assert.Equal(target, RuleNumbers.ShownKind(site, target));
            }
            Assert.Equal(AllianceBuilding.Kind, RuleNumbers.ShownKind(site, AllianceBuilding.Kind));
        }
    }

    [Fact]
    public void A_site_of_no_known_building_stays_a_site()
    {
        Assert.Equal(RuleNumbers.FirstSite, RuleNumbers.ShownKind(RuleNumbers.FirstSite, aux: RuleNumbers.FirstSite));
        Assert.Equal(RuleNumbers.LastSite, RuleNumbers.ShownKind(RuleNumbers.LastSite, aux: -1));
        Assert.Equal(RuleNumbers.LastSite, RuleNumbers.ShownKind(RuleNumbers.LastSite, aux: 40));
    }
}
