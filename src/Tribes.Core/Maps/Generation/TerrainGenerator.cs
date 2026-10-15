using System;
using Tribes.Core.Content;

namespace Tribes.Core.Maps.Generation;

/// <summary>
/// Makes the land of an endless world one chunk at a time, in the look of the original maps:
/// pieces of their designs stitched by their own neighbour rules, joined to the chunks around that
/// exist already (whatever generator made them: their saved edge is the constraint) and kept
/// walkable from chunk to chunk (<see cref="ChunkConnectivity"/>). A chunk depends on the world's
/// seed, its coordinates and the neighbours made before it.
/// </summary>
internal sealed class TerrainGenerator
{
    /// <summary>The version written with every chunk this generator makes; raise it when the land it makes changes.</summary>
    public const int Version = 2;

    private readonly GameContent content;
    private readonly ChunkDesigner designer;

    /// <summary>A generator for <paramref name="content"/>'s terrain, learnt from its maps.</summary>
    public TerrainGenerator(GameContent content)
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        Model = TerrainModel.For(content);
        designer = new ChunkDesigner(Model);
    }

    /// <summary>What the generator learnt from the original maps.</summary>
    public TerrainModel Model { get; }

    /// <summary>The seed of chunk (<paramref name="chunkX"/>, <paramref name="chunkY"/>) of the world with <paramref name="worldSeed"/>.</summary>
    public static long ChunkSeed(long worldSeed, int chunkX, int chunkY)
    {
        ulong mixed = Mix((ulong)worldSeed ^ Mix((uint)chunkX | ((ulong)(uint)chunkY << 32)));
        return (long)mixed;
    }

    /// <summary>Makes chunk <paramref name="coord"/> of <paramref name="map"/>; whether every seam fits the learnt rules.</summary>
    /// <exception cref="InvalidOperationException">The chunk exists already.</exception>
    public bool Generate(WorldMap map, ChunkCoord coord, long worldSeed)
    {
        ArgumentNullException.ThrowIfNull(map);
        var canvas = new ChunkCanvas();
        foreach ((int x, int y) in ChunkCanvas.Ring)
        {
            canvas[x, y] = RingTile(map, coord, x, y);
        }
        GameRandom random = RandomOf(coord, worldSeed);
        bool fits = Design(canvas, random);
        Build(map, coord, canvas, random);
        return fits;
    }

    /// <summary>The random draws chunk <paramref name="coord"/> is made with (the design's, then the build's).</summary>
    public static GameRandom RandomOf(ChunkCoord coord, long worldSeed) => new(ChunkSeed(worldSeed, coord.X, coord.Y));

    /// <summary>
    /// Ring cell (<paramref name="x"/>, <paramref name="y"/>) of chunk <paramref name="coord"/>'s
    /// canvas as <paramref name="map"/> has it: the design tile of a neighbour made already, or
    /// <see cref="ChunkCanvas.Open"/>.
    /// </summary>
    public int RingTile(WorldMap map, ChunkCoord coord, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cell = new GridPoint(coord.Origin.X + x, coord.Origin.Y + y);
        return map.Contains(cell) ? ChunkBuilder.DesignTile(content.Terrain, map.TileAt(cell)) : ChunkCanvas.Open;
    }

    /// <summary>
    /// Designs the chunk cells of <paramref name="canvas"/> (its ring set); whether every seam fits.
    /// Touches nothing else, so it may run off the frame (one design at a time per generator).
    /// </summary>
    public bool Design(ChunkCanvas canvas, GameRandom random) => designer.Design(canvas, random);

    /// <summary>Adds chunk <paramref name="coord"/> to <paramref name="map"/> as designed on <paramref name="canvas"/>.</summary>
    /// <exception cref="InvalidOperationException">The chunk exists already.</exception>
    public void Build(WorldMap map, ChunkCoord coord, ChunkCanvas canvas, GameRandom random)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.AddChunk(coord, Version);
        ChunkBuilder.Build(map, coord, canvas, content, random);
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
