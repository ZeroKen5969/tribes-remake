using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>The rectangle a camp covers with its margin, relative to the camp's origin (right and bottom exclusive).</summary>
internal readonly record struct SiteShape(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The shape of <paramref name="template"/>'s camp with <paramref name="margin"/> cells around it.</summary>
    public static SiteShape Of(GameWorld world, CampTemplate template, int margin)
    {
        int left = 0, top = 0, right = 1, bottom = 1;
        foreach (InitialObject item in template.Objects)
        {
            int width = 1, height = 1;
            if (item.Kind == InitialObjectKind.Building)
            {
                BuildingKind kind = world.Content.Rules.Buildings.Kinds[item.BuildingKind ?? 0];
                (width, height) = (kind.Width, kind.Height);
            }
            left = Math.Min(left, item.Cell.X);
            top = Math.Min(top, item.Cell.Y);
            right = Math.Max(right, item.Cell.X + width);
            bottom = Math.Max(bottom, item.Cell.Y + height);
        }
        return new SiteShape(left - margin, top - margin, right + margin, bottom + margin);
    }
}

/// <summary>
/// What the ground of a chunk allows a camp: for every cell of the chunk and around it whether a
/// camp may cover it, and which cells of the chunk are joined to its connectors, as sums over
/// rectangles so that each place is checked at once.
/// </summary>
internal sealed class SiteGround
{
    private readonly SiteShape shape;
    private readonly GridPoint windowOrigin;
    private readonly int width, height;
    private readonly int[] blocked;
    private readonly int[] joined;

    /// <summary>Looks at the ground of <paramref name="chunk"/> for camps of <paramref name="shape"/>.</summary>
    public SiteGround(GameWorld world, ChunkCoord chunk, SiteShape shape)
    {
        this.shape = shape;
        GridPoint origin = chunk.Origin;
        windowOrigin = new GridPoint(origin.X + shape.Left - 1, origin.Y + shape.Top - 1);
        width = WorldMap.ChunkSize + shape.Right - shape.Left + 2;
        height = WorldMap.ChunkSize + shape.Bottom - shape.Top + 2;
        blocked = new int[(width + 1) * (height + 1)];
        joined = new int[(width + 1) * (height + 1)];
        bool[] reached = JoinedCells(world, chunk);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new GridPoint(windowOrigin.X + x, windowOrigin.Y + y);
                int local = LocalOf(chunk, cell);
                Sum(blocked, x, y, Covers(world, cell) ? 0 : 1);
                Sum(joined, x, y, local >= 0 && reached[local] ? 1 : 0);
            }
        }
    }

    /// <summary>Whether a camp at <paramref name="origin"/> fits: nothing in its way, and it touches land joined to the connectors.</summary>
    public bool Fits(GridPoint origin)
    {
        int left = origin.X + shape.Left - windowOrigin.X, top = origin.Y + shape.Top - windowOrigin.Y;
        int right = origin.X + shape.Right - windowOrigin.X, bottom = origin.Y + shape.Bottom - windowOrigin.Y;
        return Count(blocked, left, top, right, bottom) == 0 && Count(joined, left - 1, top - 1, right + 1, bottom + 1) > 0;
    }

    /// <summary>Whether a camp may cover <paramref name="cell"/>: land made, open ground or objects a camp clears away, nothing else there.</summary>
    private static bool Covers(GameWorld world, GridPoint cell)
    {
        if (!world.Map.InPlay(cell))
        {
            return false;
        }
        int tile = world.Map.TileAt(cell);
        if (world.Content.Terrain[tile].BlocksMovement && !CampSites.IsObjectTile(world.Content.Terrain, tile))
        {
            return false;
        }
        return world.Map.CellAt(cell).Content is CellContent.Empty or CellContent.Thicket or CellContent.Rocks or CellContent.ResourceNode;
    }

    /// <summary>The cells of the chunk a unit can reach from its connectors (units and animals do not block).</summary>
    private static bool[] JoinedCells(GameWorld world, ChunkCoord chunk)
    {
        var reached = new bool[WorldMap.ChunkSize * WorldMap.ChunkSize];
        var queue = new Queue<int>();
        GridPoint origin = chunk.Origin;
        foreach ((int x, int y) in ChunkConnectivity.ConnectorCells())
        {
            Reach(world, origin, x, y, reached, queue);
        }
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % WorldMap.ChunkSize, y = cell / WorldMap.ChunkSize;
            Reach(world, origin, x + 1, y, reached, queue);
            Reach(world, origin, x - 1, y, reached, queue);
            Reach(world, origin, x, y + 1, reached, queue);
            Reach(world, origin, x, y - 1, reached, queue);
        }
        return reached;
    }

    private static void Reach(GameWorld world, GridPoint origin, int x, int y, bool[] reached, Queue<int> queue)
    {
        if ((uint)x >= WorldMap.ChunkSize || (uint)y >= WorldMap.ChunkSize)
        {
            return;
        }
        int index = (y * WorldMap.ChunkSize) + x;
        if (reached[index])
        {
            return;
        }
        CellContent content = world.Map.CellAt(new GridPoint(origin.X + x, origin.Y + y)).Content;
        if (content is CellContent.Empty or CellContent.Unit or CellContent.Animal or CellContent.Reserved)
        {
            reached[index] = true;
            queue.Enqueue(index);
        }
    }

    private static int LocalOf(ChunkCoord chunk, GridPoint cell) =>
        ChunkCoord.Of(cell) == chunk ? MapChunk.LocalIndex(cell.X, cell.Y) : -1;

    /// <summary>Adds <paramref name="value"/> at (x, y) to a summed-area table of the window.</summary>
    private void Sum(int[] table, int x, int y, int value)
    {
        int stride = width + 1;
        table[((y + 1) * stride) + x + 1] = value + table[(y * stride) + x + 1] + table[((y + 1) * stride) + x] - table[(y * stride) + x];
    }

    /// <summary>The sum of the window cells from (left, top) up to, not including, (right, bottom), clipped to the window.</summary>
    private int Count(int[] table, int left, int top, int right, int bottom)
    {
        left = Math.Clamp(left, 0, width);
        right = Math.Clamp(right, 0, width);
        top = Math.Clamp(top, 0, height);
        bottom = Math.Clamp(bottom, 0, height);
        if (left >= right || top >= bottom)
        {
            return 0;
        }
        int stride = width + 1;
        return table[(bottom * stride) + right] - table[(top * stride) + right] - table[(bottom * stride) + left] + table[(top * stride) + left];
    }
}
