using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// Construction sites and buildings must always be drawn whole: wherever the camera is, at any
/// zoom and screen size, every part of a building that falls on the screen is drawn. The world
/// view draws a building in two places, and both are checked against a drawing of the whole map
/// at once: its ground (the terrain blocks of 16x16 cells, made the way the view makes them for
/// the cells on screen) and its upright parts (the object layer composed for the cells on screen).
/// </summary>
public sealed class SiteDrawingTests
{
    private const int CellWidth = 22, CellHeight = 16, BlockCells = 16;
    private const int Mission = 7;

    private static readonly (float Width, float Height)[] Screens = { (640, 360), (360, 780) };
    private static readonly float[] Zooms = { 0.5f, 1f, 2f, 6f };

    private readonly ITestOutputHelper output;

    public SiteDrawingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SitesAndBuildings_AreDrawnWhole_AtEveryCameraPlace()
    {
        (int cases, int failures) = CheckAll(ObjectLayer.RowsForWholeBuildings);
        output.WriteLine($"{cases} camera places checked, {failures} drawn incompletely");
        Assert.True(cases > 10000);
        Assert.Equal(0, failures);
    }

    [Fact]
    public void TheOriginalScan_CutsBuildingsOffAtTheTopEdge()
    {
        // The original scans from the row just above the screen, so a building standing above it
        // loses the part of its picture that reaches down onto the screen (the half-drawn site).
        (_, int failures) = CheckAll(0);
        output.WriteLine($"original scan: {failures} drawn incompletely");
        Assert.True(failures > 0);
    }

    [Fact]
    public void TheExtraRows_HoldEveryBuildingThatReachesDownOntoTheScreen()
    {
        Repo repo = Repo.Instance;
        IReadOnlyList<BuildingKind> kinds = repo.Content.Rules.Buildings.Kinds;
        int deepest = 0;
        for (int kind = 0; kind < kinds.Count; kind++)
        {
            for (int level = 1; level <= 3; level++)
            {
                foreach (DrawPart part in repo.Assets.Composers.Objects.Compose(ObjectRequest.Building(kind, level, 1, ObjectPass.Upper)).Drawing.Parts)
                {
                    deepest = Math.Max(deepest, part.Y + part.Height - (kinds[kind].Height * CellHeight));
                }
            }
        }
        output.WriteLine($"pictures reach {deepest} pixels below their footprint");
        // A building whose lowest row is the first row not scanned starts (rows + 2) rows above the screen.
        Assert.True(deepest <= (ObjectLayer.RowsForWholeBuildings + 1) * CellHeight);
    }

    private (int Cases, int Failures) CheckAll(int extraRows)
    {
        Repo repo = Repo.Instance;
        IReadOnlyList<BuildingKind> kinds = repo.Content.Rules.Buildings.Kinds;
        int cases = 0, failures = 0;
        var problems = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var configurations = new List<(int Kind, int Level, int X, int Y)>();
        foreach (int kind in Enumerable.Range(0, 20))
        {
            foreach (int level in kind < 12 ? new[] { 1, 3 } : new[] { 3 })
            {
                // Origins on both sides of terrain block edges, where a half-drawn ground would show.
                foreach ((int ox, int oy) in new[] { (31, 30), (30, 31), (46, 47) })
                {
                    configurations.Add((kind, level, ox, oy));
                }
            }
        }
        System.Threading.Tasks.Parallel.ForEach(configurations, c =>
        {
            // Composers keep caches, so every worker has its own.
            var assets = new SceneAssets(repo.Catalog, new Composers(repo.Catalog), repo.Assets.Fonts);
            var localRasterizer = new PartRasterizer(repo.Sheet);
            var localTerrain = new TerrainLayer(assets);
            GameWorld world = WorldWith(c.Kind, c.Level, c.X, c.Y, kinds[c.Kind]);
            RgbaImage reference = DrawWholeMap(world, localTerrain, localRasterizer);
            var blocks = new Dictionary<(int, int), RgbaImage>();
            var layer = new ObjectLayer(assets) { ExtraRowsAbove = extraRows };
            foreach (ViewRect view in Views(c.X, c.Y, kinds[c.Kind]))
            {
                System.Threading.Interlocked.Increment(ref cases);
                string? problem = Check(world, assets, localTerrain, localRasterizer, reference, blocks, layer, view, c.X, c.Y, kinds[c.Kind]);
                if (problem is not null && System.Threading.Interlocked.Increment(ref failures) <= 20)
                {
                    problems.Enqueue($"kind {c.Kind} level {c.Level} at {c.X},{c.Y} view {view}: {problem}");
                }
            }
        });
        foreach (string problem in problems)
        {
            output.WriteLine(problem);
        }
        return (cases, failures);
    }

    /// <summary>
    /// Screens in cells: 640x360 at zoom 1 and 0.5, 360x780 at zoom 1 (portrait), 640x360 at zoom 6.
    /// </summary>
    private static readonly (int Columns, int Rows)[] ViewSizes = { (30, 23), (59, 46), (17, 49), (5, 4) };

