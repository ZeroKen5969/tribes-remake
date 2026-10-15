using System.Linq;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="CoupleLinks"/> with the original, plus edge cases.</summary>
public sealed class CoupleLinksTests
{
    private const int PairLinkField = 82;
    private const int UnitsPerSample = 3;

    private readonly ITestOutputHelper output;

    public CoupleLinksTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void DissolveAndRelease_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var live = LegacySamples.LiveUnits(game).ToList();
            var paired = live.Where(id => (LegacySamples.Field(game, PairLinkField, id) & 0x7E) != 0).ToList();
            foreach (int id in paired.Concat(SampleInputs.Spread(live, UnitsPerSample)).Distinct())
            {
                foreach (int mode in new[] { -1, 0, 1 })
                {
                    run.Check(sample, $"Dissolve unit {id} mode {mode}",
                        g => { g.world_a.i_v = id; g.world_a.d_II(id, mode); },
                        s => s.CoupleLinks.Dissolve(s.World.Unit(id), mode));
                }
                run.Check(sample, $"ReleasePartner unit {id}",
                    g => { g.world_a.i_v = id; g.world_a.k_I(id); },
                    s => s.CoupleLinks.ReleasePartner(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void CancelCoupleAt_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int hut in LegacySamples.UsedBuildings(game))
            {
                run.Check(sample, $"CancelCoupleAt hut {hut}",
                    g => g.world_a.a_I_V(hut),
                    s => s.CoupleLinks.CancelCoupleAt(s.World.Building(hut)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 50);
    }

    [Fact]
    public void Dissolve_ReleasesBothAndKeepsTheDelayedDeathBit()
    {
        var world = TestWorlds.Empty();
        var links = new CoupleLinks(world, new UnitActions(world));
        Unit man = world.Unit(2), woman = world.Unit(3);
        Couple couple = world.Couples[4];
        (couple.Man, couple.Woman, couple.State, couple.Hut) = (2, 3, CoupleLinks.WalkingIntoHut, 5);
        world.Building(5).Aux = 2;
        man.PairLink = 0x80 | ((4 + 1) << 1);
        woman.PairLink = ((4 + 1) << 1) | 1;
        (man.Action, man.Age, man.X, man.Y) = (ActionNumbers.FirstCoupleAction, 30, 3, 4);
        world.Map.SetCell(new Content.GridPoint(3, 4), MapCell.UnitAt(2));
        (woman.Action, woman.Age, woman.CarriedItem, woman.CarriedAmount) = (ActionNumbers.FirstCoupleAction, 30, CoupleLinks.CourtshipGift, 1);

        links.Dissolve(woman, -1);

        Assert.Equal(0, couple.State);
        Assert.Equal((0x80 - 256, 0), (man.PairLink, woman.PairLink));
        Assert.Equal(0, world.Building(5).Aux);
        Assert.Equal(ActionNumbers.Idle, man.Action);
        Assert.Equal((ActionNumbers.LeaveBuilding, OrderModes.Forced, 0), (woman.Action, woman.OrderMode, woman.CarriedAmount));
    }

    [Fact]
    public void Dissolve_WithPositiveModeWaitsForTheWalkIntoTheHut()
    {
        var world = TestWorlds.Empty();
        var links = new CoupleLinks(world, new UnitActions(world));
        Couple couple = world.Couples[0];
        (couple.Man, couple.Woman, couple.State) = (1, 2, 1);
        world.Unit(1).PairLink = 1 << 1;

        links.Dissolve(world.Unit(1), 1);
        Assert.Equal(1, couple.State);
        links.Dissolve(world.Unit(7), 0);
        Assert.Equal(1, couple.State);
    }
}
