using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Tribes.Core.Content;

namespace Tribes.Core.Maps.Generation;

/// <summary>One of the original map designs, as the generator copies pieces of it.</summary>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
/// <param name="Tiles">Design tiles in rows from the top.</param>
internal sealed record SourceMap(int Width, int Height, byte[] Tiles)
{
    /// <summary>The design tile at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public int TileAt(int x, int y) => Tiles[(y * Width) + x];
}

/// <summary>
/// What the terrain generator learns from the original maps (their designs, before random tile
/// variants): which tile may stand beside which in each direction, how often each tile is used, and
/// the designs themselves to copy pieces from. It depends on the content only.
/// </summary>
internal sealed class TerrainModel
{
    /// <summary>Directions to a neighbour: right, down, left, up.</summary>
    public const int Right = 0, Down = 1, Left = 2, Up = 3, Directions = 4;

    private static readonly ConditionalWeakTable<GameContent, TerrainModel> Models = new();

    private readonly TileSet[][] allowed = new TileSet[Directions][];
    private readonly TileSet[] anyAllowed = new TileSet[Directions];

    /// <summary>Tiles a set holds per byte of it.</summary>
    private const int TilesPerByte = 8, Bytes = TileSet.Capacity / TilesPerByte, Subsets = 1 << TilesPerByte;

    /// <summary>For each direction, byte of a tile set and value of that byte: the tiles allowed next to those tiles.</summary>
    private readonly TileSet[][] byteUnions = new TileSet[Directions][];

    private TerrainModel(GameContent content)
    {
        for (int d = 0; d < Directions; d++)
        {
            allowed[d] = new TileSet[TileSet.Capacity];
        }
        Weights = new int[TileSet.Capacity];
        Sources = content.Maps.Values.OrderBy(m => m.Id).Select(ToSource).ToArray();
        foreach (SourceMap source in Sources)
        {
            Learn(source);
        }
        for (int t = 0; t < TileSet.Capacity; t++)
        {
            Walkable[t] = !content.Terrain[t].BlocksMovement;
            Resource[t] = content.Terrain.ResourceTiles.Contains(t);
            Clearable[t] = content.Terrain[t].IsRock || content.Terrain.ObstacleTiles.Contains(t) || Resource[t];
            if (Weights[t] > 0)
            {
                Seen.Add(t);
                if (Walkable[t])
                {
                    SeenWalkable.Add(t);
                }
                for (int d = 0; d < Directions; d++)
                {
                    anyAllowed[d] |= allowed[d][t];
                }
            }
        }
        Ground = Enumerable.Range(0, TileSet.Capacity).Where(t => Walkable[t]).MaxBy(t => Weights[t]);
        for (int d = 0; d < Directions; d++)
        {
            byteUnions[d] = new TileSet[Bytes * Subsets];
            for (int b = 0; b < Bytes; b++)
            {
                for (int subset = 1; subset < Subsets; subset++)
                {
                    int low = System.Numerics.BitOperations.TrailingZeroCount(subset);
                    byteUnions[d][(b * Subsets) + subset] = byteUnions[d][(b * Subsets) + (subset & (subset - 1))]
                        | allowed[d][(b * TilesPerByte) + low];
                }
            }
        }
    }

    /// <summary>The designs learnt from, by map number.</summary>
    public IReadOnlyList<SourceMap> Sources { get; }

    /// <summary>How many cells of the designs use each tile.</summary>
    public int[] Weights { get; }

    /// <summary>The tiles the designs use.</summary>
    public TileSet Seen;

    /// <summary>The tiles the designs use that units can walk on.</summary>
    public TileSet SeenWalkable;

    /// <summary>Whether each tile can be walked on.</summary>
    public bool[] Walkable { get; } = new bool[TileSet.Capacity];

    /// <summary>Whether each tile is a resource node of the design.</summary>
    public bool[] Resource { get; } = new bool[TileSet.Capacity];

    /// <summary>Whether each tile is a thicket, rock or resource node of the design (cleared without changing the ground around it).</summary>
    public bool[] Clearable { get; } = new bool[TileSet.Capacity];

    /// <summary>The most used tile that can be walked on (plain ground).</summary>
    public int Ground { get; }

    /// <summary>The model of <paramref name="content"/>'s maps (learnt once per content).</summary>
    public static TerrainModel For(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Models.GetValue(content, c => new TerrainModel(c));
    }

    /// <summary>The direction back from a neighbour in <paramref name="direction"/>.</summary>
    public static int Opposite(int direction) => (direction + 2) & 3;

    /// <summary>The column step of <paramref name="direction"/>.</summary>
    public static int StepX(int direction) => direction switch { Right => 1, Left => -1, _ => 0 };

    /// <summary>The row step of <paramref name="direction"/>.</summary>
    public static int StepY(int direction) => direction switch { Down => 1, Up => -1, _ => 0 };

    /// <summary>The tiles seen next to <paramref name="tile"/> in <paramref name="direction"/>.</summary>
    public TileSet Allowed(int tile, int direction) => allowed[direction][tile];

    /// <summary>Whether <paramref name="next"/> may stand in <paramref name="direction"/> of <paramref name="tile"/>.</summary>
    public bool Fits(int tile, int direction, int next) => allowed[direction][tile].Contains(next);

    /// <summary>The tiles that may stand in <paramref name="direction"/> of any tile of <paramref name="tiles"/>.</summary>
    public TileSet AllowedNextTo(TileSet tiles, int direction)
    {
        if (tiles.SameAs(Seen))
        {
            return anyAllowed[direction];
        }
        TileSet[] unions = byteUnions[direction];
        TileSet result = default;
        for (int b = 0; b < Bytes; b++)
        {
            int subset = tiles.ByteAt(b);
            if (subset != 0)
            {
                result |= unions[(b * Subsets) + subset];
            }
        }
        return result;
    }

    private static SourceMap ToSource(MapDefinition map)
    {
        byte[] tiles = new byte[map.Size.Width * map.Size.Height];
        for (int y = 0; y < map.Size.Height; y++)
        {
            for (int x = 0; x < map.Size.Width; x++)
            {
                tiles[(y * map.Size.Width) + x] = (byte)map.TileAt(new GridPoint(x, y));
            }
        }
        return new SourceMap(map.Size.Width, map.Size.Height, tiles);
    }

    private void Learn(SourceMap source)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int tile = source.TileAt(x, y);
                Weights[tile]++;
                if (x + 1 < source.Width)
                {
                    Pair(tile, Right, source.TileAt(x + 1, y));
                }
                if (y + 1 < source.Height)
                {
                    Pair(tile, Down, source.TileAt(x, y + 1));
                }
            }
        }
    }

    private void Pair(int tile, int direction, int next)
    {
        allowed[direction][tile].Add(next);
        allowed[Opposite(direction)][next].Add(tile);
    }
}
