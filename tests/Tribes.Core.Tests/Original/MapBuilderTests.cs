using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Legacy;
using Xunit;

namespace Tribes.Core.Tests;

public sealed class MapBuilderTests
{
    private const int Grass = 10;
    private const int Variant = 1;
    private const int Wall = 40;
    private const int Rock = 63;
    private const int Bush = 175;
    private const int Pond = 179;
    private const int Thicket = 200;
    private const int RockyThicket = 201;
    private const int RockyBush = 176;
    private const int ShoreTile = 33;

    private static readonly int[][] Room =
    {
        new[] { Grass, Grass, Grass, Grass, Grass },
        new[] { Grass, Variant, Rock, Thicket, Grass },
        new[] { Grass, Bush, Wall, Grass, Grass },
        new[] { Grass, Grass, Grass, Grass, Grass },
    };

    /// <summary>Content with one map made of <paramref name="rows"/>; tiles as the constants above.</summary>
    private static GameContent SmallContent(int[][] rows, bool terrainObjects = true,
        GridSize? playable = null)
    {
        var size = new GridSize(rows[0].Length, rows.Length);
        byte[] tiles = rows.SelectMany(r => r).Select(t => (byte)t).ToArray();
        var kinds = Enumerable.Range(0, 256).Select(id => new TileKind(id,
            BlocksMovement: id == Wall || id == Rock || id == ShoreTile || id >= Bush,
            IsRock: id is Rock or RockyThicket or RockyBush));
        var terrain = new TerrainCatalog(kinds, new TileRange(0, 8), 3, new TileRange(175, 179),
            new TileRange(180, 231), new[] { ShoreTile });
        var mission = new MissionDefinition(0, 0, playable ?? size, new GridPoint(0, 0), terrainObjects);
        var nodes = Enumerable.Range(1, 5).Select(k => new ResourceNodeKind(k, 10 * k, MarksShore: k == 5));
        return new GameContent(new[] { mission }, new[] { new MapDefinition(0, size, tiles) }, terrain, nodes);
    }

    private static WorldMap Build(GameContent content) => MapBuilder.Build(0, content, new GameRandom(1));

    private static List<ResourceNode> UsedNodes(WorldMap map) => map.ResourceNodes.Where(n => n.InUse).ToList();

    [Fact]
    public void Variants_DrawOncePerCellAndStayInRange()
    {
        GameContent content = SmallContent(Room);
        var random = new GameRandom(5);

        WorldMap map = MapBuilder.Build(0, content, random);

        var expected = new GameRandom(5);
        expected.NextMasked(3, 3);
        Assert.Equal(expected.State, random.State);
        Assert.InRange(map.TileAt(new GridPoint(1, 1)), Variant, Variant + 2);
        Assert.Equal(Grass, map.TileAt(new GridPoint(0, 1)));
    }

