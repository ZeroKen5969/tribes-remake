using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Tribes.Client.World.Scene;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World;

/// <summary>
/// The terrain layer of the world view: blocks of cells (see <see cref="TerrainTiling"/>)
/// rasterised on the CPU (<see cref="FastRaster"/>) into textures. The blocks on screen are always
/// ready and checked for changes after every rule frame; with time to spare in a frame the missing
/// blocks of the ring around them are drawn ahead, so panning seldom waits (a block drawn ahead is
/// checked once it comes on screen); the least recently used blocks beyond a budget are freed, so
/// memory stays bounded however far the camera goes.
/// </summary>
public partial class WorldView
{
    /// <summary>Cells per side of a terrain block.</summary>
    public const int BlockCells = TerrainTiling.BlockCells;

    /// <summary>Milliseconds a frame may spend drawing blocks ahead of the camera.</summary>
    private const double AheadBudgetMilliseconds = 3;

    private readonly Dictionary<(int X, int Y), TerrainBlock> blocks = new();
    private readonly List<TerrainBlock> byAge = new();
    private IReadOnlyList<(int X, int Y)> ahead = Array.Empty<(int X, int Y)>();
    private (int X0, int Y0, int X1, int Y1) aheadOf = (0, 0, -1, -1);
    private bool faultTold;
    private TerrainLayer terrain = null!;
    private FastRaster rasterizer = null!;
    private TerrainCanvas terrainCanvas = null!;
    private FastRaster.Target blockPixels = null!;
    private byte[] blockBytes = Array.Empty<byte>();
    private Image blockImage = null!;
    private int terrainVersion = 1;
    private int terrainFrame;

    /// <summary>The terrain blocks drawn (for the terrain canvas).</summary>
    internal IEnumerable<TerrainBlock> Blocks => blocks.Values;

    /// <summary>Terrain blocks held in memory now.</summary>
    public int TerrainBlocksKept => blocks.Count;

    /// <summary>Milliseconds the terrain took in the last frame (checking, drawing and freeing blocks).</summary>
    public double TerrainMilliseconds { get; private set; }

    private void ReadyTerrain()
    {
        terrain = new TerrainLayer(art.Assets);
        rasterizer = new FastRaster(art.Pixels);
        blockPixels = new FastRaster.Target(BlockCells * CellWidth, BlockCells * CellHeight);
        blockBytes = new byte[blockPixels.Width * blockPixels.Height * 4];
        blockImage = Image.CreateEmpty(blockPixels.Width, blockPixels.Height, false, Image.Format.Rgba8);
        terrainCanvas = new TerrainCanvas { Name = "Terrain", View = this };
        AddChild(terrainCanvas);
    }