    /// <summary>
    /// The views with the building crossing every edge of the screen by every number of cells
    /// (and a little beyond), and in the corners: the places where a half-drawn building would show.
    /// </summary>
    private static IEnumerable<ViewRect> Views(int ox, int oy, BuildingKind kind)
    {
        var seen = new HashSet<ViewRect>();
        foreach ((int columns, int rows) in ViewSizes)
        {
            var across = new List<int>();
            for (int p = -kind.Width - 3; p <= 3; p++)
            {
                across.Add(p);
                across.Add(columns + p);
            }
            var down = new List<int>();
            for (int q = -kind.Height - 4; q <= 3; q++)
            {
                down.Add(q);
                down.Add(rows + q);
            }
            int middleX = (columns - kind.Width) / 2, middleY = (rows - kind.Height) / 2;
            foreach (int p in across)
            {
                foreach (ViewRect v in new[] { At(p, middleY), At(p, down[0]), At(p, down[^1]) })
                {
                    if (seen.Add(v))
                    {
                        yield return v;
                    }
                }
            }
            foreach (int q in down)
            {
                foreach (ViewRect v in new[] { At(middleX, q), At(across[0], q), At(across[^1], q) })
                {
                    if (seen.Add(v))
                    {
                        yield return v;
                    }
                }
            }

            // The view whose column p and row q hold the building's top-left cell.
            ViewRect At(int p, int q) => new(ox - p, oy - q, columns, rows);
        }
    }