    [Fact]
    public void Edges_AreTopBottomAndRight()
    {
        WorldMap map = Build(SmallContent(Room));

        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(2, 0)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(2, 3)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(4, 1)).Content);
        Assert.Equal(CellContent.Empty, map.CellAt(new GridPoint(0, 1)).Content);
    }

    [Fact]
    public void TerrainObjects_ArePlaced()
    {
        WorldMap map = Build(SmallContent(Room));

        Assert.Equal(CellContent.Rocks, map.CellAt(new GridPoint(2, 1)).Content);
        Assert.Equal(CellContent.Thicket, map.CellAt(new GridPoint(3, 1)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(2, 2)).Content);
        ResourceNode bush = Assert.Single(UsedNodes(map));
        Assert.Equal((0, 1, new GridPoint(1, 2), 10), (bush.Index, bush.Kind, bush.Position, bush.Amount));
        Assert.Equal(MapCell.Node(0), map.CellAt(new GridPoint(1, 2)));
    }

    [Fact]
    public void ThicketsAndNodes_OverrideTheRockMark()
    {
        int[][] rows =
        {
            new[] { Grass, Grass, Grass, Grass },
            new[] { Grass, RockyThicket, RockyBush, Grass },
            new[] { Grass, Grass, Grass, Grass },
        };

        WorldMap map = Build(SmallContent(rows));

        Assert.Equal(CellContent.Thicket, map.CellAt(new GridPoint(1, 1)).Content);
        Assert.Equal(CellContent.ResourceNode, map.CellAt(new GridPoint(2, 1)).Content);
    }

    [Fact]
    public void NodesOnTheEdge_KeepTheirSlotButTheCellIsEdge()
    {
        int[][] rows =
        {
            new[] { Grass, Bush, Grass, Grass },
            new[] { Grass, Grass, Grass, Pond },
            new[] { Grass, Grass, Grass, Grass },
        };

        WorldMap map = Build(SmallContent(rows));

        Assert.Equal(new[] { 1, 5 }, UsedNodes(map).Select(n => n.Kind));
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(1, 0)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(3, 1)).Content);
    }

    [Fact]
    public void Nodes_TakeSlotsInRowOrderWithTheirKindsAmount()
    {
        int[][] rows =
        {
            new[] { Grass, Grass, Grass, Grass, Grass },
            new[] { Grass, Pond, Grass, Bush, Grass },
            new[] { Grass, RockyBush, Grass, Grass, Grass },
            new[] { Grass, Grass, Grass, Grass, Grass },
        };

        WorldMap map = Build(SmallContent(rows));

        Assert.Equal(new[] { (0, 5, 50), (1, 1, 10), (2, 2, 20) },
            UsedNodes(map).Select(n => (n.Index, n.Kind, n.Amount)));
    }

    [Fact]
    public void BareTerrainMission_HasNoTerrainObjects()
    {
        WorldMap map = Build(SmallContent(Room, terrainObjects: false));

        // Without terrain objects the tiles still block as terrain, but nothing is placed on them.
        Assert.Empty(UsedNodes(map));
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(2, 1)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(3, 1)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(1, 2)).Content);
    }

    [Fact]
    public void ShoreTiles_BesideShoreMarkingNodes_BecomeShore()
    {
        int[][] lake =
        {
            new[] { Grass, Grass, Grass, Grass, Grass, Grass, Grass },
            new[] { Grass, ShoreTile, Pond, ShoreTile, Bush, ShoreTile, Grass },
            new[] { Grass, Grass, Grass, Grass, Grass, Grass, Grass },
        };

        WorldMap map = Build(SmallContent(lake));

        Assert.Equal(CellContent.Shore, map.CellAt(new GridPoint(1, 1)).Content);
        Assert.Equal(CellContent.Shore, map.CellAt(new GridPoint(3, 1)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(5, 1)).Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Shores_AreOnlyMarkedForNodesInsideThePlayableArea(int pondColumn)
    {
        int[] middle = Enumerable.Repeat(ShoreTile, 6).ToArray();
        middle[pondColumn] = Pond;
        int[][] lake =
        {
            Enumerable.Repeat(Grass, 6).ToArray(),
            middle,
            Enumerable.Repeat(Grass, 6).ToArray(),
        };

        // Playable width 4: shore marking looks at columns 1..2 only.
        WorldMap map = Build(SmallContent(lake, playable: new GridSize(4, 3)));

        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(1, 1)).Content);
        Assert.Equal(CellContent.Impassable, map.CellAt(new GridPoint(2, 1)).Content);
    }

    [Fact]
    public void FullNodeSlots_AreReported()
    {
        int[][] rows =
        {
            Enumerable.Repeat(Grass, 53).ToArray(),
            new[] { Grass }.Concat(Enumerable.Repeat(Bush, 51)).Append(Grass).ToArray(),
            Enumerable.Repeat(Grass, 53).ToArray(),
        };

        Assert.Throws<InvalidOperationException>(() => Build(SmallContent(rows)));
    }

    [Fact]
    public void WorldMap_RejectsEmptySize() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(new GridSize(0, 4), new GridSize(0, 4)));

    [Theory]
    [InlineData(5, 4)]
    [InlineData(-1, 2)]
    public void WorldMap_RejectsPlayableAreaOutsideTheMap(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldMap(new GridSize(4, 4), new GridSize(width, height)));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 4)]
    public void CellsOutsideTheMap_AreRejected(int x, int y)
    {
        WorldMap map = Build(SmallContent(Room));

        Assert.Throws<ArgumentOutOfRangeException>(() => map.CellAt(new GridPoint(x, y)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.TileAt(new GridPoint(x, y)));
    }

    [Fact]
    public void DefaultCell_IsEmpty() => Assert.Equal(MapCell.Empty, default(MapCell));
}

/// <summary>The new start maps against the original game's, cell by cell.</summary>
public sealed class MapBuilderLegacyTests
{
    private static readonly string Root = TestPaths.RepoRoot();
    private static readonly GameContent Content = GameContent.Load(Path.Combine(Root, "data"));

    public static IEnumerable<object[]> Cases() =>
        from mission in Content.Missions.Keys.Order()
        from seed in new long[] { 1, 77, 123456789 }
        select new object[] { mission, seed };

    [Theory]
    [MemberData(nameof(Cases))]
    public void StartMap_MatchesTheOriginal(int missionId, long seed)
    {
        GameCanvas original = Tribes.Trace.Program.RunTo(Path.Combine(Root, "original", "game", "res"), 300, 0L,
            int.MaxValue);
        long state = new GameRandom(seed).State;
        original.b_q = (sbyte)missionId;
        original.aM_();
        original.rnd_a.State = state;
        original.aN_();

        var random = new GameRandom(0) { State = state };
        WorldMap map = MapBuilder.Build(missionId, Content, random);

        Assert.Equal(original.rnd_a.State, random.State);
        for (int y = 0; y < map.Size.Height; y++)
        {
            for (int x = 0; x < map.Size.Width; x++)
            {
                var cell = new GridPoint(x, y);
                Assert.True(original.bArr2_b[y][x] == unchecked((sbyte)map.TileAt(cell)),
                    $"tile ({x},{y}): original {original.bArr2_b[y][x] & 0xFF}, new {map.TileAt(cell)}");
                Assert.True(original.bArr2_c[y][x] == OriginalCode(map.CellAt(cell)),
                    $"cell ({x},{y}): original {original.bArr2_c[y][x]}, new {map.CellAt(cell)}");
            }
        }
        Assert.Equal(50, map.ResourceNodes.Count);
        foreach (ResourceNode node in map.ResourceNodes)
        {
            int i = node.Index;
            Assert.Equal(node.Kind, original.bArr_e[9833 + i]);
            Assert.Equal(node.X, original.bArr_e[9733 + i]);
            Assert.Equal(node.Y, original.bArr_e[9783 + i]);
            Assert.Equal(node.Amount, original.bArr_e[9883 + i]);
        }
    }

    /// <summary>How the original game encodes a cell on its occupancy grid.</summary>
    private static sbyte OriginalCode(MapCell cell) => cell.Content switch
    {
        CellContent.Empty => 0,
        CellContent.Impassable => -126,
        CellContent.Rocks => -125,
        CellContent.Thicket => -124,
        CellContent.Shore => -127,
        CellContent.ResourceNode => (sbyte)(-61 - cell.Index),
        _ => throw new InvalidDataException(cell.ToString()),
    };
}
