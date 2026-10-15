using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>How many camps each custom map has room for, and setups that ask for more.</summary>
public sealed class CampCapacityTests
{
    /// <summary>Camps the custom maps (missions 7..11) have room for, each camp with room to build on its own side.</summary>
    private static readonly int[] Capacities = [9, 7, 5, 6, 4];

    [Fact]
    public void EveryCustomMap_HasItsCampCapacity_AndRoomForFourPlayers()
    {
        int[] capacities = Enumerable.Range(0, MatchStart.MapCount(MatchWorlds.Content))
            .Select(map => MatchStart.CampCapacity(MatchWorlds.Content, map)).ToArray();
        Assert.Equal(Capacities, capacities);
        Assert.All(capacities, c => Assert.True(c >= 4, $"a map has room for only {c} camps"));
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchStart.CampCapacity(MatchWorlds.Content, Capacities.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AFullMap_GivesEveryTribeACampOfItsOwn(int map)
    {
        int count = Capacities[map];
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, 3, MatchWorlds.Controllers(4, count - 4)));
        GameWorld world = game.World;
        GridPoint[] camps = world.Tribes.Select(t => t.Camp).ToArray();
        for (int a = 0; a < camps.Length; a++)
        {
            Assert.Equal(2, world.Tribes[a][TribeCounter.Buildings]);
            Assert.True(world.Tribes[a][TribeCounter.Population] > 10);
            for (int b = a + 1; b < camps.Length; b++)
            {
                int dx = camps[a].X - camps[b].X, dy = camps[a].Y - camps[b].Y;
                Assert.True((dx * dx) + (dy * dy) >= 144, $"camps {a} and {b} are too close");
            }
        }
        GameSimulation other = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, 4, MatchWorlds.Controllers(4, count - 4)));
        Assert.Equal(camps.OrderBy(c => (c.X, c.Y)), other.World.Tribes.Select(t => t.Camp).OrderBy(c => (c.X, c.Y)));
    }

    [Fact]
    public void ASetupWithMoreTribesThanCamps_IsRefusedBeforeItStarts()
    {
        MatchSetup setup = MatchWorlds.Setup(4, 1, MatchWorlds.Controllers(4, Capacities[4] - 3));
        string? problem = MatchStart.Problem(MatchWorlds.Content, setup);
        Assert.NotNull(problem);
        Assert.Contains("room for 4 camps", problem, StringComparison.Ordinal);
        ArgumentException refused = Assert.Throws<ArgumentException>(() => GameSimulation.StartMatch(MatchWorlds.Content, setup));
        Assert.Contains("room for 4 camps", refused.Message, StringComparison.Ordinal);
        Assert.Null(MatchStart.Problem(MatchWorlds.Content, setup with { Tribes = setup.Tribes.Take(Capacities[4]).ToArray() }));
        Assert.NotNull(MatchStart.Problem(MatchWorlds.Content, new MatchSetup()));
        Assert.NotNull(MatchStart.Problem(MatchWorlds.Content, setup with { Map = -1 }));
    }

    [Fact]
    public void AddTribe_UsesTheCampFinderItIsGiven()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 2, MatchWorlds.Controllers(1, 1)));
        GridPoint free = MapCampFinder.Instance.FindCamp(game.World) ?? throw new InvalidOperationException("no room");
        Assert.Equal(-1, game.AddTribe(TribeController.Computer(), TribeState.NoTeam, new NoRoom()));
        Assert.Equal(2, game.World.Tribes.Count);
        int tribe = game.AddTribe(TribeController.Computer(), TribeState.NoTeam, new FixedCamp(free));
        Assert.Equal(free, game.World.Tribes[tribe].Camp);
    }

    private sealed class NoRoom : ICampFinder
    {
        public GridPoint? FindCamp(GameWorld world) => null;
    }

    private sealed class FixedCamp(GridPoint camp) : ICampFinder
    {
        public GridPoint? FindCamp(GameWorld world) => camp;
    }
}
