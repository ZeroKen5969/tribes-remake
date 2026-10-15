using System;
using System.Collections.Generic;

namespace Tribes.Core.Content;

/// <summary>
/// One of the original game's scenarios: the tutorial, the campaign missions and the custom-game
/// maps, numbered as the original numbers them.
/// </summary>
/// <param name="Id">Scenario number (the tutorial is 0; the campaign follows; custom maps last).</param>
/// <param name="MapId">The map it is played on (two scenarios may share one).</param>
/// <param name="PlayableSize">
/// The part of the map in play, from the top-left corner; smaller than the map for a scenario
/// that uses only part of it.
/// </param>
/// <param name="StartCenter">The cell the view is centred on at the start.</param>
/// <param name="HasTerrainObjects">
/// Whether the map's resource nodes and obstacles are placed; one campaign mission is played on
/// bare terrain.
/// </param>
public sealed record MissionDefinition(int Id, int MapId, GridSize PlayableSize, GridPoint StartCenter,
    bool HasTerrainObjects);

/// <summary>A map as designed: the base terrain tile of every cell, before random variants.</summary>
public sealed class MapDefinition
{
    private readonly byte[] tiles;

    /// <summary>Creates a map; <paramref name="tiles"/> holds the rows from top to bottom.</summary>
    public MapDefinition(int id, GridSize size, byte[] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (size.Width <= 0 || size.Height <= 0 || tiles.Length != size.Width * size.Height)
        {
            throw new ArgumentException(
                $"map {id}: {tiles.Length} tiles do not fill {size.Width}x{size.Height}", nameof(tiles));
        }
        Id = id;
        Size = size;
        this.tiles = (byte[])tiles.Clone();
    }

    /// <summary>Map number, as referred to by <see cref="MissionDefinition.MapId"/>.</summary>
    public int Id { get; }

    /// <summary>Width and height in cells.</summary>
    public GridSize Size { get; }

    /// <summary>The base terrain tile at <paramref name="cell"/>.</summary>
    public int TileAt(GridPoint cell)
    {
        if (!Size.Contains(cell))
        {
            throw new ArgumentOutOfRangeException(nameof(cell), cell, $"outside map {Id} ({Size})");
        }
        return tiles[(cell.Y * Size.Width) + cell.X];
    }
}

/// <summary>An inclusive range of terrain tile numbers.</summary>
public readonly record struct TileRange(int First, int Last)
{
    /// <summary>Whether <paramref name="tile"/> is in the range.</summary>
    public bool Contains(int tile) => tile >= First && tile <= Last;
}

/// <summary>How the rules treat one terrain tile.</summary>
/// <param name="Id">Tile number as used in <see cref="MapDefinition"/>.</param>
/// <param name="BlocksMovement">Nothing can enter or be built on it.</param>
/// <param name="IsRock">A rock formation; it blocks like other terrain but is a cell of its own kind.</param>
public sealed record TileKind(int Id, bool BlocksMovement, bool IsRock);

/// <summary>The terrain tiles and the tile ranges with a special meaning.</summary>
public sealed class TerrainCatalog
{
    private readonly Dictionary<int, TileKind> tiles = new();
    private readonly HashSet<int> shoreTiles;

    /// <summary>Creates the catalog.</summary>
    /// <param name="tiles">Every tile a map can contain.</param>
    /// <param name="variantTiles">Tiles drawn as one of several variants chosen at random.</param>
    /// <param name="variantCount">How many variants each of those tiles has (consecutive numbers).</param>
    /// <param name="resourceTiles">Tiles where a resource node starts; the first is node kind 1.</param>
    /// <param name="obstacleTiles">Tiles that are obstacles on the occupancy grid.</param>
    /// <param name="shoreTiles">
    /// Tiles that become shore cells beside a resource node of a kind that marks the shore.
    /// </param>
    public TerrainCatalog(IEnumerable<TileKind> tiles, TileRange variantTiles, int variantCount,
        TileRange resourceTiles, TileRange obstacleTiles, IEnumerable<int> shoreTiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        foreach (TileKind tile in tiles)
        {
            this.tiles.Add(tile.Id, tile);
        }
        VariantTiles = variantTiles;
        VariantCount = variantCount;
        ResourceTiles = resourceTiles;
        ObstacleTiles = obstacleTiles;
        this.shoreTiles = new HashSet<int>(shoreTiles);
    }

    /// <summary>
    /// Tiles that become shore cells beside a resource node of a kind that marks the shore
    /// (see <see cref="ResourceNodeKind.MarksShore"/>).
    /// </summary>
    public IReadOnlyCollection<int> ShoreTiles => shoreTiles;

    /// <summary>Whether <paramref name="tile"/> is one of <see cref="ShoreTiles"/>.</summary>
    public bool IsShore(int tile) => shoreTiles.Contains(tile);

    /// <summary>Tiles drawn as one of <see cref="VariantCount"/> variants chosen at random.</summary>
    public TileRange VariantTiles { get; }

    /// <summary>Number of variants of each tile in <see cref="VariantTiles"/>.</summary>
    public int VariantCount { get; }

    /// <summary>Tiles where a resource node starts; the first is node kind 1.</summary>
    public TileRange ResourceTiles { get; }

    /// <summary>Tiles that are obstacles on the occupancy grid.</summary>
    public TileRange ObstacleTiles { get; }

    /// <summary>All tiles, by number.</summary>
    public IReadOnlyCollection<TileKind> Tiles => tiles.Values;

    /// <summary>The tile numbered <paramref name="id"/>.</summary>
    public TileKind this[int id] =>
        tiles.TryGetValue(id, out TileKind? tile)
            ? tile
            : throw new KeyNotFoundException($"no terrain tile {id}");
}

/// <summary>A kind of resource node (a bush, tree, rock...).</summary>
/// <param name="Id">Kind number; kind n starts on tile <c>ResourceTiles.First + n - 1</c>.</param>
/// <param name="MaxAmount">How much a full node holds.</param>
/// <param name="MarksShore">
/// Whether the shore tiles left and right of such a node become shore cells, which only units
/// working the node can enter.
/// </param>
public sealed record ResourceNodeKind(int Id, int MaxAmount, bool MarksShore);
