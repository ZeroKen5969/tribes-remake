using Tribes.Core.Content;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Turns a chunk design into the chunk's tiles and cells as the original builds a map from its
/// design: random tile variants, blocking terrain, rocks, thickets, resource nodes, and shore cells
/// beside the nodes that mark the shore (also across the edge to a neighbouring chunk).
/// </summary>
internal static class ChunkBuilder
{
    /// <summary>Writes the design of <paramref name="canvas"/> into chunk <paramref name="coord"/> of <paramref name="map"/>.</summary>
    public static void Build(WorldMap map, ChunkCoord coord, ChunkCanvas canvas, GameContent content, GameRandom random)
    {
        TerrainCatalog terrain = content.Terrain;
        GridPoint origin = coord.Origin;
        for (int y = 0; y < ChunkCanvas.Size; y++)
        {
            for (int x = 0; x < ChunkCanvas.Size; x++)
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                int tile = canvas[x, y];
                if (terrain.VariantTiles.Contains(tile))
                {
                    tile += random.NextMasked(MapBuilder.VariantMask, terrain.VariantCount);
                }
                map.SetTile(cell, tile);
                map.SetCell(cell, MapBuilder.TerrainContent(terrain[tile]));
                MapBuilder.PlaceTerrainObject(map, cell, tile, content);
            }
        }
        MarkShores(map, coord, content);
    }

    /// <summary>The design tile of a built tile: a random variant counts as the tile it was drawn from.</summary>
    public static int DesignTile(TerrainCatalog terrain, int tile) =>
        terrain.VariantTiles.Contains(tile)
            ? terrain.VariantTiles.First + ((tile - terrain.VariantTiles.First) / terrain.VariantCount * terrain.VariantCount)
            : tile;

    /// <summary>Shore tiles left and right of a shore-marking node become shore cells (in the chunk, or the neighbour's edge column).</summary>
    private static void MarkShores(WorldMap map, ChunkCoord coord, GameContent content)
    {
        GridPoint origin = coord.Origin;
        for (int y = 0; y < ChunkCanvas.Size; y++)
        {
            for (int x = -1; x <= ChunkCanvas.Size; x++)
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                if (!MarksShore(map, cell, content))
                {
                    continue;
                }
                foreach (int side in new[] { -1, 1 })
                {
                    var neighbour = new GridPoint(cell.X + side, cell.Y);
                    bool touchesChunk = coord.Cells.Contains(cell) || coord.Cells.Contains(neighbour);
                    if (touchesChunk && map.Contains(neighbour) && content.Terrain.IsShore(map.TileAt(neighbour))
                        && map.CellAt(neighbour).Content == CellContent.Impassable)
                    {
                        map.SetCell(neighbour, MapCell.Of(CellContent.Shore));
                    }
                }
            }
        }
    }

    private static bool MarksShore(WorldMap map, GridPoint cell, GameContent content)
    {
        MapCell node = map.CellAt(cell);
        return node.Content == CellContent.ResourceNode
            && content.ResourceNodeKinds[map.ResourceNodes[node.Index].Kind].MarksShore;
    }
}
