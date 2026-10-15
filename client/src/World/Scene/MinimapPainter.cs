using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// Paints an endless map's minimap picture (see <see cref="MinimapLayout"/>) in the colours of the
/// original's minimap: land by terrain class (darker where the viewer explored it but does not see
/// it now, earth brown where it never was), the viewer's own buildings orange and units yellow,
/// every other tribe's in its tribe colour (buildings darker), only where the viewer sees them.
/// The land can be painted a band of rows at a time, so a large picture is spread over frames.
/// </summary>
public sealed class MinimapPainter
{
    /// <summary>Never explored land.</summary>
    public const uint Unexplored = 0x5c4e2d;

    /// <summary>The viewer's units and buildings.</summary>
    public const uint OwnUnit = 0xffd800, OwnBuilding = 0xff9600;

    /// <summary>Other tribes' buildings in land explored but not seen now.</summary>
    public const uint OtherBuildingDim = 0x530000;

    /// <summary>Land by terrain class, seen now.</summary>
    public static readonly IReadOnlyList<uint> Terrain = Array.AsReadOnly(new uint[] { 0x6ea236, 0xb19423, 0x2877b0, 0x25731c, 0x3b3b30, 0x464646 });

    /// <summary>Land by terrain class, explored but not seen now.</summary>
    public static readonly IReadOnlyList<uint> TerrainDim = Array.AsReadOnly(new uint[] { 0x508418, 0x866f16, 0x0a5992, 0x075500, 0x1d1d12, 0x282828 });

    private readonly Func<int, uint> tribeSwatch;

    /// <summary>A painter giving other tribes the colours <paramref name="tribeSwatch"/> returns (0xRRGGBB).</summary>
    public MinimapPainter(Func<int, uint> tribeSwatch)
    {
        this.tribeSwatch = tribeSwatch ?? throw new ArgumentNullException(nameof(tribeSwatch));
    }

    /// <summary>
    /// Paints picture rows [<paramref name="rowFrom"/>, <paramref name="rowTo"/>) of the land
    /// into <paramref name="rgba"/> (4 bytes per pixel, picture width per row).
    /// </summary>
    public void PaintLand(GameWorld world, int viewer, MinimapLayout layout, byte[] rgba, int rowFrom, int rowTo) =>
        PaintLand(world, ViewerSight.Of(world, viewer), layout, rgba, rowFrom, rowTo);

