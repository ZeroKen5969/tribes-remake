using System;
using System.IO;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.World.Tests;

/// <summary>A campaign map's extent, fast terrain raster and minimap, on the dev snapshots (built only when the original repository is in original/).</summary>
public sealed partial class EndlessViewTests
{
    [Fact]
    public void ACampaignMapIsItsPartInPlayAndTheCameraShowsAllOfIt()
    {
        Repo repo = Repo.Instance;
        foreach (string snapshot in repo.Snapshots().Take(3))
        {
            (GameWorld world, SnapshotView _) = repo.Load(snapshot);
            GridSize size = world.Map.PlayableSize;
            Assert.False(MapArea.IsChunked(world.Map));
            Assert.Equal(new CellRect(0, 0, size.Width, size.Height), MapArea.Bounds(world.Map));
            Assert.Equal(MapArea.Bounds(world.Map), MapArea.CameraArea(world, 0));
        }
    }

    [Fact]
    public void TheFastTerrainRasterDrawsTheSamePixels()
    {
        Repo repo = Repo.Instance;
        var terrain = new TerrainLayer(repo.Assets);
        var rasterizer = new Tribes.Graphics.Imaging.PartRasterizer(repo.Sheet);
        var raster = new FastRaster(repo.Sheet);
        int blocks = 0;
        foreach (string snapshot in repo.Snapshots().Where((_, i) => i % 9 == 0))
        {
            (GameWorld world, SnapshotView view) = repo.Load(snapshot);
            CellRect bounds = MapArea.Bounds(world.Map);
            for (int by = TerrainTiling.FloorDiv(bounds.Top, 16) - 1; by <= TerrainTiling.FloorDiv(bounds.Bottom, 16); by++)
            {
                for (int bx = TerrainTiling.FloorDiv(bounds.Left, 16) - 1; bx <= TerrainTiling.FloorDiv(bounds.Right, 16); bx++)
                {
                    CellRect cells = TerrainTiling.CellsOf(bx, by);
                    var expected = new Tribes.Graphics.Imaging.RgbaImage(16 * 22, 16 * 16);
                    expected.ClearOpaque(0x123456);
                    terrain.Draw(world, rasterizer, expected, 0, 0, cells.Left, cells.Top, cells.Right, cells.Bottom, view.Seed % 2);
                    var actual = new FastRaster.Target(16 * 22, 16 * 16);
                    actual.ClearOpaque(0x123456);
                    terrain.Draw(world, raster, actual, 0, 0, cells.Left, cells.Top, cells.Right, cells.Bottom, view.Seed % 2);
                    Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), $"{Path.GetFileName(snapshot)} block {bx},{by}");
                    blocks++;
                }
            }
        }
        Assert.True(blocks >= 40, $"only {blocks} blocks compared");
    }

    [Fact]
    public void TheMinimapShowsOwnUnitsYellowAndOthersInTheirTribeColour()
    {
        Repo repo = Repo.Instance;
        (GameWorld world, SnapshotView _) = repo.Load(repo.OriginalReference("snapshots", "s4-f06000.trbw"));
        CellRect bounds = MapArea.Bounds(world.Map);
        MinimapLayout layout = MinimapLayout.Fit(bounds, bounds.Width + 2, bounds.Height + 2, 0, 0);
        Assert.Equal(1, layout.Scale);
        var pixels = new byte[layout.PictureWidth * layout.PictureHeight * 4];
        var painter = new MinimapPainter(tribe => tribe == 1 ? 0x123456u : 0xabcdefu);
        painter.PaintLand(world, 0, layout, pixels, 0, layout.PictureHeight / 2);
        painter.PaintLand(world, 0, layout, pixels, layout.PictureHeight / 2, layout.PictureHeight);
        bool Inside(Unit u) => world.Map.CellAt(new GridPoint(u.X, u.Y)) != MapCell.UnitAt(u.Id);
        painter.PaintMarkers(world, 0, layout, pixels, Inside);
        uint At(int x, int y)
        {
            int i = ((y * layout.PictureWidth) + x) * 4;
            return ((uint)pixels[i] << 16) | ((uint)pixels[i + 1] << 8) | pixels[i + 2];
        }
        int own = 0;
        FogOfWar fog = world.Fog(0);
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            if (unit.HitPoints <= 0 || Inside(unit) || !bounds.Contains(unit.X, unit.Y))
            {
                continue;
            }
            if (unit.Tribe == 0)
            {
                Assert.Equal(MinimapPainter.OwnUnit, At(unit.X, unit.Y));
                own++;
            }
            else if (fog.IsVisible(new GridPoint(unit.X, unit.Y)))
            {
                Assert.Equal(0x123456u, At(unit.X, unit.Y));
            }
        }
        Assert.True(own > 0);
        // Land never explored is earth brown.
        for (int y = 0; y < bounds.Height; y++)
        {
            for (int x = 0; x < bounds.Width; x++)
            {
                if (!fog.IsExplored(new GridPoint(x, y)) && world.Map.CellAt(new GridPoint(x, y)).Content != CellContent.Building)
                {
                    Assert.Equal(MinimapPainter.Unexplored, At(x, y));
                }
            }
        }
        Assert.Equal(0x7a1f13u, MinimapPainter.Darker(0xcc3420));

    }
}
