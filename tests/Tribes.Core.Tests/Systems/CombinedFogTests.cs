using System;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Tests.Endless;
using Tribes.Core.Tests.MultiTribe;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// <see cref="CombinedFog"/> (several tribes' fog seen as one, for spectators), <see cref="FogOfWar.AddExplored"/>
/// (allies' explored land) and the bulk flag operation under both.
/// </summary>
public sealed class CombinedFogTests
{
    private static GameWorld MatchWorld(int humans) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 3, MatchWorlds.Controllers(humans, 0))).World;

    private static GameWorld EndlessWorld(int humans) => EndlessWorlds.Start(humans, 0).World;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Combined_IsTheUnionOfTheTribesFog_AndFollowsTheirChanges(bool endless)
    {
        GameWorld world = endless ? EndlessWorld(3) : MatchWorld(3);
        var combined = new CombinedFog(world, [2, 0, 2]);
        Assert.Equal(new[] { 0, 2 }, combined.Tribes);
        Assert.True(combined.Includes(2));
        Assert.False(combined.Includes(1));
        Assert.True(combined.IsVisible(world.Tribes[0].Camp));
        Assert.True(combined.IsVisible(world.Tribes[2].Camp));
        Assert.False(combined.IsExplored(world.Tribes[1].Camp));

        GridPoint cell = UnseenCell(world);
        Assert.False(combined.IsExplored(cell));
        world.Fog(2).SetExplored(cell, true);
        Assert.True(combined.IsExplored(cell));
        Assert.False(combined.IsVisible(cell));
        world.Fog(0).SetVisible(cell, true);
        Assert.True(combined.IsVisible(cell));
        world.Fog(0).ClearVisible();
        Assert.False(combined.IsVisible(cell));
        world.Fog(1).SetVisible(cell, true);
        Assert.False(combined.IsVisible(cell));
        Assert.True(combined.ExploredBounds.Contains(cell));
        Assert.Equal(world.Fog(0).ExploredBounds.Union(world.Fog(2).ExploredBounds), combined.ExploredBounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneTribe_IsReadAsItIs_AndNoneSeesNothing(bool endless)
    {
        GameWorld world = endless ? EndlessWorld(2) : MatchWorld(2);
        var one = new CombinedFog(world, [1]);
        var none = new CombinedFog(world, Array.Empty<int>());
        GridPoint camp = world.Tribes[1].Camp;
        Assert.True(one.IsVisible(camp));
        Assert.Equal(world.Fog(1).ExploredBounds, one.ExploredBounds);
        Assert.False(none.IsExplored(camp));
        Assert.False(none.IsVisible(camp));
        Assert.True(none.ExploredBounds.IsEmpty);
    }

    [Fact]
    public void ATribeNotOfTheWorld_IsRefused()
    {
        GameWorld world = MatchWorld(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombinedFog(world, [0, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombinedFog(world, [-1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddExplored_AddsTheOthersExploredCellsOnly(bool endless)
    {
        GameWorld world = endless ? EndlessWorld(2) : MatchWorld(2);
        FogOfWar mine = world.Fog(0), theirs = world.Fog(1);
        GridPoint cell = UnseenCell(world);
        theirs.SetExplored(cell, true);
        theirs.SetVisible(world.Tribes[0].Camp, true);
        long before = mine.Revision;
        mine.AddExplored(theirs);
        Assert.True(mine.IsExplored(cell));
        Assert.True(mine.IsExplored(world.Tribes[1].Camp));
        Assert.False(mine.IsVisible(world.Tribes[1].Camp));
        Assert.NotEqual(before, mine.Revision);
        long after = mine.Revision;
        mine.AddExplored(theirs);
        Assert.Equal(after, mine.Revision);
    }

    [Fact]
    public void AddExplored_RefusesTheFogOfAnotherMap()
    {
        var small = new FogOfWar(new GridSize(4, 4));
        var large = new FogOfWar(new GridSize(8, 8));
        Assert.Throws<ArgumentException>(() => small.AddExplored(large));
        Assert.Throws<ArgumentException>(() => EndlessWorld(1).Fog(0).AddExplored(small));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(37)]
    public void Or_SetsTheSourcesFlags_AndTellsWhetherAnyWasNew(int length)
    {
        var random = new Random(length);
        bool[] target = new bool[length], source = new bool[length], expected = new bool[length];
        for (int i = 0; i < length; i++)
        {
            target[i] = random.Next(3) == 0;
            source[i] = random.Next(3) == 0;
            expected[i] = target[i] | source[i];
        }
        bool anyNew = !expected.AsSpan().SequenceEqual(target);
        Assert.Equal(anyNew, FogCells.Or(target, source));
        Assert.Equal(expected, target);
        Assert.False(FogCells.Or(target, source));
        Assert.Throws<ArgumentException>(() => FogCells.Or(new bool[3], new bool[4]));
    }

    /// <summary>A cell in play that no tribe has explored.</summary>
    private static GridPoint UnseenCell(GameWorld world)
    {
        GridRect bounds = world.Map.Bounds;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                var cell = new GridPoint(x, y);
                bool seen = false;
                for (int t = 0; t < world.Tribes.Count; t++)
                {
                    seen |= world.Fog(t).IsExplored(cell);
                }
                if (world.Map.InPlay(cell) && !seen)
                {
                    return cell;
                }
            }
        }
        throw new Xunit.Sdk.XunitException("every cell is explored");
    }
}
