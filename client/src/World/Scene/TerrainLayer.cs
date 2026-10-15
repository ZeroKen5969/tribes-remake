using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The original's terrain layer (GameCanvas.o_): each cell's tile, then the ground part of the
/// building, node or trap on it. Cells outside the map (on an endless map: land not made yet) are
/// black. Cells are drawn in rows from the top, each row from the left, so a part reaching into the
/// next cells is covered by them. Traps show as the viewing tribe sees them (see <see cref="CellClasses"/>).
/// </summary>
public sealed class TerrainLayer
{
    /// <summary>Cells drawn around a block so that parts reaching into it are included.</summary>
    public const int Margin = 2;

    private static readonly int BlackArgb = unchecked((int)0xFF000000);

    private readonly SceneAssets assets;
    private readonly Composers composers;
    private readonly Dictionary<int, IReadOnlyList<DrawPart>> tiles = new();

    /// <summary>Creates the layer drawing with <paramref name="assets"/>.</summary>
    public TerrainLayer(SceneAssets assets)
    {
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        composers = assets.Composers;
    }

    /// <summary>
    /// The parts of map cell (<paramref name="x"/>, <paramref name="y"/>), relative to the cell's
    /// top-left corner, as tribe <paramref name="viewer"/> sees it; null for a cell outside the map (drawn black).
    /// </summary>
    public IReadOnlyList<DrawPart>? ComposeCell(GameWorld world, int x, int y, int viewer = 0) => ComposeCell(world, x, y, new PlayerJudge(viewer));

