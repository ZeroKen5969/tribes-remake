using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Xunit;

namespace Tribes.Core.Tests.Endless;

public sealed class ChunkedMapTests
{
    [Fact]
    public void GridRect_EdgesAndContains()
    {
        var rect = new GridRect(-3, 2, 5, 4);

        Assert.Equal(-3, rect.Left);
        Assert.Equal(2, rect.Top);
        Assert.Equal(2, rect.Right);
        Assert.Equal(6, rect.Bottom);
        Assert.True(rect.Contains(new GridPoint(-3, 2)));
        Assert.True(rect.Contains(new GridPoint(1, 5)));
        Assert.False(rect.Contains(new GridPoint(2, 5)));
        Assert.False(rect.Contains(new GridPoint(-4, 3)));
        Assert.False(rect.IsEmpty);
        Assert.True(default(GridRect).IsEmpty);
    }

    [Fact]
    public void GridRect_UnionAndFromCorners()
    {
        GridRect a = GridRect.FromEdges(0, 0, 2, 2);
        GridRect b = GridRect.FromEdges(-5, 1, -4, 9);

        GridRect union = a.Union(b);

        Assert.Equal(GridRect.FromEdges(-5, 0, 2, 9), union);
        Assert.Equal(a, a.Union(default));
        Assert.Equal(b, default(GridRect).Union(b));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(63, 63, 0, 0)]
    [InlineData(64, 0, 1, 0)]
    [InlineData(-1, -1, -1, -1)]
    [InlineData(-64, -65, -1, -2)]
    public void ChunkCoord_Of_UsesFloorDivision(int x, int y, int cx, int cy)
    {
        Assert.Equal(new ChunkCoord(cx, cy), ChunkCoord.Of(new GridPoint(x, y)));
    }

    [Fact]
    public void ChunkCoord_OriginAndCells()
    {
        var chunk = new ChunkCoord(-2, 3);

        Assert.Equal(new GridPoint(-128, 192), chunk.Origin);
        Assert.Equal(new GridRect(-128, 192, WorldMap.ChunkSize, WorldMap.ChunkSize), chunk.Cells);
    }

