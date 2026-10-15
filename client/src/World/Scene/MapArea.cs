using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// Which cells of a map the view works with. A campaign or custom map is its part in play, from
/// cell (0, 0); an endless map grows in chunks in every direction (cells can be negative), most of
/// its bounding box may not be land yet, and the camera stays over what the viewer has explored.
/// Everything the view and the minimap ask about the extent of a map goes through here.
/// </summary>
public static class MapArea
{
    /// <summary>Cells around the explored land of an endless map that the camera and the minimap still show.</summary>
    public const int ExploredMargin = 4;

    /// <summary>Whether <paramref name="map"/> is an endless map made of chunks.</summary>
    public static bool IsChunked(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.IsChunked;
    }

    /// <summary>The cells the map can have: the part in play, or the box around an endless map's chunks.</summary>
    public static CellRect Bounds(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Of(map.Bounds);
    }

    /// <summary>Whether cell (<paramref name="x"/>, <paramref name="y"/>) is land in play (on an endless map: its chunk is made).</summary>
    public static bool InPlay(WorldMap map, int x, int y) => map.InPlay(new GridPoint(x, y));

    /// <summary>
    /// The box around the cells <paramref name="viewer"/> has explored on an endless map; the
    /// whole map otherwise.
    /// </summary>
    public static CellRect Explored(GameWorld world, int viewer)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!IsChunked(world.Map))
        {
            return Bounds(world.Map);
        }
        return viewer >= 0 && viewer < world.Tribes.Count ? Of(world.Fog(viewer).ExploredBounds) : CellRect.Empty;
    }

    /// <summary>
    /// The box around the cells explored in <paramref name="fog"/> (a viewer's sight) on an endless map;
    /// the whole map otherwise.
    /// </summary>
    public static CellRect Explored(GameWorld world, IFogView fog)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fog);
        return IsChunked(world.Map) ? Of(fog.ExploredBounds) : Bounds(world.Map);
    }

    /// <summary>The rectangle of the rules' <paramref name="rect"/>.</summary>
    public static CellRect Of(GridRect rect) => rect.IsEmpty ? CellRect.Empty : new CellRect(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>The cells the camera may show: see <see cref="CameraArea(CellRect, CellRect, bool, int)"/>.</summary>
    public static CellRect CameraArea(GameWorld world, int viewer)
    {
        ArgumentNullException.ThrowIfNull(world);
        bool chunked = IsChunked(world.Map);
        return CameraArea(Bounds(world.Map), chunked ? Explored(world, viewer) : CellRect.Empty, chunked, ExploredMargin);
    }

    /// <summary>The cells the camera may show with <paramref name="fog"/> (a viewer's sight): see <see cref="CameraArea(CellRect, CellRect, bool, int)"/>.</summary>
    public static CellRect CameraArea(GameWorld world, IFogView fog)
    {
        ArgumentNullException.ThrowIfNull(world);
        bool chunked = IsChunked(world.Map);
        return CameraArea(Bounds(world.Map), chunked ? Explored(world, fog) : CellRect.Empty, chunked, ExploredMargin);
    }

    /// <summary>
    /// The cells the camera may show: the whole map of a campaign or custom game; on an endless map
    /// the explored box grown by <paramref name="margin"/> cells (within the map's box), or the
    /// whole box while nothing is explored.
    /// </summary>
    public static CellRect CameraArea(CellRect bounds, CellRect explored, bool chunked, int margin)
    {
        if (!chunked || explored.IsEmpty)
        {
            return bounds;
        }
        CellRect area = explored.Inflate(Math.Max(0, margin)).Intersect(bounds);
        return area.IsEmpty ? explored : area;
    }
}
