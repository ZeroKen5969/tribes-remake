using System.Collections.Generic;
using Tribes.Graphics.Composition;

namespace Tribes.Graphics.Model;

/// <summary>How a terrain tile is drawn.</summary>
public enum TileDrawing
{
    /// <summary>Tiles 0..174: the composite sprite with the tile's number.</summary>
    Sprite,

    /// <summary>Tiles 175..255: an object graphic (resource tiles, trees, rocks...).</summary>
    ObjectGraphic,
}

/// <summary>An animation in a terrain tile's object layer.</summary>
/// <param name="Effect">Effect number.</param>
/// <param name="X">Horizontal offset from the cell's top-left corner.</param>
/// <param name="Y">Vertical offset from the cell's top-left corner.</param>
/// <param name="Kind">Idle or ambient animation.</param>
/// <param name="AmbientCount">
/// Number of ambient animations of the tile up to and including this one. The animation's clock
/// is the game frame counter <c>t</c> when this is 0, otherwise
/// <c>(t + AmbientCount * mapRow + ClockConstant) mod 120</c> with the cell's map row.
/// </param>
/// <param name="ClockConstant">Constant term of the clock (see <paramref name="AmbientCount"/>).</param>
public sealed record TileAnimation(int Effect, int X, int Y, ObjectPartKind Kind, int AmbientCount, int ClockConstant);

/// <summary>One terrain tile.</summary>
/// <param name="Id">Tile number (0..255), as stored in the map grid.</param>
/// <param name="Drawing">Sprite or object graphic.</param>
/// <param name="Graphic">The sprite or object graphic number.</param>
/// <param name="Ground">
/// The terrain layer: parts drawn relative to the cell's top-left corner, in order. Parts may
/// reach into the neighbouring cells; the original draws the cells of a row from left to right
/// and the rows from the top.
/// </param>
/// <param name="Overlay">
/// The still parts drawn in the object layer (trees, rock tops), relative to the cell's top-left
/// corner; the original draws a row's cells from right to left there, after the terrain.
/// </param>
/// <param name="OverlayAnimations">The animations of the object layer.</param>
/// <param name="BlocksMovement">
/// Nothing can enter or be built on it: the sprite's obstacle flag, or any tile from 175 (the map
/// loader marks those cells; resource tiles then hold their resource node).
/// </param>
/// <param name="IsRock">A rock formation.</param>
/// <param name="UsedInMaps">Whether any original map contains the tile.</param>
public sealed record TerrainTile(int Id, TileDrawing Drawing, int Graphic, IReadOnlyList<DrawPart> Ground,
    IReadOnlyList<DrawPart> Overlay, IReadOnlyList<TileAnimation> OverlayAnimations, bool BlocksMovement, bool IsRock,
    bool UsedInMaps);

/// <summary>The terrain tiles (<c>data/client/terrain-tiles.json</c>).</summary>
/// <param name="Format">Format name and version.</param>
/// <param name="Cell">Cell size.</param>
/// <param name="RandomVariantLastTile">
/// Map tiles up to this number are drawn as the stored tile plus a random 0..<c>RandomVariantCount - 1</c>
/// chosen when the map is loaded (the maps store 0, 3 and 6).
/// </param>
/// <param name="RandomVariantCount">Number of random variants.</param>
/// <param name="Tiles">Tiles 0..255.</param>
public sealed record TerrainTileCatalog(string Format, CellSize Cell, int RandomVariantLastTile, int RandomVariantCount,
    IReadOnlyList<TerrainTile> Tiles)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-terrain-tiles/1";
}
