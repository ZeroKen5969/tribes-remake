using System;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>
/// The map's ground as its terrain design gives it (tiles only, which never change while a game
/// runs): where camps and planned buildings may go in a match.
/// </summary>
internal static class MatchTerrain
{
    /// <summary>
    /// Whether <paramref name="cell"/> is open ground by its tile: in play, off the map's edge, and
    /// neither blocking terrain, rock, thicket, a resource node's tile nor shore.
    /// </summary>
    public static bool IsGround(GameWorld world, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        GridRect bounds = world.Map.Bounds;
        if (cell.X < bounds.Left + 1 || cell.Y < bounds.Top + 1 || cell.X > bounds.Right - 2 || cell.Y > bounds.Bottom - 2)
        {
            return false;
        }
        TerrainCatalog terrain = world.Content.Terrain;
        int tile = world.Map.TileAt(cell);
        TileKind kind = terrain[tile];
        return !kind.BlocksMovement && !kind.IsRock && !terrain.ObstacleTiles.Contains(tile)
            && !terrain.ResourceTiles.Contains(tile) && !terrain.IsShore(tile);
    }

    /// <summary><paramref name="cell"/> moved inside the part in play, off its edge.</summary>
    public static GridPoint Inside(GameWorld world, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        GridRect bounds = world.Map.Bounds;
        return new GridPoint(GridMath.Clamp(cell.X, bounds.Left + 1, Math.Max(bounds.Left + 1, bounds.Right - 2)),
            GridMath.Clamp(cell.Y, bounds.Top + 1, Math.Max(bounds.Top + 1, bounds.Bottom - 2)));
    }

    /// <summary>The nearest ground cell to <paramref name="centre"/> (itself first) within <paramref name="rounds"/> spiral rounds, or null.</summary>
    public static GridPoint? NearestGround(GameWorld world, GridPoint centre, int rounds)
    {
        if (IsGround(world, centre))
        {
            return centre;
        }
        int dx = 0, dy = 0, sign = 1;
        for (int round = 1; round <= rounds; round++, sign = -sign)
        {
            for (int step = 0; step < 2 * round; step++)
            {
                if (step < round)
                {
                    dx += sign;
                }
                else
                {
                    dy += sign;
                }
                var cell = new GridPoint(centre.X + dx, centre.Y + dy);
                if (IsGround(world, cell))
                {
                    return cell;
                }
            }
        }
        return null;
    }
}