    /// <summary>A world of the mission's map, all seen by the player, with one building of <paramref name="kind"/>.</summary>
    private static GameWorld WorldWith(int kind, int level, int ox, int oy, BuildingKind definition)
    {
        Repo repo = Repo.Instance;
        WorldMap map = MapBuilder.Build(Mission, repo.Content, new GameRandom(1));
        var world = new GameWorld(repo.Content, map, WorldLayout.Original, new GameRandom(1));
        FogOfWar fog = world.Fog(0);
        for (int y = 0; y < fog.Size.Height; y++)
        {
            for (int x = 0; x < fog.Size.Width; x++)
            {
                fog.SetExplored(new GridPoint(x, y), true);
                fog.SetVisible(new GridPoint(x, y), true);
            }
        }
        // Clear the footprint and a ring around it so the building stands on plain ground.
        for (int y = oy - 2; y < oy + definition.Height + 2; y++)
        {
            for (int x = ox - 2; x < ox + definition.Width + 2; x++)
            {
                map.SetCell(new GridPoint(x, y), MapCell.Empty);
                map.SetTile(new GridPoint(x, y), 0);
            }
        }
        Building b = world.Building(1);
        b.X = ox;
        b.Y = oy;
        b.Kind = kind;
        b.Level = level;
        b.MaxHitPoints = definition.MaxHitPoints[Math.Clamp(level - 1, 0, definition.MaxHitPoints.Count - 1)];
        b.HitPoints = Math.Max(1, b.MaxHitPoints);
        b.Visibility = 4;
        if (kind >= 12)
        {
            // A site shows what it becomes: the planned kind built on a site of this kind.
            b.Aux = Enumerable.Range(0, 12).FirstOrDefault(k => repo.Content.Rules.Buildings.Kinds[k].SiteKind == kind, 2);
        }
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                map.SetCell(new GridPoint(ox + x, oy + y), MapCell.BuildingAt(1));
            }
        }
        return world;
    }

    /// <summary>The whole map's terrain drawn at once, the reference for the ground.</summary>
    private static RgbaImage DrawWholeMap(GameWorld world, TerrainLayer terrain, PartRasterizer rasterizer)
    {
        GridSize size = world.Map.PlayableSize;
        var image = new RgbaImage(size.Width * CellWidth, size.Height * CellHeight);
        image.ClearOpaque(0);
        terrain.Draw(world, rasterizer, image, 0, 0, 0, 0, size.Width, size.Height);
        return image;
    }

    /// <summary>What is wrong with the building as drawn for <paramref name="view"/>, or null.</summary>
    private static string? Check(GameWorld world, SceneAssets assets, TerrainLayer terrain, PartRasterizer rasterizer, RgbaImage reference,
        Dictionary<(int, int), RgbaImage> blocks, ObjectLayer layer, ViewRect view, int ox, int oy, BuildingKind definition)
    {
        // Ground: the parts of every footprint cell, which may reach into the cells around.
        (int gx0, int gy0, int gx1, int gy1) = GroundBounds(world, terrain, ox, oy, definition);
        int vx0 = Math.Max(gx0, view.Column * CellWidth), vy0 = Math.Max(gy0, view.Row * CellHeight);
        int vx1 = Math.Min(gx1, (view.Column + view.Columns) * CellWidth), vy1 = Math.Min(gy1, (view.Row + view.Rows) * CellHeight);
        if (vx0 < vx1 && vy0 < vy1)
        {
            for (int y = vy0; y < vy1; y++)
            {
                for (int x = vx0; x < vx1; x++)
                {
                    (int bx, int by) = (FloorDiv(x / CellWidth, BlockCells), FloorDiv(y / CellHeight, BlockCells));
                    if (!InBlockRange(view, bx, by))
                    {
                        return $"ground pixel {x},{y} is in no terrain block the view makes";
                    }
                    if (!blocks.TryGetValue((bx, by), out RgbaImage? block))
                    {
                        block = DrawBlock(world, terrain, rasterizer, bx, by);
                        blocks[(bx, by)] = block;
                    }
                    int lx = x - (bx * BlockCells * CellWidth), ly = y - (by * BlockCells * CellHeight);
                    if (block[lx, ly] != reference[x, y])
                    {
                        return $"ground pixel {x},{y} differs in block {bx},{by}";
                    }
                }
            }
        }
        // Upright parts: the object layer composed for the view must hold every fixed part of the building that is on screen.
        var scene = new SceneList();
        layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(0), false), scene);
        var drawn = new HashSet<(string?, int, int, int, int, int, int)>();
        foreach (SceneItem item in scene.Items)
        {
            DrawPart p = item.Part;
            drawn.Add((p.Sheet, p.SourceX, p.SourceY, p.Width, p.Height, p.X + (view.Column * CellWidth), p.Y + (view.Row * CellHeight)));
        }
        foreach (DrawPart part in FixedUpperParts(world, assets, oy))
        {
            int x = part.X + (ox * CellWidth), y = part.Y + (oy * CellHeight);
            bool onScreen = x + part.Width > view.Column * CellWidth && x < (view.Column + view.Columns) * CellWidth
                && y + part.Height > view.Row * CellHeight && y < (view.Row + view.Rows) * CellHeight;
            if (onScreen && !drawn.Contains((part.Sheet, part.SourceX, part.SourceY, part.Width, part.Height, x, y)))
            {
                return $"upright part {part.Sheet} {part.SourceX},{part.SourceY} at {x},{y} is on screen but not drawn";
            }
        }
        return null;
    }

    /// <summary>The upright parts of building slot 1 that do not change with time (not its animations).</summary>
    private static List<DrawPart> FixedUpperParts(GameWorld world, SceneAssets assets, int oy)
    {
        Building b = world.Building(1);
        Composers composers = assets.Composers;
        ObjectRequest request = ObjectRequest.Building(b.Kind, b.Level, b.Id, ObjectPass.Upper);
        var first = composers.Objects.Compose(request with { Tick = 0, MapRow = oy }).Drawing.Parts;
        var second = composers.Objects.Compose(request with { Tick = 37, MapRow = oy + 3 }).Drawing.Parts;
        var keys = new HashSet<(string?, int, int, int, int, int, int)>(second.Select(p => (p.Sheet, p.SourceX, p.SourceY, p.Width, p.Height, p.X, p.Y)));
        return first.Where(p => p.Kind == DrawPartKind.Image && keys.Contains((p.Sheet, p.SourceX, p.SourceY, p.Width, p.Height, p.X, p.Y))).ToList();
    }

    /// <summary>The pixel rectangle the building's ground covers (all its cells' ground parts).</summary>
    private static (int X0, int Y0, int X1, int Y1) GroundBounds(GameWorld world, TerrainLayer terrain, int ox, int oy, BuildingKind definition)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (int cy = oy; cy < oy + definition.Height; cy++)
        {
            for (int cx = ox; cx < ox + definition.Width; cx++)
            {
                foreach (DrawPart p in terrain.ComposeCell(world, cx, cy) ?? Array.Empty<DrawPart>())
                {
                    x0 = Math.Min(x0, (cx * CellWidth) + p.X);
                    y0 = Math.Min(y0, (cy * CellHeight) + p.Y);
                    x1 = Math.Max(x1, (cx * CellWidth) + p.X + p.Width);
                    y1 = Math.Max(y1, (cy * CellHeight) + p.Y + p.Height);
                }
            }
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>One terrain block as the world view rasterises it (its cells and their margin, clipped to the block).</summary>
    private static RgbaImage DrawBlock(GameWorld world, TerrainLayer terrain, PartRasterizer rasterizer, int bx, int by)
    {
        var block = new RgbaImage(BlockCells * CellWidth, BlockCells * CellHeight);
        block.ClearOpaque(0);
        terrain.Draw(world, rasterizer, block, 0, 0, bx * BlockCells, by * BlockCells, (bx + 1) * BlockCells, (by + 1) * BlockCells);
        return block;
    }

    /// <summary>Whether the world view makes block (<paramref name="bx"/>, <paramref name="by"/>) for <paramref name="view"/> (as WorldView.UpdateTerrain).</summary>
    private static bool InBlockRange(ViewRect view, int bx, int by)
    {
        int bx0 = FloorDiv(view.Column - 1, BlockCells), by0 = FloorDiv(view.Row - 1, BlockCells);
        int bx1 = FloorDiv(view.Column + view.Columns, BlockCells), by1 = FloorDiv(view.Row + view.Rows, BlockCells);
        return bx >= bx0 && bx <= bx1 && by >= by0 && by <= by1;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
