using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Graphics.Tests;

/// <summary>
/// Map views captured from the original game (runs identical to DiffHarness): the terrain layer
/// composed from the exported data must equal the game's own redraw and the screenshots.
/// </summary>
public sealed class MapViewTests
{
    private const int Key = 0xFF00FF;
    private const int CellWidth = 22;
    private const int CellHeight = 16;
    private const int ScreenWidth = 240;
    private const int ScreenHeight = 320;
    private const int TopFrameSprite = 524;
    private const int BottomFrameSprite = 528;
    private const int FirstOverlayCode = -123;
    private const int CursorReach = 4;
    private readonly ITestOutputHelper output;

    public MapViewTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static IReadOnlyList<MapShot> Shots { get; } = MapShot.All();

    private static TerrainTileCatalog Tiles { get; } = CatalogJson.Read<TerrainTileCatalog>(TestPaths.Data("terrain-tiles.json"));

    private static RgbaImage Blank(RgbaImage like)
    {
        var image = new RgbaImage(like.Width, like.Height);
        image.ClearOpaque(Key);
        return image;
    }

    private static int Differences(RgbaImage expected, RgbaImage actual)
    {
        int count = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if ((expected[x, y] & 0xFFFFFF) != (actual[x, y] & 0xFFFFFF))
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void CapturesExist()
    {
        Assert.True(Shots.Count >= 6, $"only {Shots.Count} map captures");
    }

    [Fact]
    public void TerrainLayerComposedByTheLibraryEqualsTheGamesRedraw()
    {
        TestAssets assets = TestAssets.Instance;
        foreach (MapShot shot in Shots)
        {
            RgbaImage image = Blank(shot.Terrain);
            var view = new TerrainView(shot.Columns, shot.Rows, shot.TileAt, shot.CameraX, shot.CameraY);
            assets.Rasterizer.Draw(image, assets.Composers.Terrain.ComposeView(view), MapShot.MarginX, MapShot.MarginY);
            Assert.True(Differences(shot.Terrain, image) == 0, $"{shot.Name}: {Differences(shot.Terrain, image)} pixels differ");
        }
        output.WriteLine($"{Shots.Count} map views, {Shots.Sum(s => s.Columns * s.Rows)} cells: identical");
    }

    [Fact]
    public void TerrainLayerDrawnFromTheTileTableEqualsTheGamesRedraw()
    {
        TestAssets assets = TestAssets.Instance;
        foreach (MapShot shot in Shots)
        {
            RgbaImage image = DrawFromTileTable(assets, shot, Blank(shot.Terrain), MapShot.MarginX, MapShot.MarginY);
            Assert.True(Differences(shot.Terrain, image) == 0, $"{shot.Name}: {Differences(shot.Terrain, image)} pixels differ");
        }
    }

    private static RgbaImage DrawFromTileTable(TestAssets assets, MapShot shot, RgbaImage image, int originX, int originY)
    {
        for (int row = 0; row < shot.Rows; row++)
        {
            for (int column = 0; column < shot.Columns; column++)
            {
                TerrainTile tile = Tiles.Tiles[shot.TileAt(shot.CameraX + column, shot.CameraY + row)];
                assets.Rasterizer.Draw(image, tile.Ground, originX + (column * CellWidth), originY + (row * CellHeight));
            }
        }
        return image;
    }

    [Fact]
    public void GroundLayerWithObjectsEqualsTheGamesRedraw()
    {
        TestAssets assets = TestAssets.Instance;
        int objects = 0;
        foreach (MapShot shot in Shots)
        {
            RgbaImage image = Blank(shot.Ground);
            var parts = new List<DrawPart>();
            for (int row = 0; row < shot.Rows; row++)
            {
                for (int column = 0; column < shot.Columns; column++)
                {
                    int x = shot.CameraX + column;
                    int y = shot.CameraY + row;
                    int cellX = column * CellWidth;
                    int cellY = row * CellHeight;
                    parts.AddRange(assets.Composers.Terrain.ComposeTile(shot.TileAt(x, y)).Parts.Select(p => p.Offset(cellX, cellY)));
                    int code = shot.CodeAt(x, y);
                    if (code < 0 && code >= FirstOverlayCode)
                    {
                        (ObjectRequest request, int originX, int originY) = GroundRequest(assets.Catalog, shot, code, x, y);
                        parts.AddRange(assets.Composers.Objects.Compose(request).Drawing.Parts.Select(p => p.Offset(originX, originY)));
                        objects++;
                    }
                }
            }
            assets.Rasterizer.Draw(image, parts, MapShot.MarginX, MapShot.MarginY);
            Assert.True(Differences(shot.Ground, image) == 0, $"{shot.Name}: {Differences(shot.Ground, image)} pixels differ");
        }
        output.WriteLine($"{objects} object cells drawn in {Shots.Count} views: identical");
        Assert.True(objects > 0);
    }

    /// <summary>The terrain-layer request for the object at a grid code, as <c>GameCanvas.o_</c> makes it, and its origin in the view.</summary>
    private static (ObjectRequest Request, int X, int Y) GroundRequest(SpriteCatalog catalog, MapShot shot, int code, int x, int y)
    {
        sbyte[] s = shot.State;
        int viewX = (x - shot.CameraX) * CellWidth;
        int viewY = (y - shot.CameraY) * CellHeight;
        if (code >= -60)
        {
            int slot = -code;
            int kind = s[8749 + slot];
            int bx = s[8383 + slot];
            int by = s[8444 + slot];
            int width = catalog.Tables.Footprints[kind].Width;
            var request = ObjectRequest.Building(kind, s[9115 + slot], slot, ObjectPass.Ground) with
            {
                CellIndex = ((y - by) * width) + (x - bx),
            };
            return (request, (bx - shot.CameraX) * CellWidth, (by - shot.CameraY) * CellHeight);
        }
        if (code >= -110)
        {
            int slot = -61 - code;
            return (ObjectRequest.ResourceNode(s[9833 + slot], s[9933 + slot], slot, ObjectPass.Ground), viewX, viewY);
        }
        int trap = code == -123 ? 23 : WrapTrap(20 - (code + 111));
        bool computer = code <= -117;
        ObjectRequest trapRequest = ObjectRequest.Trap(trap, code == -123 ? 0 : 1, computer && code != -123 ? ObjectPass.Upper : ObjectPass.Ground);
        return (trapRequest, viewX, viewY);
    }

    private static int WrapTrap(int kind)
    {
        while (kind > 22)
        {
            kind -= 3;
        }
        return kind;
    }

    [Fact]
    public void TerrainCellsEqualTheScreenshots()
    {
        TestAssets assets = TestAssets.Instance;
        UiCatalog ui = CatalogJson.Read<UiCatalog>(TestPaths.Data("ui.json"));
        int compared = 0;
        int views = 0;
        foreach (MapShot shot in Shots.Where(s => s.ScrollX == 0 && s.ScrollY == 0))
        {
            views++;
            RgbaImage terrain = DrawFromTileTable(assets, shot, Blank(shot.Terrain), MapShot.MarginX, MapShot.MarginY);
            DrawFog(assets, shot, terrain);
            List<ScreenRect> covered = CoveredAreas(assets, ui, shot);
            int screenTop = shot.TopBar - shot.BarSlide;
            if (!shot.Panels[4])
            {
                Assert.Equal(ui.MapView.Y, screenTop);
            }
            for (int row = 0; row < shot.Rows; row++)
            {
                for (int column = 0; column < shot.Columns; column++)
                {
                    var cell = new ScreenRect(column * CellWidth, screenTop + (row * CellHeight), CellWidth, CellHeight);
                    if (!Inside(cell) || covered.Any(r => Overlaps(r, cell)) || !QuietNeighbourhood(shot, column, row))
                    {
                        continue;
                    }
                    for (int dy = 0; dy < CellHeight; dy++)
                    {
                        for (int dx = 0; dx < CellWidth; dx++)
                        {
                            int expected = terrain[MapShot.MarginX + cell.X + dx, MapShot.MarginY + (row * CellHeight) + dy] & 0xFFFFFF;
                            int actual = shot.Screen[cell.X + dx, cell.Y + dy] & 0xFFFFFF;
                            Assert.True(expected == actual,
                                $"{shot.Name}: cell ({column},{row}) pixel ({dx},{dy}): screen {actual:x6}, tile table {expected:x6}");
                        }
                    }
                    compared++;
                }
            }
        }
        output.WriteLine($"{compared} terrain cells compared in {views} screenshots: identical");
        Assert.True(compared > 100, $"only {compared} cells could be compared");
    }

    /// <summary>
    /// The fog overlays of the object pass (<c>GameCanvas.u_</c>): rows from the top, each row from the
    /// right; half-explored cells get piece 12 at (-2, -4), unexplored cells piece 11 at (-3, -2), or a
    /// black cell when all four neighbours are unexplored too.
    /// </summary>
    private static void DrawFog(TestAssets assets, MapShot shot, RgbaImage image)
    {
        const int HalfFogPiece = 12;
        const int FogEdgePiece = 11;
        const int Black = unchecked((int)0xFF000000);
        for (int row = -1; row < shot.Rows + 3; row++)
        {
            for (int column = shot.Columns; column >= -1; column--)
            {
                int x = shot.CameraX + column;
                int y = shot.CameraY + row;
                if (x < 0 || y < 0 || x >= shot.Width || y >= shot.Height)
                {
                    continue;
                }
                int cellX = MapShot.MarginX + (column * CellWidth);
                int cellY = MapShot.MarginY + (row * CellHeight);
                int fog = shot.FogAt(x, y);
                if (fog == 1)
                {
                    assets.Rasterizer.Draw(image, assets.Composers.Sprites.ComposePiece(HalfFogPiece).Parts, cellX - 2, cellY - 4);
                }
                else if (fog == 2)
                {
                    bool interior = x != 0 && x != shot.Width - 1 && y != 0 && y != shot.Height - 1
                        && shot.FogAt(x - 1, y) == 2 && shot.FogAt(x + 1, y) == 2 && shot.FogAt(x, y - 1) == 2 && shot.FogAt(x, y + 1) == 2;
                    if (interior)
                    {
                        PartRasterizer.FillRect(image, cellX, cellY, CellWidth, CellHeight, Black);
                    }
                    else
                    {
                        assets.Rasterizer.Draw(image, assets.Composers.Sprites.ComposePiece(FogEdgePiece).Parts, cellX - 3, cellY - 2);
                    }
                }
            }
        }
    }

    private static bool Inside(ScreenRect r) => r.X >= 0 && r.Y >= 0 && r.X + r.Width <= ScreenWidth && r.Y + r.Height <= ScreenHeight;

    private static bool Overlaps(ScreenRect a, ScreenRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>The visible panels' areas and frames, which cover the map on the screen.</summary>
    private static List<ScreenRect> CoveredAreas(TestAssets assets, UiCatalog ui, MapShot shot)
    {
        var covered = new List<ScreenRect>();
        int lowestBottom = ScreenHeight;
        foreach (UiPanel panel in ui.Panels.Where(p => shot.Panels[p.Id]))
        {
            covered.Add(panel.Area with { X = 0, Width = ScreenWidth });
            if (panel.Frame == PanelFrame.Top)
            {
                covered.Add(Bounds(assets, TopFrameSprite, 0, shot.TopBar));
            }
            if (panel.Area.Y > ScreenHeight / 2)
            {
                lowestBottom = Math.Min(lowestBottom, panel.Area.Y);
            }
        }
        UiBounds bottom = PartBounds(assets, BottomFrameSprite);
        covered.Add(new ScreenRect(0, lowestBottom - bottom.Height, ScreenWidth, ScreenHeight));
        return covered;
    }

    private static UiBounds PartBounds(TestAssets assets, int sprite) =>
        Tribes.AssetExport.Export.PartBounds.Of(sprite, assets.Composers.Sprites.ComposeSprite(sprite).Parts);

    private static ScreenRect Bounds(TestAssets assets, int sprite, int x, int y)
    {
        UiBounds b = PartBounds(assets, sprite);
        return new ScreenRect(x + b.X, y + b.Y, b.Width, b.Height);
    }

    /// <summary>
    /// No object, unit, overlay tile or cursor near the cell, so the screen shows only its terrain and
    /// fog: the cells left and right, one above and three below (tall things reach up) are empty.
    /// </summary>
    private static bool QuietNeighbourhood(MapShot shot, int column, int row)
    {
        // The cursor, and the placement preview centred on it (up to 6 x 4 cells plus its roof).
        if (Math.Abs(column - shot.CursorX) <= CursorReach && Math.Abs(row - shot.CursorY) <= CursorReach)
        {
            return false;
        }
        for (int dy = -1; dy <= 3; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = shot.CameraX + column + dx;
                int y = shot.CameraY + row + dy;
                if (x < 0 || y < 0 || x >= shot.Width || y >= shot.Height)
                {
                    return false;
                }
                int code = shot.CodeAt(x, y);
                bool terrainMarker = code is 0 or -124 or -125 or -126 or -127;
                if (!terrainMarker || TerrainComposer.HasOverlay(shot.TileAt(x, y)))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
