using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>What a piece draws.</summary>
public enum PieceShape
{
    /// <summary>Nothing (piece 0, or an unused shape code).</summary>
    None,

    /// <summary>A rectangle copied from a sprite sheet.</summary>
    Image,

    /// <summary>A one-pixel line from (<see cref="Piece.X"/>, <see cref="Piece.Y"/>) to (<see cref="Piece.EndX"/>, <see cref="Piece.EndY"/>).</summary>
    Line,

    /// <summary>A one-pixel rectangle outline covering <c>Width + 1</c> by <c>Height + 1</c> pixels.</summary>
    Outline,

    /// <summary>A filled rectangle; position and size are multiplied by <see cref="Piece.Scale"/>.</summary>
    Fill,
}

/// <summary>
/// The smallest drawable unit of the original renderer: a sheet rectangle or a solid primitive.
/// Positions are relative to the point the piece is drawn at; an image's top-left corner lands there.
/// Fields that do not apply to the shape are null.
/// </summary>
/// <param name="Id">Piece number (0..1809).</param>
/// <param name="Shape">What the piece draws.</param>
/// <param name="X">Image: source x. Line: start x. Outline and fill: left edge.</param>
/// <param name="Y">Image: source y. Line: start y. Outline and fill: top edge.</param>
/// <param name="Width">Image: source width. Outline and fill: width.</param>
/// <param name="Height">Image: source height. Outline and fill: height.</param>
/// <param name="Sheet">Image: the sprite sheet number (0..26).</param>
/// <param name="EndX">Line: end x.</param>
/// <param name="EndY">Line: end y.</param>
/// <param name="Color">Line, outline and fill: index into <see cref="SpriteCatalog.PrimitiveColors"/>.</param>
/// <param name="Scale">Fill: 1, or 4 for the large background fills of the menus.</param>
public sealed record Piece(int Id, PieceShape Shape, int X = 0, int Y = 0, int Width = 0, int Height = 0,
    int? Sheet = null, int? EndX = null, int? EndY = null, int? Color = null, int? Scale = null);

/// <summary>A piece placed relative to the origin of a composite sprite.</summary>
/// <param name="Piece">The piece drawn.</param>
/// <param name="X">Horizontal offset from the sprite's origin.</param>
/// <param name="Y">Vertical offset from the sprite's origin.</param>
public readonly record struct SpritePart(int Piece, int X, int Y);

/// <summary>
/// A composite sprite (the original nests composites; here they are flattened): the pieces it
/// draws, in drawing order, with their offsets from the sprite's origin. A drop shadow, when the
/// sprite has one, is the first part.
/// </summary>
/// <param name="Id">Sprite number (0..1720). Terrain tile <c>t</c> below 175 is drawn as sprite <c>t</c>.</param>
/// <param name="Parts">The pieces, in drawing order.</param>
public sealed record CompositeSprite(int Id, IReadOnlyList<SpritePart> Parts);
