using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.World.Tests;

/// <summary>The view's extent of a map, terrain tiling and minimap of endless maps, and many tribes.</summary>
public sealed partial class EndlessViewTests
{
    private static readonly Lazy<GameSimulation> Endless = new(() =>
    {
        GameSimulation simulation = GameSimulation.StartEndless(Repo.Instance.Content, new EndlessSetup
        {
            Seed = 11,
            Tribes = new[] { new MatchTribe(TribeController.Human(0)), new MatchTribe(TribeController.Human(1)) },
            ComputerTribes = 6,
            HintsEnabled = false,
        });
        for (int tick = 0; tick < 40; tick++)
        {
            simulation.Step();
        }
        return simulation;
    });

    [Fact]
    public void AnEndlessWorldIsDrawnWholeAroundEveryCampAndAtItsEdges()
    {
        Repo repo = Repo.Instance;
        GameWorld world = Endless.Value.World;
        Assert.True(MapArea.IsChunked(world.Map));
        CellRect bounds = MapArea.Bounds(world.Map);
        Assert.True(bounds.Left < 0 && bounds.Top < 0, $"bounds {bounds}");
        var layer = new ObjectLayer(repo.Assets);
        var terrain = new TerrainLayer(repo.Assets);
        var raster = new FastRaster(repo.Sheet);
        var list = new SceneList();
        var views = new List<ViewRect>();
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            GridPoint camp = world.Tribes[t].Camp;
            views.Add(new ViewRect(camp.X - 15, camp.Y - 12, 31, 24));
        }
        // The corners of the land made so far, half on land not made yet.
        views.Add(new ViewRect(bounds.Left - 10, bounds.Top - 8, 31, 24));
        views.Add(new ViewRect(bounds.Right - 20, bounds.Bottom - 16, 31, 24));
        for (int viewer = 0; viewer < 2; viewer++)
        {
            var looks = new TribeLooks(viewer, world.Layout.Tribes);
            foreach (ViewRect view in views)
            {
                layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, looks, true), list);
                Assert.True(layer.LastFault == null, $"viewer {viewer} view {view}: {layer.LastFault}");
                var block = new FastRaster.Target(view.Columns * 22, view.Rows * 16);
                terrain.Draw(world, raster, block, 0, 0, view.Column, view.Row, view.Column + view.Columns, view.Row + view.Rows, viewer);
            }
        }
        // A block's signature read chunk by chunk is the one of its cells' keys.
        foreach (ViewRect view in views)
        {
            long hash = 17;
            for (int y = view.Row; y < view.Row + view.Rows; y++)
            {
                for (int x = view.Column; x < view.Column + view.Columns; x++)
                {
                    hash = unchecked((hash * 1_000_003) ^ TerrainLayer.CellKey(world, x, y, 1));
                }
            }
            Assert.Equal(hash, TerrainLayer.Signature(world, view.Column, view.Row, view.Column + view.Columns, view.Row + view.Rows, 1));
        }
        // The camera stays over what the viewer explored, which holds its camp.
        CellRect camera = MapArea.CameraArea(world, 0);
        GridPoint own = world.Tribes[0].Camp;
        Assert.True(camera.Contains(own.X, own.Y));
        Assert.True(bounds.Intersect(camera) == camera);
        Assert.True(camera.Width < bounds.Width || camera.Height < bounds.Height);
    }

    [Fact]
    public void TheAllianceHallIsDrawnInEveryState()
    {
        Repo repo = Repo.Instance;
        GameWorld world = Endless.Value.World;
        Building store = Enumerable.Range(1, world.BuildingSlots).Select(world.Building)
            .First(b => b.Stands && world.TribeOfBuilding(b.Id) == 0);
        (int kind, int hp, int max) = (store.Kind, store.HitPoints, store.MaxHitPoints);
        var layer = new ObjectLayer(repo.Assets);
        var list = new SceneList();
        var view = new ViewRect(store.X - 10, store.Y - 8, 24, 20);
        try
        {
            store.Kind = AllianceHall.Kind;
            foreach ((int points, int frame) in new[] { (max, AllianceHall.Built), (Math.Max(1, max / 3), AllianceHall.Damaged), (-1, AllianceHall.Ruin) })
            {
                store.HitPoints = points;
                for (int viewer = 0; viewer < 2; viewer++)
                {
                    layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(viewer, world.Layout.Tribes), true), list);
                    Assert.True(layer.LastFault == null, $"hp {points} viewer {viewer}: {layer.LastFault}");
                    if (!world.Fog(viewer).IsExplored(new GridPoint(store.X, store.Y)))
                    {
                        // Another tribe's hall stays hidden until this tribe has explored it.
                        Assert.DoesNotContain(list.Items, i => i.Part.Sheet == AllianceHall.SheetId);
                        Assert.NotEqual(0, viewer);
                        continue;
                    }
                    SceneItem picture = list.Items.Single(i => i.Part.Sheet == AllianceHall.SheetId);
                    Assert.Equal(frame * repo.Assets.AllianceHall.FrameWidth, picture.Part.SourceX);
                    Assert.Equal((store.X - view.Column) * 22, picture.Part.X);
                    Assert.Equal(((store.Y - view.Row) * 16) - 24, picture.Part.Y);
                }
            }
        }
        finally
        {
            (store.Kind, store.HitPoints) = (kind, hp);
        }
    }

    [Fact]
    public void TheEndlessMinimapPaintsTheExploredLandAndTheOwnCamp()
    {
        GameWorld world = Endless.Value.World;
        CellRect area = MapArea.CameraArea(world, 0);
        GridPoint camp = world.Tribes[0].Camp;
        MinimapLayout layout = MinimapLayout.Fit(area, 120, 90, camp.X, camp.Y);
        var pixels = new byte[layout.PictureWidth * layout.PictureHeight * 4];
        var painter = new MinimapPainter(_ => 0x102030u);
        painter.PaintLand(world, 0, layout, pixels, 0, layout.PictureHeight);
        painter.PaintMarkers(world, 0, layout, pixels, u => world.Map.CellAt(new GridPoint(u.X, u.Y)) != MapCell.UnitAt(u.Id));
        var colours = new HashSet<uint>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Assert.Equal(255, pixels[i + 3]);
            colours.Add(((uint)pixels[i] << 16) | ((uint)pixels[i + 1] << 8) | pixels[i + 2]);
        }
        Assert.Contains(MinimapPainter.OwnBuilding, colours);
        Assert.Contains(MinimapPainter.OwnUnit, colours);
        Assert.True(colours.Overlaps(MinimapPainter.Terrain), "no land seen now");
    }

    [Fact]
    public void CellRectanglesWorkWithNegativeCells()
    {
        var a = new CellRect(-10, -5, 4, 3);
        Assert.Equal(-6, a.Right);
        Assert.Equal(-2, a.Bottom);
        Assert.True(a.Contains(-10, -5));
        Assert.True(a.Contains(-7, -3));
        Assert.False(a.Contains(-6, -3));
        Assert.Equal(new CellRect(-10, -5, 16, 8), a.Union(new CellRect(0, 0, 6, 3)));
        Assert.Equal(new CellRect(-8, -4, 2, 2), a.Intersect(new CellRect(-8, -4, 10, 10)));
        Assert.True(a.Intersect(new CellRect(0, 0, 5, 5)).IsEmpty);
        Assert.Equal(new CellRect(-12, -7, 8, 7), a.Inflate(2));
        Assert.True(CellRect.Empty.Inflate(3).IsEmpty);
        Assert.Equal(a, a.Union(CellRect.Empty));
        Assert.Equal(new CellRect(3, 4, 0, 0), CellRect.FromEdges(3, 4, 1, 2));
    }

    [Fact]
    public void TheCameraOfAnEndlessMapStaysOverTheExploredLand()
    {
        var bounds = new CellRect(-128, -64, 320, 192);
        var explored = new CellRect(-20, -10, 30, 25);
        Assert.Equal(new CellRect(-24, -14, 38, 33), MapArea.CameraArea(bounds, explored, chunked: true, margin: 4));
        // Never beyond the land made so far.
        Assert.Equal(CellRect.FromEdges(-128, -64, -100, -30), MapArea.CameraArea(bounds, new CellRect(-127, -63, 23, 29), true, 4));
        // Nothing explored yet (a viewer without fog): the whole map.
        Assert.Equal(bounds, MapArea.CameraArea(bounds, CellRect.Empty, true, 4));
        // A campaign map ignores what is explored.
        Assert.Equal(bounds, MapArea.CameraArea(bounds, explored, chunked: false, margin: 4));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(-1, -1)]
    [InlineData(-16, -1)]
    [InlineData(-17, -2)]
    [InlineData(-64, -4)]
    public void BlocksAreCountedFromCellZeroInBothDirections(int cell, int block)
    {
        Assert.Equal(block, TerrainTiling.BlockOf(cell, cell).X);
        Assert.Equal(block, TerrainTiling.BlockOf(cell, cell).Y);
        Assert.True(TerrainTiling.CellsOf(block, block).Contains(cell, cell));
        Assert.Equal(block, TerrainTiling.FloorDiv(cell, TerrainTiling.BlockCells));
        Assert.Equal((int)Math.Floor(cell / 16.0), TerrainTiling.FloorDiv(cell, 16));
    }

    [Fact]
    public void BlocksTileAChunkExactly()
    {
        Assert.Equal(0, TerrainTiling.ChunkCells % TerrainTiling.BlockCells);
        for (int by = -6; by < 6; by++)
        {
            for (int bx = -6; bx < 6; bx++)
            {
                CellRect cells = TerrainTiling.CellsOf(bx, by);
                int chunkX = TerrainTiling.FloorDiv(cells.Left, TerrainTiling.ChunkCells);
                int chunkY = TerrainTiling.FloorDiv(cells.Top, TerrainTiling.ChunkCells);
                var chunk = new CellRect(chunkX * TerrainTiling.ChunkCells, chunkY * TerrainTiling.ChunkCells, TerrainTiling.ChunkCells, TerrainTiling.ChunkCells);
                Assert.Equal(cells, cells.Intersect(chunk));
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 18, 12)]
    [InlineData(-37, -21, 50, 17)]
    [InlineData(-16, -16, 16, 16)]
    [InlineData(-1, -1, 1, 1)]
    public void TheBlocksForAViewCoverItAndTheCellAroundIt(int column, int row, int columns, int rows)
    {
        var view = new ViewRect(column, row, columns, rows);
        (int x0, int y0, int x1, int y1) = TerrainTiling.BlocksFor(view);
        for (int y = row - 1; y <= row + rows; y++)
        {
            for (int x = column - 1; x <= column + columns; x++)
            {
                (int bx, int by) = TerrainTiling.BlockOf(x, y);
                Assert.InRange(bx, x0, x1);
                Assert.InRange(by, y0, y1);
            }
        }
        // No block of the range lies wholly outside the view and its border.
        var covered = new CellRect(column - 1, row - 1, columns + 2, rows + 2);
        Assert.False(TerrainTiling.CellsOf(x0, y0).Intersect(covered).IsEmpty);
        Assert.False(TerrainTiling.CellsOf(x1, y1).Intersect(covered).IsEmpty);
    }

    [Fact]
    public void BlocksAheadRingTheViewNearestFirst()
    {
        var view = new ViewRect(-40, -30, 30, 20);
        (int x0, int y0, int x1, int y1) = TerrainTiling.BlocksFor(view);
        int width = x1 - x0 + 1, height = y1 - y0 + 1;
        IReadOnlyList<(int X, int Y)> ahead = TerrainTiling.AheadOf(view, 1);
        Assert.Equal(((width + 2) * (height + 2)) - (width * height), ahead.Count);
        Assert.Equal(ahead.Count, ahead.Distinct().Count());
        Assert.All(ahead, b => Assert.False(b.X >= x0 && b.X <= x1 && b.Y >= y0 && b.Y <= y1));
        Assert.All(ahead, b => Assert.True(b.X >= x0 - 1 && b.X <= x1 + 1 && b.Y >= y0 - 1 && b.Y <= y1 + 1));
        int Distance((int X, int Y) b) => Math.Abs((2 * b.X) - (x0 + x1)) + Math.Abs((2 * b.Y) - (y0 + y1));
        for (int i = 1; i < ahead.Count; i++)
        {
            Assert.True(Distance(ahead[i - 1]) <= Distance(ahead[i]));
        }
    }

    [Fact]
    public void TheBlocksKeptStayBoundedByWhatIsOnScreen()
    {
        Assert.Equal(32, TerrainTiling.BlocksKept(4));
        Assert.Equal(120, TerrainTiling.BlocksKept(60));
        Assert.True(TerrainTiling.BlocksKept(1000) <= 2 * 1000);
    }

    [Fact]
    public void ASmallExploredAreaFillsTheMinimapByWholePixels()
    {
        var area = new CellRect(-30, -20, 40, 30);
        MinimapLayout layout = MinimapLayout.Fit(area, 162, 122, -10, -5);
        Assert.Equal(area, layout.Window);
        Assert.Equal(1, layout.CellsPerPixel);
        Assert.Equal(4, layout.Scale);
        Assert.Equal((40, 30), (layout.PictureWidth, layout.PictureHeight));
        Assert.Equal((1, 1), (layout.DrawX, layout.DrawY));
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                (double px, double py) = layout.PointOf(x + 0.5, y + 0.5);
                Assert.Equal((x, y), layout.CellAt(px, py));
                Assert.Equal((x, y), layout.CellOfPixel(layout.PixelOfCell(x, y)!.Value.X, layout.PixelOfCell(x, y)!.Value.Y));
            }
        }
        Assert.Equal((area.Left, area.Top), layout.CellAt(-50, -50));
        Assert.Equal((area.Right - 1, area.Bottom - 1), layout.CellAt(5000, 5000));
    }

    [Fact]
    public void ALargerAreaSkipsCellsSoThePictureStaysTheMinimapsSize()
    {
        var area = new CellRect(-150, -100, 250, 180);
        MinimapLayout layout = MinimapLayout.Fit(area, 162, 122, 0, 0);
        Assert.Equal(area, layout.Window);
        Assert.Equal(2, layout.CellsPerPixel);
        Assert.Equal(1, layout.Scale);
        Assert.InRange(layout.PictureWidth, 1, 160);
        Assert.InRange(layout.PictureHeight, 1, 120);
        (int x, int y) = layout.CellOfPixel(10, 20);
        Assert.Equal((area.Left + 21, area.Top + 41), (x, y));
        Assert.Equal((10, 20), layout.PixelOfCell(x, y));
        Assert.Null(layout.PixelOfCell(area.Right, area.Top));
    }

    [Fact]
    public void AHugeAreaShowsThePartAroundTheCamera()
    {
        var area = new CellRect(-3000, -2000, 6000, 4000);
        MinimapLayout layout = MinimapLayout.Fit(area, 162, 122, 500, -700);
        Assert.Equal(new CellRect(500 - 160, -700 - 120, 320, 240), layout.Window);
        Assert.Equal(2, layout.CellsPerPixel);
        Assert.True(layout.Window.Contains(500, -700));
        // Near the edge of the land the window stops at it.
        MinimapLayout edge = MinimapLayout.Fit(area, 162, 122, -2990, 1995);
        Assert.Equal(area.Left, edge.Window.Left);
        Assert.Equal(area.Bottom, edge.Window.Bottom);
        // A tap goes to the cell under it.
        (double px, double py) = layout.PointOf(512.5, -650.5);
        Assert.Equal((512, -651), layout.CellAt(px, py));
    }

    /// <summary>The original's trap classes of a cell code (GameCanvas.u_), for checking.</summary>
    private static int OriginalTrapClass(int code) => code switch
    {
        <= -111 and >= -116 => 0,
        <= -117 and >= -122 => 1,
        -123 => 3,
        _ => 2,
    };

    private static int OriginalTrapKind(int code)
    {
        int kind = 20 - (code + 111);
        while (kind > 22)
        {
            kind -= 3;
        }
        return kind;
    }

    [Fact]
    public void TheViewerOfAnOriginalWorldSortsTrapsLikeTheOriginal()
    {
        WorldLayout layout = WorldLayout.Original;
        for (int code = sbyte.MinValue; code <= sbyte.MaxValue; code++)
        {
            Assert.Equal(OriginalTrapClass(code), CellClasses.OfCode(code, 0));
            if (code <= -111 && code >= -122)
            {
                Assert.Equal(OriginalTrapKind(code), CellClasses.KindOfCode(code));
            }
            MapCell cell;
            try
            {
                cell = CellCode.FromCode(code, layout);
            }
            catch (System.IO.InvalidDataException)
            {
                continue;
            }
            Assert.Equal(OriginalTrapClass(CellCode.ToCode(cell)), CellClasses.Of(cell, 0));
        }
    }

    [Fact]
    public void EveryTribeSeesItsOwnTrapsAndNotTheOthers()
    {
        for (int tribe = 0; tribe < 40; tribe++)
        {
            for (int kind = 20; kind <= 22; kind++)
            {
                int code = CellCode.Trap(tribe, kind);
                var cell = new MapCell(CellContent.Trap, kind, tribe);
                Assert.Equal(kind, CellClasses.KindOfCode(code));
                Assert.Equal(CellClasses.OwnTrap, CellClasses.OfCode(code, tribe));
                Assert.Equal(CellClasses.OwnTrap, CellClasses.Of(cell, tribe));
                Assert.Equal(CellClasses.OtherTrap, CellClasses.OfCode(code, tribe + 1));
                Assert.Equal(CellClasses.OtherTrap, CellClasses.Of(cell, tribe + 1));
            }
            Assert.Equal(CellClasses.PendingTrap, CellClasses.Of(new MapCell(CellContent.PendingTrap, 0, tribe), tribe));
            Assert.Equal(CellClasses.NotATrap, CellClasses.Of(new MapCell(CellContent.PendingTrap, 0, tribe), tribe + 1));
        }
    }

    [Fact]
    public void ManyTribesUseEveryColourBeforeAnyRepeats()
    {
        var looks = new TribeLooks(0, 36);
        Assert.True(TribeLooks.IsValid(Enumerable.Range(0, 36).Select(looks.ColourOf).ToArray()));
        Assert.Equal(8, Enumerable.Range(0, 36).Select(looks.ColourOf).Distinct().Count());
        int[] chosen = Enumerable.Range(0, 36).Select(t => (t + 3) % 8).ToArray();
        Assert.True(looks.TrySetColours(chosen));
        Assert.Equal(3, looks.ColourOf(0));
        int[] missingOne = Enumerable.Range(0, 36).Select(t => t % 7).ToArray();
        Assert.False(looks.TrySetColours(missingOne));
        Assert.Equal(3, looks.ColourOf(0));
        Assert.False(TribeLooks.IsValid(new[] { 1, 1, 2 }));
        Assert.True(TribeLooks.IsValid(new[] { 1, 0, 2 }));
    }

    [Fact]
    public void TribesJoiningGetFreeColoursAndTheOthersKeepTheirs()
    {
        var looks = new TribeLooks(1, 2);
        Assert.True(looks.TrySetColours(new[] { 5, 2 }));
        TribeLooks grown = looks.Grow(4);
        Assert.Equal(1, grown.Viewer);
        Assert.Equal(4, grown.Tribes);
        Assert.Equal(new[] { 5, 2, 0, 3 }, Enumerable.Range(0, 4).Select(grown.ColourOf));
        TribeLooks many = grown.Grow(20);
        int[] colours = Enumerable.Range(0, 20).Select(many.ColourOf).ToArray();
        Assert.True(TribeLooks.IsValid(colours));
        Assert.Equal(new[] { 5, 2, 0, 3 }, colours.Take(4));
    }

    [Fact]
    public void UnitsOfMoreTribesGetTheirOwnFloatingNumbers()
    {
        var labels = new FloatingLabels();
        Assert.Equal(7, FloatingLabels.UnitKey(7));
        Assert.Equal(101 + 4, FloatingLabels.AnimalKey(4));
        Assert.NotEqual(FloatingLabels.UnitKey(105), FloatingLabels.AnimalKey(4));
        labels.Add(FloatingLabels.UnitKey(105), -3);
        labels.Add(FloatingLabels.AnimalKey(4), -2);
        labels.Add(FloatingLabels.UnitKey(2500), 5);
        for (int frame = 0; frame < 3; frame++)
        {
            labels.Step(frame);
        }
        var shown = new Dictionary<int, string>();
        for (int k = labels.First; k != labels.End; k = (k + 1) % FloatingLabels.RingSize)
        {
            shown[labels.Ring[k].Key] = labels.Ring[k].Text;
        }
        Assert.Equal("-3", shown[FloatingLabels.UnitKey(105)]);
        Assert.Equal("-2", shown[FloatingLabels.AnimalKey(4)]);
        Assert.Equal("+5", shown[FloatingLabels.UnitKey(2500)]);
    }

    [Fact]
    public void TheAllianceHallShowsHowItStands()
    {
        Assert.Equal(AllianceHall.Built, AllianceHall.FrameOf(300, 300));
        Assert.Equal(AllianceHall.Built, AllianceHall.FrameOf(150, 300));
        Assert.Equal(AllianceHall.Damaged, AllianceHall.FrameOf(149, 300));
        Assert.Equal(AllianceHall.Damaged, AllianceHall.FrameOf(0, 300));
        Assert.Equal(AllianceHall.Ruin, AllianceHall.FrameOf(-1, 300));
        AllianceHallArt art = AllianceHallArt.Placeholder();
        Assert.Equal(AllianceHallArt.StandardWidth * AllianceHallArt.Frames, art.Sheet.Width);
        for (int frame = 0; frame < AllianceHallArt.Frames; frame++)
        {
            int opaque = 0;
            for (int y = 0; y < art.FrameHeight; y++)
            {
                for (int x = 0; x < art.FrameWidth; x++)
                {
                    opaque += (art.Sheet[(frame * art.FrameWidth) + x, y] >>> 24) == 0xFF ? 1 : 0;
                }
            }
            Assert.True(opaque > 300, $"frame {frame} has {opaque} pixels");
        }
        Assert.Throws<ArgumentException>(() => AllianceHallArt.FromSheet(new Tribes.Graphics.Imaging.RgbaImage(66, 72)));
    }
}
