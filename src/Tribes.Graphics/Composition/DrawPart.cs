using System.Text.Json.Serialization;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Composition;

/// <summary>What a <see cref="DrawPart"/> draws.</summary>
public enum DrawPartKind
{
    /// <summary>Copy a sheet rectangle; transparent pixels are skipped.</summary>
    Image,

    /// <summary>A one-pixel line, both end points included.</summary>
    Line,

    /// <summary>A one-pixel rectangle outline covering <c>Width + 1</c> by <c>Height + 1</c> pixels.</summary>
    Outline,

    /// <summary>A filled rectangle.</summary>
    Fill,
}

/// <summary>
/// One thing to draw, positioned relative to the anchor the composition was made for. Parts are
/// drawn in list order, later parts over earlier ones.
/// </summary>
/// <param name="Kind">What is drawn.</param>
/// <param name="Sheet">Image: id of the sheet to copy from (a <see cref="SheetInfo.Id"/>), palette variant already applied.</param>
/// <param name="SourceX">Image: left edge of the source rectangle.</param>
/// <param name="SourceY">Image: top edge of the source rectangle.</param>
/// <param name="Width">Image, outline, fill: width.</param>
/// <param name="Height">Image, outline, fill: height.</param>
/// <param name="X">Left edge (image, outline, fill) or start x (line).</param>
/// <param name="Y">Top edge (image, outline, fill) or start y (line).</param>
/// <param name="EndX">Line: end x.</param>
/// <param name="EndY">Line: end y.</param>
/// <param name="Color">Line, outline, fill: the colour (null for images).</param>
/// <param name="FlipX">Mirror the image horizontally. The original never mirrors, so this is always false.</param>
/// <param name="Piece">The piece this part comes from, or -1 for parts the rules add themselves.</param>
public readonly record struct DrawPart(DrawPartKind Kind, string? Sheet, int SourceX, int SourceY, int Width,
    int Height, int X, int Y,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int EndX,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int EndY,
    RgbColor? Color,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FlipX,
    int Piece)
{
    /// <summary>An image part.</summary>
    public static DrawPart Image(string sheet, int sourceX, int sourceY, int width, int height, int x, int y, int piece) =>
        new(DrawPartKind.Image, sheet, sourceX, sourceY, width, height, x, y, 0, 0, null, false, piece);

    /// <summary>A line part.</summary>
    public static DrawPart Line(int x, int y, int endX, int endY, RgbColor color, int piece) =>
        new(DrawPartKind.Line, null, 0, 0, 0, 0, x, y, endX, endY, color, false, piece);

    /// <summary>An outline part.</summary>
    public static DrawPart Outline(int x, int y, int width, int height, RgbColor color, int piece) =>
        new(DrawPartKind.Outline, null, 0, 0, width, height, x, y, 0, 0, color, false, piece);

    /// <summary>A fill part.</summary>
    public static DrawPart Fill(int x, int y, int width, int height, RgbColor color, int piece) =>
        new(DrawPartKind.Fill, null, 0, 0, width, height, x, y, 0, 0, color, false, piece);

    /// <summary>The same part moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public DrawPart Offset(int dx, int dy) => Kind == DrawPartKind.Line
        ? this with { X = X + dx, Y = Y + dy, EndX = EndX + dx, EndY = EndY + dy }
        : this with { X = X + dx, Y = Y + dy };
}
