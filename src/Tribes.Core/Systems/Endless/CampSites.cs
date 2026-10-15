using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>Places that keep camps apart: points and how far a new camp must stay from them.</summary>
internal sealed class CampDistances
{
    private readonly Dictionary<ChunkCoord, List<GridPoint>> byChunk = new();
    private readonly int distance;

    /// <summary>Camps must stay at least <paramref name="distance"/> cells from every point added.</summary>
    public CampDistances(int distance) => this.distance = distance;

    /// <summary>Adds a point to keep away from.</summary>
    public void Add(GridPoint point)
    {
        ChunkCoord chunk = ChunkCoord.Of(point);
        if (!byChunk.TryGetValue(chunk, out List<GridPoint>? points))
        {
            byChunk[chunk] = points = new List<GridPoint>();
        }
        points.Add(point);
    }

    /// <summary>The squared distance from <paramref name="cell"/> to the nearest point, if it is within the distance; else <see cref="long.MaxValue"/>.</summary>
    public long NearestWithin(GridPoint cell)
    {
        int chunks = (distance >> WorldMap.ChunkShift) + 1;
        ChunkCoord at = ChunkCoord.Of(cell);
        long nearest = long.MaxValue;
        for (int cy = at.Y - chunks; cy <= at.Y + chunks; cy++)
        {
            for (int cx = at.X - chunks; cx <= at.X + chunks; cx++)
            {
                if (!byChunk.TryGetValue(new ChunkCoord(cx, cy), out List<GridPoint>? points))
                {
                    continue;
                }
                foreach (GridPoint point in points)
                {
                    long dx = point.X - cell.X, dy = point.Y - cell.Y;
                    nearest = Math.Min(nearest, (dx * dx) + (dy * dy));
                }
            }
        }
        return nearest;
    }

    /// <summary>Whether <paramref name="cell"/> is far enough from every point.</summary>
    public bool Allows(GridPoint cell) => NearestWithin(cell) >= (long)distance * distance;
}

/// <summary>
/// Camp sites of an endless world: a place where the whole camp (its buildings and units and a
/// margin of open ground) fits on ground that units can walk on, or that only thickets, rocks or
/// resource nodes cover, touching the land that is joined to the chunk's connectors. Making the
/// site ready clears those objects away.
/// </summary>
internal sealed class CampSites
{
    private readonly GameWorld world;

    /// <summary>Finds sites in <paramref name="world"/>.</summary>
    public CampSites(GameWorld world) => this.world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>Sites of a chunk looked at for room to build, the best first.</summary>
    private const int RoomTries = 16;

    /// <summary>
    /// How far from the nearest camp (in camp spacings, as a fraction 3/2) a site still counts as
    /// better; beyond it every site is as good and the one nearer the chunk's middle is taken (the
    /// land around it is made, so its room to build can be counted; sites at the edge of the made
    /// land seldom have it, and camps would always come as far away as the land allows).
    /// </summary>
    private const int FarEnoughNumerator = 3, FarEnoughDenominator = 2;

    private EndlessCampRules Rules => world.Content.Endless!.Camps;

