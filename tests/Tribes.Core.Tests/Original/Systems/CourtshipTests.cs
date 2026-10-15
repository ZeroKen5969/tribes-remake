using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Courtship"/> with the original, plus edge cases.</summary>
public sealed class CourtshipTests
{
    private const int HappinessField = 5, CooldownCounter = 18;

    private readonly ITestOutputHelper output;

    public CourtshipTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SeekPartner_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var adults = UnitSampling.Units(game, 6).Where(id => LegacySamples.Field(game, UnitSampling.AgeField, id) >= 12).ToList();
            foreach (int id in adults)
            {
                int tribe = (id - 1) / 50;
                foreach (bool potion in new[] { false, true })
                {
                    int excluded = adults.FirstOrDefault(a => a != id);
                    run.Check(sample, $"SeekPartner unit {id} potion {potion}",
                        g =>
                        {
                            MakeHappy(g, tribe);
                            Legacy.World w = UnitSampling.Turn(g, id, 0);
                            w.i_R = excluded;
                            return w.a_Z_Z(potion);
                        },
                        s =>
                        {
                            MakeHappy(s.World, tribe);
                            return s.Courtship.SeekPartner(s.UnitGoals.StartTurn(s.World.Unit(id)), potion, excluded);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void Advance_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var paired = Enumerable.Range(1, 100).Where(id => (LegacySamples.Field(game, 82, id) & 0x7E) != 0);
            foreach (int id in paired.Concat(UnitSampling.Units(game, 2)))
            {
                foreach (int frame in new[] { -1, 6 })
                {
                    int tick = frame < 0 ? LegacySamples.Field(game, 30, id) : frame;
                    run.Check(sample, $"Advance unit {id} tick {tick}",
                        g => { UnitSampling.Set(g, 30, id, tick); return UnitSampling.Turn(g, id, 0).o__Z(); },
                        s => { s.World.Unit(id).AnimationTick = tick; return s.Courtship.Advance(s.UnitGoals.StartTurn(s.World.Unit(id))); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void SeekPartner_PairsWithAFreeHutAndAdvanceWalksThemIn()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Tribes[0][TribeCounter.BirthCooldown] = -1;
        (world.Tribes[0][TribeCounter.Population], world.Tribes[0][TribeCounter.Buildings]) = (2, 1);
        Unit man = UnitNeedsTests.Adult(world, 26);
        Unit woman = UnitNeedsTests.Adult(world, 27);
        (man.Happiness, woman.Happiness, woman.Gender) = (90, 90, 1);
        Building hut = world.Building(2);
        (hut.Kind, hut.HitPoints, hut.Level, hut.X, hut.Y) = (BuildingKindNumbers.Hut, 30, 1, 3, 3);

        Assert.True(systems.Courtship.SeekPartner(man, true, 0));

        Couple couple = world.Couples[0];
        Assert.Equal((1, 26, 27, 2, 4, 5), (couple.State, couple.Man, couple.Woman, couple.Hut, couple.EntranceX, couple.EntranceY));
        Assert.Equal((2, 3, 1), (man.PairLink, woman.PairLink, hut.Aux));
        Assert.Equal(0, world.Tribes[0][TribeCounter.BirthCooldown]);
    }

    [Fact]
    public void Advance_TheCoupleMeetsGivesTheGiftAndWalksIntoTheHut()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit man = UnitNeedsTests.Adult(world, 34);
        Unit woman = UnitNeedsTests.Adult(world, 35);
        woman.Gender = 1;
        world.Map.SetCell(new Content.GridPoint(woman.X, woman.Y), Maps.MapCell.Empty);
        (woman.X, woman.Y) = (man.X + 1, man.Y);
        world.Map.SetCell(new Content.GridPoint(woman.X, woman.Y), Maps.MapCell.UnitAt(35));
        Building hut = world.Building(5);
        (hut.Kind, hut.HitPoints, hut.Level, hut.X, hut.Y, hut.Aux) = (BuildingKindNumbers.Hut, 30, 1, 3, 3, 1);
        Couple couple = world.Couples[2];
        (couple.State, couple.Man, couple.Woman, couple.Hut) = (2, 34, 35, 5);
        (man.PairLink, woman.PairLink) = (6, 7);
        foreach (Unit member in new[] { man, woman })
        {
            (member.Action, member.DestinationX, member.DestinationY) = (109, man.X, man.Y);
        }
        (man.CarriedItem, man.CarriedAmount) = (CoupleLinks.CourtshipGift, 1);

        Assert.True(systems.Courtship.Advance(man));
        Assert.Equal((UnitAnimations.GivingGift, UnitAnimations.ReceivingGift, 2),
            (man.AnimationNumber, woman.AnimationNumber, hut.Aux));

        man.AnimationTick = UnitAnimations.GiftHandedFrame;
        Assert.True(systems.Courtship.Advance(man));
        Assert.Equal((0, CoupleLinks.CourtshipGift, 1), (man.CarriedItem, woman.CarriedItem, woman.CarriedAmount));

        man.AnimationTick = world.Content.Rules.Animations.Animations[man.AnimationNumber].FrameCount - 1;
        Assert.True(systems.Courtship.Advance(man));
        Assert.Equal(3, couple.State);

        woman.Action = 111;
        Assert.True(systems.Courtship.Advance(woman));
        Assert.Equal(3, hut.Aux);
        Assert.Equal(0, couple.State);
    }

    [Fact]
    public void Advance_IgnoresUnitsWithoutACouple()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 28);
        Assert.False(systems.Courtship.Advance(systems.UnitGoals.StartTurn(unit)));
    }

    private static void MakeHappy(Legacy.GameCanvas game, int tribe)
    {
        game.sArr2_a[tribe][CooldownCounter] = -1;
        foreach (int id in Enumerable.Range((tribe * 50) + 1, 50))
        {
            UnitSampling.Set(game, HappinessField, id, 80);
        }
    }

    private static void MakeHappy(GameWorld world, int tribe)
    {
        world.Tribes[tribe][TribeCounter.BirthCooldown] = -1;
        foreach (int id in Enumerable.Range((tribe * 50) + 1, 50))
        {
            world.Unit(id).Happiness = 80;
        }
    }
}