    private List<DrawPart>? ComposeCell<TJudge>(GameWorld world, int x, int y, TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!InMap(world, x, y))
        {
            return null;
        }
        var cell = new GridPoint(x, y);
        var parts = new List<DrawPart>(composers.Terrain.ComposeTile(world.Map.TileAt(cell)).Parts);
        MapCell content = world.Map.CellAt(cell);
        if (GroundRequest(world, content, x, y, viewer) is var (request, originX, originY))
        {
            foreach (DrawPart part in composers.Objects.Compose(request).Drawing.Parts)
            {
                parts.Add(part.Offset(originX, originY));
            }
        }
        return parts;
    }

    /// <summary>
    /// A number that changes when the cell's terrain picture changes: its tile and content, and
    /// for buildings and nodes the level or stage shown, for traps their kind and whose they are.
    /// </summary>
    public static long CellKey(GameWorld world, int x, int y, int viewer = 0) => CellKey(world, x, y, new PlayerJudge(viewer));

    private static long CellKey<TJudge>(GameWorld world, int x, int y, TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!InMap(world, x, y))
        {
            return -1;
        }
        var cell = new GridPoint(x, y);
        return KeyOf(world, x, y, world.Map.TileAt(cell), world.Map.CellAt(cell), viewer);
    }

    /// <summary>
    /// The number of <see cref="CellKey"/> for land cell (<paramref name="x"/>, <paramref name="y"/>)
    /// with <paramref name="tile"/> and <paramref name="content"/> already read.
    /// </summary>
    public static long KeyOf(GameWorld world, int x, int y, int tile, MapCell content, int viewer) =>
        KeyOf(world, x, y, tile, content, new PlayerJudge(viewer));

    private static long KeyOf<TJudge>(GameWorld world, int x, int y, int tile, MapCell content, TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        ArgumentNullException.ThrowIfNull(world);
        // Only objects with a ground part show in the terrain; units and animals passing do not.
        long key = tile & 0xFF;
        switch (viewer.Building(world, x, y, content, out RememberedBuilding seen))
        {
            case CellClasses.Remembered:
                return key | BuildingKey(seen.Kind, seen.Level, x - seen.X, y - seen.Y);
            case CellClasses.NoBuilding:
                return key;
            default:
                break;
        }
        switch (content.Content)
        {
            case CellContent.Building:
                Building b = world.Building(content.Index);
                key |= BuildingKey(b.Kind, b.Level, x - b.X, y - b.Y);
                break;
            case CellContent.ResourceNode:
                var node = world.Map.ResourceNodes[content.Index];
                key |= (2L << 8) | ((long)(node.Kind & 0xFF) << 16) | ((long)(node.Stage & 0xFF) << 24);
                break;
            case CellContent.Trap:
            case CellContent.SpottedTrap:
            case CellContent.PendingTrap:
                key |= ((long)(3 + CellClasses.Of(content, viewer)) << 8) | ((long)(content.Index & 0xFF) << 16);
                break;
            default:
                break;
        }
        return key;
    }

    /// <summary>The key bits of a cell <paramref name="dx"/>, <paramref name="dy"/> into a building’s footprint.</summary>
    private static long BuildingKey(int kind, int level, int dx, int dy) =>
        (1L << 8) | ((long)(kind & 0xFF) << 16) | ((long)(level & 0xFF) << 24) | ((long)(dx & 0xFF) << 32) | ((long)(dy & 0xFF) << 40);

    /// <summary>
    /// A number that changes when the terrain picture of cells [<paramref name="x0"/>,
    /// <paramref name="x1"/>) × [<paramref name="y0"/>, <paramref name="y1"/>) changes (their
    /// <see cref="CellKey"/>s). An endless map's cells are read chunk by chunk, so a block costs a
    /// few chunk look-ups rather than several per cell.
    /// </summary>
    public static long Signature(GameWorld world, int x0, int y0, int x1, int y1, int viewer) =>
        Signature(world, x0, y0, x1, y1, new PlayerJudge(viewer));

    /// <summary>The <see cref="Signature(GameWorld, int, int, int, int, int)"/> of the cells as <paramref name="sight"/> sees them.</summary>
    public static long Signature(GameWorld world, int x0, int y0, int x1, int y1, ViewerSight sight) =>
        Signature(world, x0, y0, x1, y1, new SightJudge(sight ?? throw new ArgumentNullException(nameof(sight))));

    private static long Signature<TJudge>(GameWorld world, int x0, int y0, int x1, int y1, TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        ArgumentNullException.ThrowIfNull(world);
        long hash = 17;
        if (!MapArea.IsChunked(world.Map))
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    hash = unchecked((hash * 1_000_003) ^ CellKey(world, x, y, viewer));
                }
            }
            return hash;
        }
        for (int y = y0; y < y1; y++)
        {
            MapChunk? chunk = null;
            int chunkX = int.MinValue;
            for (int x = x0; x < x1; x++)
            {
                if (x >> WorldMap.ChunkShift != chunkX)
                {
                    chunkX = x >> WorldMap.ChunkShift;
                    chunk = world.Map.Chunk(new ChunkCoord(chunkX, y >> WorldMap.ChunkShift));
                }
                var cell = new GridPoint(x, y);
                long key = chunk == null ? -1 : KeyOf(world, x, y, chunk.TileAt(cell), chunk.CellAt(cell), viewer);
                hash = unchecked((hash * 1_000_003) ^ key);
            }
        }
        return hash;
    }

    /// <summary>
    /// Draws the terrain of map cells [<paramref name="x0"/>, <paramref name="x1"/>) ×
    /// [<paramref name="y0"/>, <paramref name="y1"/>) into <paramref name="target"/>, the cell
    /// (<paramref name="x0"/>, <paramref name="y0"/>) at (<paramref name="originX"/>, <paramref name="originY"/>).
    /// Cells up to <see cref="Margin"/> around are drawn too (clipped by the image).
    /// </summary>
    public void Draw(GameWorld world, PartRasterizer rasterizer, RgbaImage target, int originX, int originY, int x0, int y0, int x1, int y1,
        int viewer = 0)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        int width = assets.CellWidth, height = assets.CellHeight;
        for (int y = y0 - Margin; y < y1 + Margin; y++)
        {
            for (int x = x0 - Margin; x < x1 + Margin; x++)
            {
                int cx = originX + ((x - x0) * width), cy = originY + ((y - y0) * height);
                IReadOnlyList<DrawPart>? parts = ComposeCell(world, x, y, viewer);
                if (parts == null)
                {
                    PartRasterizer.FillRect(target, cx, cy, width, height, BlackArgb);
                }
                else
                {
                    rasterizer.Draw(target, parts, cx, cy);
                }
            }
        }
    }

    /// <summary>
    /// The same picture as <see cref="Draw(GameWorld, PartRasterizer, RgbaImage, int, int, int, int, int, int, int)"/>
    /// drawn with <paramref name="raster"/> into a plain buffer (the world view's terrain blocks).
    /// </summary>
    public void Draw(GameWorld world, FastRaster raster, FastRaster.Target target, int originX, int originY, int x0, int y0, int x1, int y1,
        int viewer = 0) => Draw(world, raster, target, originX, originY, x0, y0, x1, y1, new PlayerJudge(viewer));

    /// <summary>The same picture as <paramref name="sight"/> sees the cells.</summary>
    public void Draw(GameWorld world, FastRaster raster, FastRaster.Target target, int originX, int originY, int x0, int y0, int x1, int y1,
        ViewerSight sight) =>
        Draw(world, raster, target, originX, originY, x0, y0, x1, y1, new SightJudge(sight ?? throw new ArgumentNullException(nameof(sight))));

    private void Draw<TJudge>(GameWorld world, FastRaster raster, FastRaster.Target target, int originX, int originY, int x0, int y0, int x1, int y1,
        TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(raster);
        int width = assets.CellWidth, height = assets.CellHeight;
        for (int y = y0 - Margin; y < y1 + Margin; y++)
        {
            for (int x = x0 - Margin; x < x1 + Margin; x++)
            {
                int cx = originX + ((x - x0) * width), cy = originY + ((y - y0) * height);
                if (!InMap(world, x, y))
                {
                    FastRaster.FillRect(target, cx, cy, width, height, BlackArgb);
                    continue;
                }
                var cell = new GridPoint(x, y);
                raster.Draw(target, TileParts(world.Map.TileAt(cell)), cx, cy);
                if (GroundRequest(world, world.Map.CellAt(cell), x, y, viewer) is var (request, originDx, originDy))
                {
                    raster.Draw(target, composers.Objects.Compose(request).Drawing.Parts, cx + originDx, cy + originDy);
                }
            }
        }
    }

    /// <summary>The parts of terrain tile <paramref name="tile"/> (composed once per tile).</summary>
    private IReadOnlyList<DrawPart> TileParts(int tile)
    {
        if (!tiles.TryGetValue(tile, out IReadOnlyList<DrawPart>? parts))
        {
            parts = composers.Terrain.ComposeTile(tile).Parts;
            tiles[tile] = parts;
        }
        return parts;
    }

    /// <summary>Whether cell (<paramref name="x"/>, <paramref name="y"/>) is land of the map.</summary>
    private static bool InMap(GameWorld world, int x, int y) =>
        MapArea.Bounds(world.Map).Contains(x, y) && MapArea.InPlay(world.Map, x, y);

    /// <summary>
    /// The terrain-layer request for the object at a cell and its origin relative to the cell, as
    /// GameCanvas.o_ makes it; null when the cell's object has no ground part (units, animals, the
    /// alliance hall, whose picture is all in the object layer, other tribes' ordered traps).
    /// </summary>
    /// <summary>The ground part of a building of <paramref name="kind"/> at (<paramref name="originX"/>, <paramref name="originY"/>) in cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private (ObjectRequest Request, int X, int Y)? GroundOf(GameWorld world, int kind, int level, int slot, int originX, int originY, int x, int y)
    {
        if (AllianceHall.Is(kind))
        {
            return null;
        }
        int footprintWidth = world.Content.Rules.Buildings.Kinds[kind].Width;
        ObjectRequest request = ObjectRequest.Building(kind, level, slot, ObjectPass.Ground) with
        {
            CellIndex = ((y - originY) * footprintWidth) + (x - originX),
        };
        return (request, (originX - x) * assets.CellWidth, (originY - y) * assets.CellHeight);
    }

    private (ObjectRequest Request, int X, int Y)? GroundRequest<TJudge>(GameWorld world, MapCell cell, int x, int y, TJudge viewer)
        where TJudge : struct, ITrapJudge
    {
        int width = assets.CellWidth, height = assets.CellHeight;
        switch (viewer.Building(world, x, y, cell, out RememberedBuilding seen))
        {
            case CellClasses.Remembered:
                return GroundOf(world, seen.Kind, seen.Level, seen.Slot, seen.X, seen.Y, x, y);
            case CellClasses.NoBuilding:
                return null;
            default:
                break;
        }
        if (cell.Content == CellContent.Building)
        {
            Building b = world.Building(cell.Index);
            if (AllianceHall.Is(b.Kind))
            {
                return null;
            }
            int footprintWidth = world.Content.Rules.Buildings.Kinds[b.Kind].Width;
            ObjectRequest request = ObjectRequest.Building(b.Kind, b.Level, cell.Index, ObjectPass.Ground) with
            {
                CellIndex = ((y - b.Y) * footprintWidth) + (x - b.X),
            };
            return (request, (b.X - x) * width, (b.Y - y) * height);
        }
        if (cell.Content == CellContent.ResourceNode)
        {
            var node = world.Map.ResourceNodes[cell.Index];
            return (ObjectRequest.ResourceNode(node.Kind, node.Stage, cell.Index, ObjectPass.Ground), 0, 0);
        }
        switch (CellClasses.Of(cell, viewer))
        {
            case CellClasses.PendingTrap:
                return (ObjectRequest.Trap(CellClasses.PendingTrapKind, 0, ObjectPass.Ground), 0, 0);
            case CellClasses.OwnTrap:
                return (ObjectRequest.Trap(cell.Index, 1, ObjectPass.Ground), 0, 0);
            case CellClasses.OtherTrap:
                // The original draws the computer's traps with the object-layer rules here, which shows nothing of them.
                return (ObjectRequest.Trap(cell.Index, 1, ObjectPass.Upper), 0, 0);
            default:
                return null;
        }
    }
}
