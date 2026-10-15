using System;
using Tribes.Core.Content;

namespace Tribes.Core.Maps;

/// <summary>Builds the map a scenario starts on from its design.</summary>
public static class MapBuilder
{
    /// <summary>
    /// The start map of mission <paramref name="missionId"/>. Draws from <paramref name="random"/>
    /// once for every cell whose tile has variants, in rows from the top, left to right; resource
    /// nodes take slots in the same order.
    /// </summary>
    public static WorldMap Build(int missionId, GameContent content, GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(random);
        MissionDefinition mission = content.Missions[missionId];
        MapDefinition design = content.Maps[mission.MapId];
        TerrainCatalog terrain = content.Terrain;
        var map = new WorldMap(design.Size, mission.PlayableSize);
        for (int y = 0; y < design.Size.Height; y++)
        {
            for (int x = 0; x < design.Size.Width; x++)
            {
                var cell = new GridPoint(x, y);
                int tile = design.TileAt(cell);
                if (terrain.VariantTiles.Contains(tile))
                {
                    // The original picks one of three variants with a biased draw: (r & 3) % 3
                    // gives the first variant half of the time.
                    tile += random.NextMasked(VariantMask, terrain.VariantCount);
                }
                map.SetTile(cell, tile);
                map.SetCell(cell, TerrainContent(terrain[tile]));
                if (mission.HasTerrainObjects)
                {
                    PlaceTerrainObject(map, cell, tile, content);
                }
                if (IsEdge(map, cell))
                {
                    map.SetCell(cell, MapCell.Of(CellContent.Impassable));
                }
            }
        }
        MarkShores(map, content);
        return map;
    }

    /// <summary>The draw that picks a tile variant uses the low two bits of the random number.</summary>
    internal const int VariantMask = 3;

    internal static MapCell TerrainContent(TileKind kind) =>
        kind.BlocksMovement ? MapCell.Of(CellContent.Impassable) : MapCell.Empty;

    /// <summary>Rocks, thickets and resource nodes drawn into the map design.</summary>
    internal static void PlaceTerrainObject(WorldMap map, GridPoint cell, int tile, GameContent content)
    {
        TerrainCatalog terrain = content.Terrain;
        if (terrain[tile].IsRock)
        {
            map.SetCell(cell, MapCell.Of(CellContent.Rocks));
        }
        if (terrain.ObstacleTiles.Contains(tile))
        {
            map.SetCell(cell, MapCell.Of(CellContent.Thicket));
        }
        else if (terrain.ResourceTiles.Contains(tile))
        {
            int kind = tile - terrain.ResourceTiles.First + 1;
            ResourceNode? node = map.PlaceResourceNode(kind, cell, content.ResourceNodeKinds[kind].MaxAmount);
            if (node == null)
            {
                throw new InvalidOperationException(
                    $"resource node at {cell}: all {map.ResourceNodes.Count} node slots are taken");
            }
        }
    }

    /// <summary>
    /// The top row, the bottom row and the right column are the map's edge. The left column is not:
    /// as in the original game, its cells keep what the terrain says.
    /// </summary>
    private static bool IsEdge(WorldMap map, GridPoint cell) =>
        cell.Y == 0 || cell.Y == map.Size.Height - 1 || cell.X == map.Size.Width - 1;

    /// <summary>Turns the shore tiles left and right of shore-marking nodes into shore cells.</summary>
    private static void MarkShores(WorldMap map, GameContent content)
    {
        for (int y = 0; y < map.PlayableSize.Height; y++)
        {
            for (int x = 1; x < map.PlayableSize.Width - 1; x++)
            {
                MapCell cell = map.CellAt(new GridPoint(x, y));
                if (cell.Content != CellContent.ResourceNode
                    || !content.ResourceNodeKinds[map.ResourceNodes[cell.Index].Kind].MarksShore)
                {
                    continue;
                }
                foreach (int side in new[] { x - 1, x + 1 })
                {
                    var neighbour = new GridPoint(side, y);
                    if (content.Terrain.IsShore(map.TileAt(neighbour)))
                    {
                        map.SetCell(neighbour, MapCell.Of(CellContent.Shore));
                    }
                }
            }
        }
    }
}