    /// <summary>
    /// The best site with its origin in chunk <paramref name="chunk"/> allowed by every one of
    /// <paramref name="keepAway"/> and with room to build (the farthest from the nearest point of the
    /// first up to one and a half camp spacings, then the nearest the chunk's middle, then the first
    /// in rows), or null when the chunk has none.
    /// </summary>
    public GridPoint? FindIn(ChunkCoord chunk, params CampDistances[] keepAway)
    {
        ArgumentNullException.ThrowIfNull(keepAway);
        if (world.Map.Chunk(chunk) == null)
        {
            return null;
        }
        SiteShape shape = Shape();
        SiteGround? ground = null;
        GridPoint origin = chunk.Origin;
        var sites = new List<(GridPoint Site, long Score, long FromMiddle)>();
        long farEnough = (long)Rules.MinimumDistance * FarEnoughNumerator / FarEnoughDenominator;
        int middle = WorldMap.ChunkSize / 2;
        for (int y = 0; y < WorldMap.ChunkSize; y += Rules.CandidateStep)
        {
            for (int x = 0; x < WorldMap.ChunkSize; x += Rules.CandidateStep)
            {
                var candidate = new GridPoint(origin.X + x, origin.Y + y);
                // The distances are quick to check: the ground is looked at only where they allow a camp.
                if (!Array.TrueForAll(keepAway, k => k.Allows(candidate)))
                {
                    continue;
                }
                ground ??= new SiteGround(world, chunk, shape);
                if (!ground.Fits(candidate))
                {
                    continue;
                }
                long score = keepAway.Length == 0 ? 0 : Math.Min(keepAway[0].NearestWithin(candidate), farEnough * farEnough);
                long fromMiddle = ((long)(x - middle) * (x - middle)) + ((long)(y - middle) * (y - middle));
                sites.Add((candidate, score, fromMiddle));
            }
        }
        // The best first: far enough from the first points, then nearest the middle, then the first in rows.
        sites.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score)
            : a.FromMiddle != b.FromMiddle ? a.FromMiddle.CompareTo(b.FromMiddle)
            : a.Site.Y != b.Site.Y ? a.Site.Y.CompareTo(b.Site.Y) : a.Site.X.CompareTo(b.Site.X));
        for (int i = 0; i < sites.Count && i < RoomTries; i++)
        {
            if (HasRoom(sites[i].Site))
            {
                return sites[i].Site;
            }
        }
        return null;
    }

    /// <summary>Whether a camp at <paramref name="site"/> would have room to build on its own side (<see cref="CampRoom"/>).</summary>
    private bool HasRoom(GridPoint site)
    {
        int reach = CampRoom.BuildReach(world);
        var camps = new List<GridPoint>();
        CampIndex.For(world).Near(site, 2 * reach, (world, camps), static (s, tribe) =>
        {
            s.camps.Add(s.world.Tribes[tribe].Camp);
            return true;
        });
        camps.Add(site);
        return CampRoom.Count(world, camps, camps.Count - 1, -1, CampRoom.Needed) >= CampRoom.Needed;
    }

    /// <summary>
    /// Clears the site of the camp at <paramref name="origin"/>: thickets, rocks and resource nodes
    /// go, their tiles become open ground that fits the tiles around (<see cref="SiteTiles"/>).
    /// </summary>
    public void Clear(GridPoint origin)
    {
        SiteShape shape = Shape();
        TerrainCatalog terrain = world.Content.Terrain;
        var cleared = new List<GridPoint>();
        for (int y = shape.Top; y < shape.Bottom; y++)
        {
            for (int x = shape.Left; x < shape.Right; x++)
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                MapCell content = world.Map.CellAt(cell);
                if (content.Content == CellContent.ResourceNode)
                {
                    world.Map.ResourceNodes[content.Index].Kind = 0;
                }
                if (content.Content is CellContent.ResourceNode or CellContent.Thicket or CellContent.Rocks)
                {
                    world.Map.SetCell(cell, MapCell.Empty);
                }
                if (IsObjectTile(terrain, world.Map.TileAt(cell)))
                {
                    cleared.Add(cell);
                }
            }
        }
        SiteTiles.Replant(world.Map, world.Content, world.Endless!.Seed, cleared);
        ForgetLoneShores(origin, shape);
    }

    /// <summary>
    /// The last resort when land offers no site: a camp beside the top connector of
    /// <paramref name="chunk"/>, every cell of its site made open ground; null when even that
    /// place is taken (units, animals or buildings there).
    /// </summary>
    public GridPoint? ForceIn(ChunkCoord chunk)
    {
        SiteShape shape = Shape();
        var origin = new GridPoint(chunk.Origin.X + Maps.Generation.ChunkConnectivity.ConnectorStart - shape.Left,
            chunk.Origin.Y - shape.Top);
        var cells = new List<GridPoint>();
        for (int y = shape.Top; y < shape.Bottom; y++)
        {
            for (int x = shape.Left; x < shape.Right; x++)
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                cells.Add(cell);
                if (!world.Map.InPlay(cell) || world.Map.CellAt(cell).Content is CellContent.Unit or CellContent.Animal
                    or CellContent.Building or CellContent.Reserved)
                {
                    return null;
                }
            }
        }
        Clear(origin);
        foreach (GridPoint cell in cells)
        {
            world.Map.SetCell(cell, MapCell.Empty);
        }
        SiteTiles.Replant(world.Map, world.Content, world.Endless!.Seed, cells);
        return origin;
    }

    /// <summary>The cells a camp covers, with its margin, relative to its origin.</summary>
    internal SiteShape Shape() => SiteShape.Of(world, MatchStart.Template(world), Rules.SiteMargin);

    /// <summary>Whether a tile is a thicket, rock or resource node of the design (cleared away for a camp).</summary>
    internal static bool IsObjectTile(TerrainCatalog terrain, int tile) =>
        terrain[tile].IsRock || terrain.ObstacleTiles.Contains(tile) || terrain.ResourceTiles.Contains(tile);

    /// <summary>Shore cells beside the site whose node went: impassable again unless another shore-marking node is beside them.</summary>
    private void ForgetLoneShores(GridPoint origin, SiteShape shape)
    {
        for (int y = shape.Top; y < shape.Bottom; y++)
        {
            foreach (int x in new[] { shape.Left - 1, shape.Right })
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                if (world.Map.CellAt(cell).Content == CellContent.Shore && !BesideShoreNode(cell))
                {
                    world.Map.SetCell(cell, MapCell.Of(CellContent.Impassable));
                }
            }
        }
    }

    private bool BesideShoreNode(GridPoint cell)
    {
        foreach (int side in new[] { -1, 1 })
        {
            MapCell next = world.Map.CellAt(new GridPoint(cell.X + side, cell.Y));
            if (next.Content == CellContent.ResourceNode
                && world.Content.ResourceNodeKinds[world.Map.ResourceNodes[next.Index].Kind].MarksShore)
            {
                return true;
            }
        }
        return false;
    }
}
