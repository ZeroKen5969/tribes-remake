using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tribes.Core.Content;

namespace Tribes.Core.Maps;

/// <summary>
/// The map of a game in progress: terrain, what fills each cell, and the resource nodes. A map is
/// either a fixed grid (the campaign's and the custom maps) or, in an endless world, chunks of
/// <see cref="ChunkSize"/> by <see cref="ChunkSize"/> cells made as the tribes need land
/// (<see cref="IsChunked"/>); cells of an endless map may have negative coordinates.
/// </summary>
public sealed class WorldMap
{
    /// <summary>Resource node slots in the original game (a chunked map has as many as it needs).</summary>
    public const int DefaultNodeCapacity = 50;

    /// <summary>A cell coordinate shifted right by this gives its chunk coordinate.</summary>
    public const int ChunkShift = 6;

    /// <summary>Cells along each side of a chunk of an endless map.</summary>
    public const int ChunkSize = 1 << ChunkShift;

    /// <summary>A cell coordinate masked with this gives its place inside the chunk.</summary>
    public const int ChunkMask = ChunkSize - 1;

    /// <summary>
    /// The tile <see cref="TileAt"/> gives for a cell of an endless map that has not been made yet: it
    /// blocks movement and has no picture.
    /// </summary>
    public const int UngeneratedTile = byte.MaxValue;

    private static readonly MapCell Ungenerated = MapCell.Of(CellContent.Impassable);

    private readonly GridSize size;
    private readonly byte[] tiles;
    private readonly MapCell[] cells;
    private readonly ChunkGrid? grid;
    private readonly List<ResourceNode> nodes = new();
    private GridSize playableSize;

    /// <summary>
    /// A running hash of every tile and cell that is not plain tile 0 and empty: the exclusive-or of
    /// a mix of each such cell's place and contents, kept as cells change, so hashing the map costs
    /// nothing however large it is (see <see cref="Exchange"/>).
    /// </summary>
    private ulong contentHash;