    /// <summary>The land as <paramref name="sight"/> sees it (see <see cref="PaintLand(GameWorld, int, MinimapLayout, byte[], int, int)"/>).</summary>
    public void PaintLand(GameWorld world, ViewerSight sight, MinimapLayout layout, byte[] rgba, int rowFrom, int rowTo)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(rgba);
        CheckSize(layout, rgba);
        IFogView fog = sight;
        IReadOnlyList<int> classes = world.Content.Rules.Interface.MinimapColors;
        int last = Math.Min(rowTo, layout.PictureHeight);
        for (int py = Math.Max(0, rowFrom); py < last; py++)
        {
            for (int px = 0; px < layout.PictureWidth; px++)
            {
                (int x, int y) = layout.CellOfPixel(px, py);
                uint colour = Unexplored;
                var cell = new GridPoint(x, y);
                if (MapArea.InPlay(world.Map, x, y) && fog.IsExplored(cell))
                {
                    int tile = world.Map.TileAt(cell);
                    int terrain = tile >= 0 && tile < classes.Count ? Math.Clamp(classes[tile], 0, Terrain.Count - 1) : 0;
                    colour = fog.IsVisible(cell) ? Terrain[terrain] : TerrainDim[terrain];
                }
                Put(rgba, layout, px, py, colour);
            }
        }
    }

    /// <summary>
    /// Paints the buildings and the units over the land; <paramref name="isInside"/> tells the
    /// units inside a building (not shown).
    /// </summary>
    public void PaintMarkers(GameWorld world, int viewer, MinimapLayout layout, byte[] rgba, Func<Unit, bool> isInside) =>
        PaintMarkers(world, ViewerSight.Of(world, viewer), layout, rgba, isInside);

    /// <summary>
    /// The buildings and units as <paramref name="sight"/> sees them: its own (a player's) always,
    /// the others' where it sees them; hidden units only of the tribes it knows.
    /// </summary>
    public void PaintMarkers(GameWorld world, ViewerSight sight, MinimapLayout layout, byte[] rgba, Func<Unit, bool> isInside)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(isInside);
        CheckSize(layout, rgba);
        PaintBuildings(world, sight, layout, rgba);
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            if (unit.HitPoints <= 0 || !layout.Window.Contains(unit.X, unit.Y) || !MapArea.InPlay(world.Map, unit.X, unit.Y))
            {
                continue;
            }
            bool own = sight.IsOwn(unit.Tribe);
            bool hidden = (unit.Status & UnitStatus.Hidden) != 0 && !sight.Includes(unit.Tribe);
            if ((!own && (hidden || !sight.IsVisible(new GridPoint(unit.X, unit.Y)))) || isInside(unit))
            {
                continue;
            }
            PutCell(rgba, layout, unit.X, unit.Y, own ? OwnUnit : tribeSwatch(unit.Tribe));
        }
    }

    private void PaintBuildings(GameWorld world, ViewerSight sight, MinimapLayout layout, byte[] rgba)
    {
        var kinds = world.Content.Rules.Buildings.Kinds;
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            Building b = world.Building(id);
            if (!b.Stands || b.Kind < 0 || b.Kind >= kinds.Count)
            {
                continue;
            }
            var kind = kinds[b.Kind];
            if (b.X + kind.Width <= layout.Window.Left || b.X >= layout.Window.Right
                || b.Y + kind.Height <= layout.Window.Top || b.Y >= layout.Window.Bottom)
            {
                continue;
            }
            bool own = sight.IsOwn(b.Tribe);
            if (!own && !sight.ShowsAsItIs(b))
            {
                // Out of sight in a match: shown below as remembered, if it is.
                continue;
            }
            uint seen = own ? OwnBuilding : Darker(tribeSwatch(b.Tribe));
            for (int dy = 0; dy < kind.Height; dy++)
            {
                for (int dx = 0; dx < kind.Width; dx++)
                {
                    int x = b.X + dx, y = b.Y + dy;
                    var cell = new GridPoint(x, y);
                    if (!MapArea.InPlay(world.Map, x, y) || (!own && !sight.IsExplored(cell)))
                    {
                        continue;
                    }
                    PutCell(rgba, layout, x, y, own || sight.IsVisible(cell) ? seen : OtherBuildingDim);
                }
            }
        }
        if (sight.RemembersBuildings)
        {
            PaintRemembered(world, sight, layout, rgba);
        }
    }

    /// <summary>The buildings out of sight as the viewer last saw them (a match), darkened.</summary>
    private static void PaintRemembered(GameWorld world, ViewerSight sight, MinimapLayout layout, byte[] rgba)
    {
        foreach (RememberedBuilding seen in sight.Buildings.Buildings)
        {
            GridRect cells = seen.Footprint(world.Content);
            if (seen.HitPoints <= 0 || sight.IsOwn(seen.Tribe) || cells.Right <= layout.Window.Left || cells.Left >= layout.Window.Right
                || cells.Bottom <= layout.Window.Top || cells.Top >= layout.Window.Bottom || sight.AnyVisible(cells))
            {
                continue;
            }
            for (int y = cells.Top; y < cells.Bottom; y++)
            {
                for (int x = cells.Left; x < cells.Right; x++)
                {
                    if (MapArea.InPlay(world.Map, x, y) && sight.IsExplored(new GridPoint(x, y)))
                    {
                        PutCell(rgba, layout, x, y, OtherBuildingDim);
                    }
                }
            }
        }
    }

    /// <summary>A tribe colour as other tribes' buildings show it (40% darker, like the network minimap).</summary>
    public static uint Darker(uint rgb)
    {
        static uint Channel(uint rgb, int shift) => (uint)Math.Round(((rgb >> shift) & 0xFF) * 0.6) << shift;
        return Channel(rgb, 16) | Channel(rgb, 8) | Channel(rgb, 0);
    }

    private static void PutCell(byte[] rgba, MinimapLayout layout, int x, int y, uint colour)
    {
        if (layout.PixelOfCell(x, y) is { } pixel)
        {
            Put(rgba, layout, pixel.X, pixel.Y, colour);
        }
    }

    private static void Put(byte[] rgba, MinimapLayout layout, int px, int py, uint colour)
    {
        int i = ((py * layout.PictureWidth) + px) * 4;
        rgba[i] = (byte)(colour >> 16);
        rgba[i + 1] = (byte)(colour >> 8);
        rgba[i + 2] = (byte)colour;
        rgba[i + 3] = 255;
    }

    private static void CheckSize(MinimapLayout layout, byte[] rgba)
    {
        if (rgba.Length < layout.PictureWidth * layout.PictureHeight * 4)
        {
            throw new ArgumentException($"{rgba.Length} bytes do not hold a {layout.PictureWidth}x{layout.PictureHeight} picture", nameof(rgba));
        }
    }
}
