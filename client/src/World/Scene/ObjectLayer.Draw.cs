using System.Collections.Generic;
using Tribes.Graphics.Composition;

namespace Tribes.Client.World.Scene;

/// <summary>Adding compositions to the layer with the palette variant carried from one picture to the next.</summary>
public sealed partial class ObjectLayer
{
    /// <summary>Composite sprite <paramref name="sprite"/> at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private void AddSprite(int sprite, int x, int y, int skin = -1, MoverKind mover = MoverKind.None, int moverId = 0) =>
        Emit(composers.Sprites.ComposeSprite(sprite, variant), x, y, skin, mover, moverId);

    /// <summary>Piece <paramref name="piece"/> at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private void AddPiece(int piece, int x, int y) => Emit(composers.Sprites.ComposePiece(piece, variant), x, y, -1, MoverKind.None, 0);

    /// <summary>Effect <paramref name="effect"/> at <paramref name="tick"/>, at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private void AddEffect(int effect, int x, int y, int tick, MoverKind mover = MoverKind.None, int moverId = 0) =>
        Emit(composers.Sprites.ComposeEffect(effect, tick, variant), x, y, -1, mover, moverId);

    /// <summary>
    /// Adds a composition and keeps its palette variant for what follows; a composition that
    /// stopped at a missing picture stops the caller too, as the original's exception does.
    /// </summary>
    private void Emit(DrawList list, int x, int y, int skin, MoverKind mover, int moverId)
    {
        output.Add(list.Parts, x, y, skin, mover, moverId);
        variant = list.PaletteVariantAfter;
        if (list.Truncated)
        {
            throw new DrawFailedException("a picture of the object layer is missing");
        }
    }

    /// <summary>Adds <paramref name="count"/> parts of <paramref name="parts"/> from <paramref name="start"/>.</summary>
    private void EmitRange(IReadOnlyList<DrawPart> parts, int start, int count, int x, int y, int skin, MoverKind mover, int moverId)
    {
        for (int i = start; i < start + count; i++)
        {
            output.Add(parts[i].Offset(x, y), skin, mover, moverId);
        }
    }
}