    [Fact]
    public void Chunked_StartsWithoutLand()
    {
        WorldMap map = WorldMap.Chunked();

        Assert.True(map.IsChunked);
        Assert.True(map.Bounds.IsEmpty);
        Assert.Empty(map.Chunks);
        Assert.False(map.Contains(new GridPoint(0, 0)));
        Assert.False(map.InPlay(new GridPoint(0, 0)));
        Assert.Equal(WorldMap.UngeneratedTile, map.TileAt(new GridPoint(5, -7)));
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(5, -7)).Content);
        Assert.Null(map.Chunk(new ChunkCoord(0, 0)));
    }

    [Fact]
    public void Chunked_AddedChunksHoldCellsAtNegativeCoordinates()
    {
        WorldMap map = WorldMap.Chunked();

        MapChunk a = map.AddChunk(new ChunkCoord(0, 0), generatorVersion: 3);
        MapChunk b = map.AddChunk(new ChunkCoord(-1, -2), generatorVersion: 3);
        map.SetTile(new GridPoint(-1, -65), 42);
        map.SetCell(new GridPoint(-1, -65), MapCell.Of(CellContent.Rocks));
        map.SetTile(new GridPoint(63, 63), 7);

        Assert.Equal(new[] { a, b }, map.Chunks);
        Assert.Same(b, map.Chunk(new ChunkCoord(-1, -2)));
        Assert.Equal(3, b.GeneratorVersion);
        Assert.True(map.InPlay(new GridPoint(-1, -65)));
        Assert.False(map.InPlay(new GridPoint(-1, -1)));
        Assert.Equal(42, map.TileAt(new GridPoint(-1, -65)));
        Assert.Equal(42, b.TileAt(new GridPoint(-1, -65)));
        Assert.Equal(CellContent.Rocks, b.CellAt(new GridPoint(-1, -65)).Content);
        Assert.Equal(7, a.TileAt(new GridPoint(63, 63)));
        Assert.Equal(GridRect.FromEdges(-64, -128, 64, 64), map.Bounds);
        Assert.Equal(new GridSize(128, 192), map.Size);
        Assert.Equal(map.Size, map.PlayableSize);
    }

    [Fact]
    public void Chunked_RevisionGrowsWhenAChunkChanges()
    {
        WorldMap map = WorldMap.Chunked();
        MapChunk chunk = map.AddChunk(new ChunkCoord(2, 2), 1);
        int before = chunk.Revision;

        map.SetCell(new GridPoint(130, 140), MapCell.Of(CellContent.Thicket));
        map.SetTile(new GridPoint(130, 140), 3);

        Assert.True(chunk.Revision >= before + 2);
    }

    [Fact]
    public void Chunked_RefusesWritesToUngeneratedLandAndSecondChunks()
    {
        WorldMap map = WorldMap.Chunked();
        map.AddChunk(new ChunkCoord(0, 0), 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetCell(new GridPoint(-1, 0), MapCell.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SetTile(new GridPoint(64, 0), 1));
        Assert.Throws<InvalidOperationException>(() => map.AddChunk(new ChunkCoord(0, 0), 1));
    }

    [Fact]
    public void Chunked_ResourceNodesHaveNoSlotLimit()
    {
        WorldMap map = WorldMap.Chunked();
        map.AddChunk(new ChunkCoord(0, 0), 1);

        for (int i = 0; i < WorldMap.DefaultNodeCapacity + 20; i++)
        {
            Assert.NotNull(map.PlaceResourceNode(1, new GridPoint(i % 64, i / 64), 5));
        }

        Assert.Equal(WorldMap.DefaultNodeCapacity + 20, map.ResourceNodes.Count(n => n.InUse));
        Assert.Equal(CellContent.ResourceNode, map.CellAt(new GridPoint(69 % 64, 69 / 64)).Content);
    }

    [Fact]
    public void Chunked_ClampKeepsCoordinatesInTheGeneratedBox()
    {
        WorldMap map = WorldMap.Chunked();
        map.AddChunk(new ChunkCoord(-1, 0), 1);
        map.AddChunk(new ChunkCoord(1, 1), 1);

        Assert.Equal(-64, map.ClampColumn(-500));
        Assert.Equal(127, map.ClampColumn(500));
        Assert.Equal(0, map.ClampRow(-3));
        Assert.Equal(127, map.ClampRow(1000));
    }

    [Fact]
    public void Dense_BoundsAreThePartInPlay()
    {
        var map = new WorldMap(new GridSize(96, 96), new GridSize(64, 48));

        Assert.False(map.IsChunked);
        Assert.Equal(new GridRect(0, 0, 64, 48), map.Bounds);
        Assert.Empty(map.Chunks);
        Assert.Equal(0, map.ClampColumn(-4));
        Assert.Equal(63, map.ClampColumn(200));
        Assert.Equal(47, map.ClampRow(48));
        Assert.Null(map.Chunk(new ChunkCoord(0, 0)));
    }
}

public sealed class RoutePlanningReachTests
{
    [Fact]
    public void WithinReach_KeepsNearGoalsAndStepsTowardsFarOnes()
    {
        var from = new GridPoint(10, 10);
        int reach = Tribes.Core.Systems.Paths.RouteFinder.PlanningReach;

        Assert.Equal(new GridPoint(10 + reach, 0), Tribes.Core.Systems.Paths.RouteFinder.WithinReach(from, new GridPoint(10 + reach, 0)));
        Assert.Equal(new GridPoint(10 + reach, 10), Tribes.Core.Systems.Paths.RouteFinder.WithinReach(from, new GridPoint(1010, 10)));
        GridPoint diagonal = Tribes.Core.Systems.Paths.RouteFinder.WithinReach(from, new GridPoint(-990, -490));
        Assert.Equal(new GridPoint(10 - reach, 10 - (reach / 2)), diagonal);
    }
}