    /// <summary>Creates an empty map.</summary>
    /// <param name="size">Size of the whole map.</param>
    /// <param name="playableSize">The part in play, from the top-left corner.</param>
    /// <param name="nodeCapacity">Number of resource node slots.</param>
    public WorldMap(GridSize size, GridSize playableSize, int nodeCapacity = DefaultNodeCapacity)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "map size must be positive");
        }
        if (playableSize.Width < 0 || playableSize.Height < 0
            || playableSize.Width > size.Width || playableSize.Height > size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(playableSize), playableSize, $"not within {size}");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCapacity);
        this.size = size;
        this.playableSize = playableSize;
        tiles = new byte[size.Width * size.Height];
        cells = new MapCell[size.Width * size.Height];
        AddNodeSlots(nodeCapacity);
        ResourceNodes = new ReadOnlyCollection<ResourceNode>(nodes);
        Chunks = Array.Empty<MapChunk>();
    }

    private WorldMap(ChunkGrid grid)
    {
        this.grid = grid;
        tiles = Array.Empty<byte>();
        cells = Array.Empty<MapCell>();
        ResourceNodes = new ReadOnlyCollection<ResourceNode>(nodes);
        Chunks = grid.Chunks;
    }

    /// <summary>An endless map without land yet (chunks are added as they are made).</summary>
    public static WorldMap Chunked() => new(new ChunkGrid());

    /// <summary>Whether this is an endless map made of chunks.</summary>
    public bool IsChunked => grid != null;

    /// <summary>
    /// Size of the whole map; for a chunked map the size of <see cref="Bounds"/> (which does not
    /// start at (0, 0): use <see cref="Bounds"/>).
    /// </summary>
    public GridSize Size => grid == null ? size : new GridSize(grid.Bounds.Width, grid.Bounds.Height);

    /// <summary>The part in play, from the top-left corner; for a chunked map the same as <see cref="Size"/>.</summary>
    public GridSize PlayableSize
    {
        get => grid == null ? playableSize : Size;
        internal set => playableSize = value;
    }

    /// <summary>
    /// The cells in play: from (0, 0) the size of <see cref="PlayableSize"/> for a fixed map, the
    /// bounding box of the chunks made so far for a chunked one (cells of chunks not made inside it
    /// are not in play).
    /// </summary>
    public GridRect Bounds => grid == null ? new GridRect(0, 0, playableSize.Width, playableSize.Height) : grid.Bounds;

    /// <summary>The chunks made so far, in the order they were made (none for a fixed map).</summary>
    public IReadOnlyList<MapChunk> Chunks { get; }

    /// <summary>All resource node slots, used or free.</summary>
    public IReadOnlyList<ResourceNode> ResourceNodes { get; }

    /// <summary>Chunk <paramref name="coord"/>, or null when it has not been made (or the map is fixed).</summary>
    public MapChunk? Chunk(ChunkCoord coord) => grid?.Find(coord.X, coord.Y);

    /// <summary>Whether <paramref name="cell"/> is on the map (for a chunked map: in a chunk made).</summary>
    public bool Contains(GridPoint cell) => grid == null ? size.Contains(cell) : grid.FindCell(cell.X, cell.Y) != null;

    /// <summary>Whether <paramref name="cell"/> is inside the part in play (for a chunked map: in a chunk made).</summary>
    public bool InPlay(GridPoint cell) => grid == null ? playableSize.Contains(cell) : grid.FindCell(cell.X, cell.Y) != null;

    /// <summary><paramref name="x"/> kept within the columns of <see cref="Bounds"/>.</summary>
    public int ClampColumn(int x) => Clamp(x, Bounds.Left, Bounds.Right - 1);

    /// <summary><paramref name="y"/> kept within the rows of <see cref="Bounds"/>.</summary>
    public int ClampRow(int y) => Clamp(y, Bounds.Top, Bounds.Bottom - 1);

    /// <summary>The running hash of the tiles and cells (equal maps have equal values).</summary>
    internal ulong ContentHash => contentHash;

    /// <summary>The terrain tile at <paramref name="cell"/> (after random variants); <see cref="UngeneratedTile"/> on land not made yet.</summary>
    public int TileAt(GridPoint cell)
    {
        if (grid == null)
        {
            return tiles[IndexOf(cell)];
        }
        MapChunk? chunk = grid.FindCell(cell.X, cell.Y);
        return chunk == null ? UngeneratedTile : chunk.Tiles[MapChunk.LocalIndex(cell.X, cell.Y)];
    }

    /// <summary>What fills <paramref name="cell"/>; impassable on land not made yet.</summary>
    public MapCell CellAt(GridPoint cell)
    {
        if (grid == null)
        {
            return cells[IndexOf(cell)];
        }
        MapChunk? chunk = grid.FindCell(cell.X, cell.Y);
        return chunk == null ? Ungenerated : chunk.CellArray[MapChunk.LocalIndex(cell.X, cell.Y)];
    }

    /// <summary>Sets the terrain tile at <paramref name="cell"/>.</summary>
    public void SetTile(GridPoint cell, int tile)
    {
        byte value = checked((byte)tile);
        if (grid == null)
        {
            int index = IndexOf(cell);
            contentHash ^= Term(cell, tiles[index], cells[index]) ^ Term(cell, value, cells[index]);
            tiles[index] = value;
            return;
        }
        MapChunk chunk = ChunkOf(cell);
        int local = MapChunk.LocalIndex(cell.X, cell.Y);
        contentHash ^= Term(cell, chunk.Tiles[local], chunk.CellArray[local]) ^ Term(cell, value, chunk.CellArray[local]);
        chunk.SetTile(local, value);
    }

    /// <summary>Sets what fills <paramref name="cell"/>.</summary>
    public void SetCell(GridPoint cell, MapCell content)
    {
        if (grid == null)
        {
            int index = IndexOf(cell);
            contentHash ^= Term(cell, tiles[index], cells[index]) ^ Term(cell, tiles[index], content);
            cells[index] = content;
            return;
        }
        MapChunk chunk = ChunkOf(cell);
        int local = MapChunk.LocalIndex(cell.X, cell.Y);
        contentHash ^= Term(cell, chunk.Tiles[local], chunk.CellArray[local]) ^ Term(cell, chunk.Tiles[local], content);
        chunk.SetCell(local, content);
    }

    /// <summary>
    /// Puts a node of <paramref name="kind"/> in the first free slot and on its cell; null when
    /// every slot is taken (a chunked map adds a slot instead).
    /// </summary>
    public ResourceNode? PlaceResourceNode(int kind, GridPoint position, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kind);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ResourceNode? free = nodes.Find(node => !node.InUse);
        if (free == null && grid != null)
        {
            AddNodeSlots(1);
            free = nodes[^1];
        }
        if (free == null)
        {
            return null;
        }
        free.Kind = kind;
        free.X = position.X;
        free.Y = position.Y;
        free.Amount = amount;
        SetCell(position, MapCell.Node(free.Index));
        return free;
    }

    /// <summary>The chunk holding <paramref name="cell"/>, or null (land not made, or a fixed map).</summary>
    internal MapChunk? ChunkAtCell(GridPoint cell) => grid?.FindCell(cell.X, cell.Y);

    /// <summary>Makes chunk <paramref name="coord"/> of a chunked map (tile 0, empty cells, until the generator fills it).</summary>
    /// <exception cref="InvalidOperationException">The map is fixed or the chunk exists already.</exception>
    internal MapChunk AddChunk(ChunkCoord coord, int generatorVersion) =>
        (grid ?? throw new InvalidOperationException("a fixed map has no chunks")).Add(coord, generatorVersion);

    /// <summary>Empties the map: every tile 0 and every cell empty (a chunked map loses its chunks); the node slots stay.</summary>
    internal void Clear()
    {
        contentHash = 0;
        if (grid != null)
        {
            grid.Clear();
            return;
        }
        Array.Clear(tiles);
        Array.Clear(cells);
    }

    /// <summary>The share of one cell in <see cref="contentHash"/> (none for plain tile 0 and an empty cell).</summary>
    private static ulong Term(GridPoint cell, byte tile, MapCell content)
    {
        if (tile == 0 && content == default)
        {
            return 0;
        }
        ulong hash = World.StateHasher.Finish(((ulong)(uint)cell.X << 32) | (uint)cell.Y);
        hash = World.StateHasher.Finish(hash ^ tile ^ ((ulong)(byte)content.Content << 8) ^ ((ulong)(uint)content.Owner << 32));
        return World.StateHasher.Finish(hash ^ (uint)content.Index);
    }

    /// <summary>Works <see cref="contentHash"/> out again from every cell (after loading).</summary>
    private void RecomputeHash()
    {
        contentHash = 0;
        if (grid == null)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                contentHash ^= Term(new GridPoint(i % size.Width, i / size.Width), tiles[i], cells[i]);
            }
            return;
        }
        foreach (MapChunk chunk in grid.Chunks)
        {
            for (int i = 0; i < chunk.Tiles.Length; i++)
            {
                var cell = new GridPoint(chunk.Cells.X + (i & ChunkMask), chunk.Cells.Y + (i >> ChunkShift));
                contentHash ^= Term(cell, chunk.Tiles[i], chunk.CellArray[i]);
            }
        }
    }

    private MapChunk ChunkOf(GridPoint cell) => grid!.FindCell(cell.X, cell.Y)
        ?? throw new ArgumentOutOfRangeException(nameof(cell), cell, "the land there has not been made");

    private void AddNodeSlots(int count)
    {
        for (int i = 0; i < count; i++)
        {
            nodes.Add(new ResourceNode(nodes.Count));
        }
    }

    private int IndexOf(GridPoint cell)
    {
        if (!size.Contains(cell))
        {
            throw new ArgumentOutOfRangeException(nameof(cell), cell, $"outside the map ({size})");
        }
        return (cell.Y * size.Width) + cell.X;
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value < max ? value : max;

    /// <summary>Saves, loads or hashes the map's state (see <see cref="World.StateExchange"/>).</summary>
    internal void Exchange(World.StateExchange s)
    {
        if (grid != null)
        {
            if (s is World.StateHasher chunkHasher)
            {
                ChunkExchange.HashList(chunkHasher, grid);
                chunkHasher.Mix64(contentHash);
            }
            else
            {
                ChunkExchange.Exchange(s, grid);
            }
            int count = s.Value(nodes.Count);
            AddNodeSlots(count - nodes.Count);
            ExchangeNodes(s);
            FinishReading(s);
            return;
        }
        playableSize = new GridSize(s.Value(playableSize.Width), s.Value(playableSize.Height));
        if (s is World.StateHasher hasher)
        {
            hasher.Mix64(contentHash);
        }
        else
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = (byte)s.Value(tiles[i]);
                MapCell cell = cells[i];
                cells[i] = new MapCell((CellContent)s.Value((int)cell.Content), s.Value(cell.Index), s.Value(cell.Owner));
            }
        }
        ExchangeNodes(s);
        FinishReading(s);
    }

    private void FinishReading(World.StateExchange s)
    {
        if (s.IsReading)
        {
            RecomputeHash();
        }
    }

    private void ExchangeNodes(World.StateExchange s)
    {
        foreach (ResourceNode node in nodes)
        {
            node.Exchange(s);
        }
    }
}
