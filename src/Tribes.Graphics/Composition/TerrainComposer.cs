using System;
using System.Collections.Generic;

namespace Tribes.Graphics.Composition;

/// <summary>A rectangle of map cells to draw, with the tiles under it.</summary>
/// <param name="Columns">Width of the view in cells.</param>
/// <param name="Rows">Height of the view in cells.</param>
/// <param name="TileAt">Terrain tile (0..255) at a map cell (x, y).</param>
/// <param name="FirstColumn">Map column of the view's left cell.</param>
/// <param name="FirstRow">Map row of the view's top cell.</param>
public sealed record TerrainView(int Columns, int Rows, Func<int, int, int> TileAt, int FirstColumn, int FirstRow);

/// <summary>
/// Composes the terrain layer: tiles below 175 are composite sprites drawn at the cell's
/// top-left corner; tiles from 175 are object graphics whose ground run is drawn there.
/// </summary>
public sealed class TerrainComposer
{
    /// <summary>First tile of the obstacles (trees, rocks) with a part in the object layer.</summary>
    public const int FirstOverlayTile = 180;

    /// <summary>Last tile of the obstacle range.</summary>
    public const int LastOverlayTile = 231;

    /// <summary>A single tile outside the obstacle range that also has a part in the object layer.</summary>
    public const int ExtraOverlayTile = 243;

    private readonly SpriteComposer sprites;
    private readonly ObjectComposer objects;

    /// <summary>Creates a composer drawing through <paramref name="sprites"/> and <paramref name="objects"/>.</summary>
    public TerrainComposer(SpriteComposer sprites, ObjectComposer objects)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(objects);
        this.sprites = sprites;
        this.objects = objects;
    }

    /// <summary>Whether the original draws a part of <paramref name="tile"/> in the object layer too.</summary>
    public static bool HasOverlay(int tile) =>
        (tile >= FirstOverlayTile && tile <= LastOverlayTile) || tile == ExtraOverlayTile;

    /// <summary>The terrain-layer parts of one cell showing <paramref name="tile"/>, relative to the cell's top-left corner.</summary>
    public DrawList ComposeTile(int tile, int paletteVariant = PaletteVariants.None)
    {
        if (tile < ObjectGraphicIds.FirstObjectTile)
        {
            return sprites.ComposeSprite(tile, paletteVariant);
        }
        return objects.Compose(ObjectRequest.TerrainObject(tile, ObjectPass.Ground) with { PaletteVariant = paletteVariant }).Drawing;
    }

    /// <summary>
    /// The object-layer parts of <paramref name="tile"/> (empty unless <see cref="HasOverlay"/>):
    /// trees and rocks above the terrain, with their animations at <paramref name="tick"/>.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="tick">Game frame counter.</param>
    /// <param name="mapRow">Map row of the cell; it shifts the ambient animations' clocks.</param>
    public DrawList ComposeOverlay(int tile, int tick, int mapRow)
    {
        if (!HasOverlay(tile))
        {
            return DrawList.Empty;
        }
        return objects.Compose(ObjectRequest.TerrainObject(tile, ObjectPass.Upper) with { Tick = tick, MapRow = mapRow }).Drawing;
    }

    /// <summary>
    /// The terrain layer of a view: every cell in rows from the top, each row from the left,
    /// positioned at <c>(column * cellWidth, row * cellHeight)</c> from the view's top-left corner.
    /// </summary>
    public IReadOnlyList<DrawPart> ComposeView(TerrainView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var parts = new List<DrawPart>();
        int variant = PaletteVariants.None;
        int cellWidth = sprites.Catalog.Cell.Width;
        int cellHeight = sprites.Catalog.Cell.Height;
        for (int row = 0; row < view.Rows; row++)
        {
            for (int column = 0; column < view.Columns; column++)
            {
                int tile = view.TileAt(view.FirstColumn + column, view.FirstRow + row) & byte.MaxValue;
                DrawList cell = ComposeTile(tile, variant);
                variant = cell.PaletteVariantAfter;
                foreach (DrawPart part in cell.Parts)
                {
                    parts.Add(part.Offset(column * cellWidth, row * cellHeight));
                }
            }
        }
        return parts;
    }
}
