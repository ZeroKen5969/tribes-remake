using System.Collections.Generic;

namespace Tribes.Graphics.Model;

/// <summary>What a panel part does (the original's menu and HUD layouts).</summary>
public enum PanelPartKind
{
    /// <summary>Draw piece <see cref="PanelPart.Id"/>.</summary>
    Piece = 0,

    /// <summary>Draw sprite <c>Id + value</c>, the value coming from the panel's label slots (icons that show a state).</summary>
    ValueSprite = 1,

    /// <summary>Play effect <see cref="PanelPart.Id"/>.</summary>
    Animation = 2,

    /// <summary>
    /// A field the program fills: <see cref="PanelPart.Id"/> 0..2 numbers, 3..4 a text box (the
    /// size of the piece before it, times 4), 5..6 a label, 10 a story illustration, 11 the minimap.
    /// </summary>
    Special = 3,

    /// <summary>Clip what follows to piece <see cref="PanelPart.Id"/>'s rectangle (piece 1: the whole screen).</summary>
    Clip = 4,
}

/// <summary>One part of a panel layout.</summary>
/// <param name="Kind">What the part does.</param>
/// <param name="Level">
/// Redraw level: parts of level <c>n</c> are drawn when the panel is redrawn at level <c>n</c> or
/// lower (0 on a full redraw); level 1 parts are bound to the next label slot's value.
/// </param>
/// <param name="Id">Piece, sprite or effect number, or the special field code.</param>
/// <param name="X">Horizontal offset from the panel origin.</param>
/// <param name="Y">Vertical offset from the panel origin.</param>
/// <param name="ShiftX">Extra horizontal offset (multiple of 128).</param>
/// <param name="ShiftY">Extra vertical offset (multiple of 128).</param>
/// <param name="TableIndex">Position in the original's part table; a few parts are recognised by it.</param>
public readonly record struct PanelPart(PanelPartKind Kind, int Level, int Id, int X, int Y, int ShiftX, int ShiftY, int TableIndex);

/// <summary>A rectangle in screen pixels (240 x 320 original layout).</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>Frame drawn with a panel.</summary>
public enum PanelFrame
{
    /// <summary>No frame.</summary>
    None = 0,

    /// <summary>Sprite 524 along the top of the screen.</summary>
    Top = 1,

    /// <summary>Sprite 528 along the bottom of the screen.</summary>
    Bottom = 2,
}

/// <summary>A menu or HUD panel.</summary>
/// <param name="Id">Panel number (0..19).</param>
/// <param name="Name">What the panel is.</param>
/// <param name="Area">Where the panel's buffer region shows on the screen (empty for panels drawn straight onto the screen).</param>
/// <param name="Frame">Frame drawn with it.</param>
/// <param name="Layout">Object graphic number of its layout.</param>
/// <param name="FirstLabel">First label slot it reads values from.</param>
/// <param name="RedrawEveryFrame">Redrawn every frame even when nothing changed.</param>
/// <param name="Parts">The layout's parts.</param>
public sealed record UiPanel(int Id, string Name, ScreenRect Area, PanelFrame Frame, int Layout, int FirstLabel,
    bool RedrawEveryFrame, IReadOnlyList<PanelPart> Parts);

/// <summary>What a UI element group numbers.</summary>
public enum UiElementKind
{
    /// <summary>Composite sprites.</summary>
    Sprite,

    /// <summary>Effect animations.</summary>
    Effect,

    /// <summary>Pieces.</summary>
    Piece,

    /// <summary>Object graphics.</summary>
    ObjectGraphic,
}

/// <summary>The extent of one element, relative to the point it is drawn at.</summary>
/// <param name="Id">Element number.</param>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width (0 when it draws nothing).</param>
/// <param name="Height">Height.</param>
public readonly record struct UiBounds(int Id, int X, int Y, int Width, int Height);

/// <summary>A numbered group of UI graphics with one purpose.</summary>
/// <param name="Name">Short name.</param>
/// <param name="Kind">What the numbers refer to.</param>
/// <param name="Ids">The element numbers, in the order the rule numbers them.</param>
/// <param name="Description">What it shows, where it is drawn and how it is chosen.</param>
/// <param name="Bounds">Extent of each element (the first frame for effects).</param>
public sealed record UiElementGroup(string Name, UiElementKind Kind, IReadOnlyList<int> Ids, string Description,
    IReadOnlyList<UiBounds> Bounds);

/// <summary>A colour the program draws with.</summary>
/// <param name="Name">Short name.</param>
/// <param name="Color">The colour.</param>
/// <param name="Description">Where it is used.</param>
public sealed record UiColor(string Name, RgbColor Color, string Description);

/// <summary>The UI pieces of the original (<c>data/client/ui.json</c>).</summary>
/// <param name="Format">Format name and version.</param>
/// <param name="ScreenWidth">Original screen width.</param>
/// <param name="ScreenHeight">Original screen height.</param>
/// <param name="MapView">
/// Where the map view is on the screen while playing: its origin (the top-left corner of the
/// view's first cell) and the map buffer's visible size. With the expanded top bar (panel 4) the
/// origin is 48 px down instead; the panels are drawn over the view.
/// </param>
/// <param name="Panels">The panels.</param>
/// <param name="Elements">Groups of UI graphics.</param>
/// <param name="Colors">Colours.</param>
/// <param name="Images">Full images: story illustrations, splash screens, icon.</param>
public sealed record UiCatalog(string Format, int ScreenWidth, int ScreenHeight, ScreenRect MapView, IReadOnlyList<UiPanel> Panels,
    IReadOnlyList<UiElementGroup> Elements, IReadOnlyList<UiColor> Colors, IReadOnlyList<SheetInfo> Images)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-ui/1";
}

/// <summary>A song (<c>data/client/music.json</c>).</summary>
/// <param name="Id">Song number (0..4).</param>
/// <param name="Ogg">Rendering, relative to <c>client/assets</c>.</param>
/// <param name="Midi">Original MIDI file, relative to <c>client/assets</c>.</param>
/// <param name="Seconds">Length of the rendering.</param>
public sealed record Song(int Id, string Ogg, string Midi, double Seconds);

/// <summary>A situation and the songs the original picks from for it.</summary>
/// <param name="Name">Short name.</param>
/// <param name="Songs">Songs it picks from at random (one is drawn whenever a song ends).</param>
/// <param name="Description">When it applies.</param>
public sealed record MusicMood(string Name, IReadOnlyList<int> Songs, string Description);

/// <summary>The music (<c>data/client/music.json</c>).</summary>
/// <param name="Format">Format name and version.</param>
/// <param name="Songs">The songs.</param>
/// <param name="Moods">When each is played.</param>
public sealed record MusicCatalog(string Format, IReadOnlyList<Song> Songs, IReadOnlyList<MusicMood> Moods)
{
    /// <summary>The format name written by this version.</summary>
    public const string CurrentFormat = "tribes-music/1";
}