    /// <summary>
    /// Makes the terrain blocks under <paramref name="view"/> and redraws those whose cells changed
    /// since they were last checked (a block off screen is checked again when it comes back); then,
    /// while the frame has time, the blocks just around the view.
    /// </summary>
    private void UpdateTerrain(ViewRect view)
    {
        terrainFrame++;
        long start = Stopwatch.GetTimestamp();
        (int bx0, int by0, int bx1, int by1) = TerrainTiling.BlocksFor(view);
        bool changed = false;
        for (int by = by0; by <= by1; by++)
        {
            for (int bx = bx0; bx <= bx1; bx++)
            {
                changed |= Bring(bx, by, onScreen: true);
            }
        }
        if (aheadOf != (bx0, by0, bx1, by1))
        {
            // The ring is worked out again only when the view moves to other blocks.
            aheadOf = (bx0, by0, bx1, by1);
            ahead = TerrainTiling.AheadOf(view, 1);
        }
        for (int i = 0; i < ahead.Count; i++)
        {
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= AheadBudgetMilliseconds)
            {
                break;
            }
            changed |= Bring(ahead[i].X, ahead[i].Y, onScreen: false);
        }
        int visible = (bx1 - bx0 + 1) * (by1 - by0 + 1);
        if (DropOldBlocks(TerrainTiling.BlocksKept(visible + ahead.Count)))
        {
            changed = true;
        }
        TerrainMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (changed)
        {
            terrainCanvas.QueueRedraw();
        }
    }

    /// <summary>
    /// Makes block (<paramref name="bx"/>, <paramref name="by"/>) current (a block ahead of the
    /// view is only made when missing); true when its picture changed.
    /// </summary>
    private bool Bring(int bx, int by, bool onScreen)
    {
        bool changed = false;
        if (!blocks.TryGetValue((bx, by), out TerrainBlock? block))
        {
            block = new TerrainBlock(bx, by);
            blocks[(bx, by)] = block;
            changed = true;
        }
        block.LastUsed = terrainFrame;
        if (block.Version != terrainVersion && (onScreen || block.Version == 0))
        {
            block.Version = terrainVersion;
            long signature = Signature(bx, by);
            if (signature != block.Signature)
            {
                Redraw(block);
                block.Signature = signature;
                changed = true;
            }
        }
        return changed;
    }

    private long Signature(int bx, int by)
    {
        CellRect cells = TerrainTiling.CellsOf(bx, by).Inflate(TerrainLayer.Margin);
        return TerrainLayer.Signature(World!, cells.Left, cells.Top, cells.Right, cells.Bottom, sight!);
    }

    /// <summary>Rasterises a block, or marks it black when it and its margin hold no land.</summary>
    private void Redraw(TerrainBlock block)
    {
        CellRect cells = TerrainTiling.CellsOf(block.X, block.Y);
        CellRect drawn = cells.Inflate(TerrainLayer.Margin).Intersect(MapArea.Bounds(World!.Map));
        block.AllBlack = drawn.IsEmpty || (MapArea.IsChunked(World.Map) && !HasLand(drawn));
        if (block.AllBlack)
        {
            block.Texture?.Dispose();
            block.Texture = null;
            return;
        }
        TerrainRedraws++;
        blockPixels.ClearOpaque(0);
        try
        {
            terrain.Draw(World, rasterizer, blockPixels, 0, 0, cells.Left, cells.Top, cells.Right, cells.Bottom, sight!);
        }
        catch (Exception e) when (ObjectLayer.IsDrawingFault(e) || e is NullReferenceException or KeyNotFoundException)
        {
            // A picture that cannot be drawn leaves the rest of the block as far as it got.
            if (!faultTold)
            {
                faultTold = true;
                GD.PushWarning($"terrain block {block.X},{block.Y}: {e.Message}");
            }
        }
        WorldArt.CopyTo(blockPixels.Pixels, blockBytes);
        blockImage.SetData(blockPixels.Width, blockPixels.Height, false, Image.Format.Rgba8, blockBytes);
        if (block.Texture == null)
        {
            block.Texture = ImageTexture.CreateFromImage(blockImage);
        }
        else
        {
            block.Texture.Update(blockImage);
        }
    }

    /// <summary>Whether any cell of <paramref name="cells"/> is land (an endless map's land not made yet is not).</summary>
    private bool HasLand(CellRect cells)
    {
        for (int y = cells.Top; y < cells.Bottom; y++)
        {
            for (int x = cells.Left; x < cells.Right; x++)
            {
                if (MapArea.InPlay(World!.Map, x, y))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Frees the least recently used blocks beyond <paramref name="kept"/>; true when any went.</summary>
    private bool DropOldBlocks(int kept)
    {
        if (blocks.Count <= kept)
        {
            return false;
        }
        byAge.Clear();
        byAge.AddRange(blocks.Values);
        byAge.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed));
        int dropped = 0;
        for (int i = 0; i < byAge.Count && blocks.Count > kept; i++)
        {
            TerrainBlock block = byAge[i];
            if (block.LastUsed == terrainFrame)
            {
                break;
            }
            block.Texture?.Dispose();
            blocks.Remove((block.X, block.Y));
            dropped++;
        }
        byAge.Clear();
        return dropped > 0;
    }

    /// <summary>Forgets every block (another world).</summary>
    private void ClearTerrain()
    {
        foreach (TerrainBlock block in blocks.Values)
        {
            block.Texture?.Dispose();
        }
        blocks.Clear();
        terrainCanvas?.QueueRedraw();
    }

    /// <summary>A block of terrain cells rasterised into one texture.</summary>
    internal sealed class TerrainBlock
    {
        public TerrainBlock(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        /// <summary>The block's texture; null while it is all black.</summary>
        public ImageTexture? Texture { get; set; }

        /// <summary>The block and its margin hold no land.</summary>
        public bool AllBlack { get; set; }

        public long Signature { get; set; } = long.MinValue;

        public int Version { get; set; }

        public int LastUsed { get; set; }
    }

    /// <summary>Draws the terrain blocks.</summary>
    private sealed partial class TerrainCanvas : Node2D
    {
        public WorldView? View { get; set; }

        public override void _Draw()
        {
            if (View == null)
            {
                return;
            }
            var size = new Vector2(BlockCells * CellWidth, BlockCells * CellHeight);
            foreach (TerrainBlock block in View.Blocks)
            {
                var at = new Vector2(block.X * size.X, block.Y * size.Y);
                if (block.Texture != null)
                {
                    DrawTexture(block.Texture, at);
                }
                else if (block.AllBlack)
                {
                    DrawRect(new Rect2(at, size), Colors.Black);
                }
            }
        }
    }
}
