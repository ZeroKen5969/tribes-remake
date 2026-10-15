using Godot;

namespace Tribes.Client.Ui.Art;

/// <summary>
/// The interface colours, taken from the original pictures: the parchment shades and brown edges
/// of the panels (sheet pi0_12 and the primitive colours of sprites.json), the yellow, outline and
/// shadow of the original large font, and the title screen's sky and ground.
/// </summary>
public static class UiPalette
{
    /// <summary>Outline of the original font; the darkest brown.</summary>
    public static readonly Color Ink = new("#311e17");

    /// <summary>Near-black brown of the panel shadows.</summary>
    public static readonly Color Dark = new("#221e09");

    /// <summary>Brown of panel edges and box outlines.</summary>
    public static readonly Color Brown = new("#66511f");

    /// <summary>Lighter brown of the font's bevel.</summary>
    public static readonly Color BrownLight = new("#7f5727");

    /// <summary>The parchment shadow under the original font.</summary>
    public static readonly Color ParchmentShadow = new("#92893d");

    /// <summary>Dark parchment.</summary>
    public static readonly Color ParchmentDark = new("#a7963f");

    /// <summary>Middle parchment.</summary>
    public static readonly Color ParchmentMid = new("#b4a647");

    /// <summary>The parchment of every original panel.</summary>
    public static readonly Color Parchment = new("#c2b651");

    /// <summary>Light parchment of speech bubbles.</summary>
    public static readonly Color ParchmentLight = new("#d0c460");

    /// <summary>Highlight drawn on lit edges (lighter than any original parchment, same hue).</summary>
    public static readonly Color ParchmentPale = new("#ded488");

    /// <summary>Gold of the original icons.</summary>
    public static readonly Color Gold = new("#af8609");

    /// <summary>Main yellow of the original font.</summary>
    public static readonly Color Yellow = new("#f5de00");

    /// <summary>The lit top of the original font's yellow.</summary>
    public static readonly Color YellowLight = new("#f5f400");

    /// <summary>The orange bottom of the original font's yellow.</summary>
    public static readonly Color Orange = new("#f5a800");

    /// <summary>Green of the original confirm tick.</summary>
    public static readonly Color Green = new("#51ad1b");

    /// <summary>Bright green of full health bars.</summary>
    public static readonly Color GreenBright = new("#00da00");

    /// <summary>Red of low health bars and the cancel cross.</summary>
    public static readonly Color Red = new("#e00000");

    /// <summary>Title screen sky.</summary>
    public static readonly Color Sky = new("#8fc7ff");

    /// <summary>Title screen ground.</summary>
    public static readonly Color Ground = new("#0e5b27");

    /// <summary>Text on dark backgrounds.</summary>
    public static readonly Color Paper = new("#f6f0c8");

    /// <summary>Transparent.</summary>
    public static readonly Color Clear = new(0, 0, 0, 0);
}
