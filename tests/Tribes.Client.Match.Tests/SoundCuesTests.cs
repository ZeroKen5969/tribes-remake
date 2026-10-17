using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Content;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Units.Combat;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// Which happenings of a match sound (<see cref="SoundCues"/>): the notices of the player's tribe by
/// kind, blows and throws where they happen, and the end of the match.
/// </summary>
public sealed class SoundCuesTests
{
    [Theory]
    [InlineData(1, SoundCues.Birth)]
    [InlineData(2, SoundCues.Birth)]
    [InlineData(3, SoundCues.Death)]
    [InlineData(4, SoundCues.Death)]
    [InlineData(5, SoundCues.Built)]
    [InlineData(6, SoundCues.Enemy)]
    [InlineData(7, SoundCues.Attack)]
    [InlineData(8, SoundCues.Destroyed)]
    [InlineData(9, SoundCues.Denied)]
    [InlineData(10, SoundCues.Denied)]
    [InlineData(11, SoundCues.Denied)]
    [InlineData(0, SoundCues.Notice)]
    [InlineData(12, SoundCues.Notice)]
    [InlineData(15, SoundCues.Notice)]
    public void EveryNoticeOfTheOriginal_HasItsSound(int kind, string sound) =>
        Assert.Equal(sound, SoundCues.ForNotice(kind));

    [Fact]
    public void NoticesBeyondTheOriginals_AreSilent()
    {
        Assert.Null(SoundCues.ForNotice(16));
        Assert.Null(SoundCues.ForNotice(-1));
    }

    [Fact]
    public void Blows_SoundWhereTheyLand_OnUnitsBuildingsAndAnimals()
    {
        GameWorld world = Repo.Instance.Started(2).World;
        Unit unit = world.Unit(BuildingOffers.LivingUnits(world, 0).First());
        Building building = Enumerable.Range(1, world.BuildingSlots).Select(world.Building).First(b => b.InUse);

        Assert.Equal((SoundCues.Hit, new GridPoint(unit.X, unit.Y)), SoundCues.Seen(world, new BlowLanded(1, unit.Id, 3, 0, 52)));
        Assert.Equal((SoundCues.Hit, new GridPoint(building.X, building.Y)), SoundCues.Seen(world, new BlowLanded(2, building.Id, 3, 0, 52)));
        if (world.AnimalSlots > 0)
        {
            Animal animal = world.Animal(0);
            Assert.Equal((SoundCues.Hit, new GridPoint(animal.X, animal.Y)), SoundCues.Seen(world, new BlowLanded(5, 0, 3, 0, 52)));
        }
        Assert.Equal((SoundCues.Throw, new GridPoint(unit.X, unit.Y)), SoundCues.Seen(world, new ProjectileThrown(unit.Id, 10, 4)));
    }

    [Fact]
    public void BlowsOnOtherThings_AndOtherHappenings_AreSilent()
    {
        GameWorld world = Repo.Instance.Started(2).World;

        Assert.Null(SoundCues.Seen(world, new BlowLanded(3, 1, 3, 0, 52)));
        Assert.Null(SoundCues.Seen(world, new BlowLanded(1, world.UnitSlots + 1, 3, 0, 52)));
        Assert.Null(SoundCues.Seen(world, new ProjectileThrown(0, 10, 4)));
        Assert.Null(SoundCues.Seen(world, new NoticeEvent(1, 0, 0, 0)));
    }

    [Fact]
    public void TheEnd_SoundsAsItTurnedOut()
    {
        Assert.Equal(SoundCues.Victory, SoundCues.ForResult(victory: true));
        Assert.Equal(SoundCues.Defeat, SoundCues.ForResult(victory: false));
    }
}
