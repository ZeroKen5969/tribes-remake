using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>How a match starts.</summary>
public sealed class MatchStartTests
{
    [Fact]
    public void EveryTribe_StartsWithTheOriginalPlayersCamp()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 3, MatchWorlds.Controllers(2, 2), teams: [0, 1, 1, 2]));
        GameWorld world = game.World;
        Assert.True(game.IsMatch);
        Assert.Equal(WorldMode.Match, world.Mode);
        Assert.Equal(MatchStart.FirstMapMission, world.Mission);
        Assert.Equal(4, world.Tribes.Count);
        Assert.Equal(4, world.Tribes.Select(t => t.Camp).Distinct().Count());
        foreach (TribeState tribe in world.Tribes)
        {
            Building chiefsHut = world.Building(world.FirstBuildingSlot(tribe.Index));
            Assert.Equal(BuildingKindNumbers.ChiefsHut, chiefsHut.Kind);
            Assert.Equal(tribe.Camp, new GridPoint(chiefsHut.X, chiefsHut.Y));
            Assert.Equal(2, tribe[TribeCounter.Buildings]);
            Assert.Equal(StartUnits(world), MatchWorlds.LiveAdults(world, tribe.Index).Count());
            bool human = tribe.Controller.IsHumanRuled;
            Assert.Equal(human, tribe[TribeCounter.Chief] == world.FirstUnitSlot(tribe.Index));
            Assert.Equal(human ? 20 : 2000, tribe[TribeCounter.Wood]);
        }
        Assert.True(world.Relations.AreAllies(1, 2));
        Assert.True(TribeRoles.AreEnemies(world, 0, 1));
        Assert.Equal(new[] { 0, 1 }, TribeRoles.HumanTribes(world));
        Assert.Equal(new[] { 2, 3 }, TribeRoles.ComputerTribes(world));
        Assert.Equal(new[] { 0, 3 }, TribeRoles.EnemiesOf(world, 1));
        Assert.False(game.Options.HintsPauseGame);
    }

    [Fact]
    public void SameSeed_SameCamps_OtherSeed_OtherCamps()
    {
        MatchSetup setup = MatchWorlds.Setup(1, 21, MatchWorlds.Controllers(1, 3));
        GameWorld a = GameSimulation.StartMatch(MatchWorlds.Content, setup).World;
        GameWorld b = GameSimulation.StartMatch(MatchWorlds.Content, setup).World;
        GameWorld c = GameSimulation.StartMatch(MatchWorlds.Content, setup with { Seed = 22 }).World;
        Assert.Equal(a.Tribes.Select(t => t.Camp), b.Tribes.Select(t => t.Camp));
        Assert.Equal(WorldSnapshot.Hash(a), WorldSnapshot.Hash(b));
        Assert.NotEqual(WorldSnapshot.Hash(a), WorldSnapshot.Hash(c));
    }

    private static int StartUnits(GameWorld world) => world.Content.Rules.Missions.Missions[world.Mission].InitialObjects
        .Count(o => o.Kind == Core.Content.Rules.InitialObjectKind.Unit && o.Tribe == 0);
}
