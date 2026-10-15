using Godot;
using Tribes.Client.Ui.Art;
using static Tribes.Client.Ui.Art.PixelPainter;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>Icons of the match interface the original has no picture for.</summary>
public enum PaintedIcon
{
    /// <summary>Two bars: pause.</summary>
    Pause,

    /// <summary>A triangle: play at normal speed.</summary>
    Play,

    /// <summary>Two triangles: faster.</summary>
    Fast,

    /// <summary>Three triangles: fastest.</summary>
    Fastest,

    /// <summary>Three lines: the game menu.</summary>
    Menu,

    /// <summary>A small map: the minimap.</summary>
    Minimap,

    /// <summary>A dotted box: select several units.</summary>
    MultiSelect,

    /// <summary>Cross hairs: centre the view.</summary>
    Centre,

    /// <summary>A cross: close or cancel.</summary>
    Close,

    /// <summary>A question mark: help.</summary>
    Help,

    /// <summary>A flag: the mission's objective.</summary>
    Objective,

    /// <summary>A plus sign.</summary>
    Plus,

    /// <summary>A minus sign.</summary>
    Minus,

    /// <summary>A bowl: send to eat.</summary>
    Eat,

    /// <summary>A crescent: send to rest.</summary>
    Rest,

    /// <summary>A door with an arrow: come out.</summary>
    ComeOut,

    /// <summary>A crown: the chief.</summary>
    Chief,

    /// <summary>A disk: save.</summary>
    Save,

    /// <summary>Two linked rings: alliances (the mark on the alliance hall's banner).</summary>
    Alliance,

    /// <summary>Three coloured marks with lines: the list of players.</summary>
    Players,
}

/// <summary>Paints <see cref="PaintedIcon"/>s in the original's ink and parchment colours (12x12, transparent).</summary>
public static class PaintedIcons
{
    private const int Size = 12;

    /// <summary>The picture of <paramref name="icon"/>.</summary>
    public static Image Paint(PaintedIcon icon)
    {
        Image image = Blank(Size, Size);
        Color ink = UiPalette.Ink;
        switch (icon)
        {
            case PaintedIcon.Pause:
                Fill(image, new Rect2I(3, 2, 2, 8), ink);
                Fill(image, new Rect2I(7, 2, 2, 8), ink);
                break;
            case PaintedIcon.Play:
                Triangle(image, 3, ink);
                break;
            case PaintedIcon.Fast:
                SmallTriangle(image, 1, ink);
                SmallTriangle(image, 6, ink);
                break;
            case PaintedIcon.Fastest:
                SmallTriangle(image, 0, ink);
                SmallTriangle(image, 4, ink);
                SmallTriangle(image, 8, ink);
                break;
            case PaintedIcon.Menu:
                Fill(image, new Rect2I(1, 2, 10, 2), ink);
                Fill(image, new Rect2I(1, 5, 10, 2), ink);
                Fill(image, new Rect2I(1, 8, 10, 2), ink);
                break;
            case PaintedIcon.Minimap:
                Box(image, new Rect2I(0, 1, 12, 10), new BoxStyle(ink, UiPalette.Ground, new Color("#2e8b46"), UiPalette.Ground, UiPalette.Clear));
                Fill(image, new Rect2I(3, 4, 4, 3), UiPalette.Yellow);
                Outline(image, new Rect2I(2, 3, 6, 5), UiPalette.Paper);
                break;
            case PaintedIcon.MultiSelect:
                for (int i = 0; i < Size; i += 2)
                {
                    Pixel(image, i, 0, ink);
                    Pixel(image, i, Size - 1, ink);
                    Pixel(image, 0, i, ink);
                    Pixel(image, Size - 1, i, ink);
                }
                Fill(image, new Rect2I(3, 3, 2, 2), ink);
                Fill(image, new Rect2I(7, 4, 2, 2), ink);
                Fill(image, new Rect2I(4, 7, 2, 2), ink);
                break;
            case PaintedIcon.Centre:
                Outline(image, new Rect2I(2, 2, 8, 8), ink);
                Fill(image, new Rect2I(5, 0, 2, 4), ink);
                Fill(image, new Rect2I(5, 8, 2, 4), ink);
                Fill(image, new Rect2I(0, 5, 4, 2), ink);
                Fill(image, new Rect2I(8, 5, 4, 2), ink);
                break;
            case PaintedIcon.Close:
                for (int i = 1; i < Size - 1; i++)
                {
                    Fill(image, new Rect2I(i, i, 2, 1), UiPalette.Red);
                    Fill(image, new Rect2I(Size - 2 - i, i, 2, 1), UiPalette.Red);
                }
                break;
            case PaintedIcon.Help:
                Fill(image, new Rect2I(3, 1, 6, 2), ink);
                Fill(image, new Rect2I(8, 2, 2, 3), ink);
                Fill(image, new Rect2I(5, 5, 4, 2), ink);
                Fill(image, new Rect2I(5, 7, 2, 1), ink);
                Fill(image, new Rect2I(5, 9, 2, 2), ink);
                Fill(image, new Rect2I(2, 2, 2, 2), ink);
                break;
            case PaintedIcon.Objective:
                Fill(image, new Rect2I(2, 0, 1, 12), ink);
                Fill(image, new Rect2I(3, 1, 7, 5), UiPalette.Red);
                Outline(image, new Rect2I(3, 1, 7, 5), ink);
                break;
            case PaintedIcon.Plus:
                Fill(image, new Rect2I(5, 1, 2, 10), ink);
                Fill(image, new Rect2I(1, 5, 10, 2), ink);
                break;
            case PaintedIcon.Minus:
                Fill(image, new Rect2I(1, 5, 10, 2), ink);
                break;
            case PaintedIcon.Eat:
                Fill(image, new Rect2I(1, 5, 10, 1), ink);
                Fill(image, new Rect2I(2, 6, 8, 3), UiPalette.BrownLight);
                Outline(image, new Rect2I(2, 6, 8, 4), ink);
                Fill(image, new Rect2I(4, 2, 1, 3), UiPalette.Orange);
                Fill(image, new Rect2I(7, 1, 1, 4), UiPalette.Orange);
                break;
            case PaintedIcon.Rest:
                Fill(image, new Rect2I(3, 1, 4, 10), UiPalette.Yellow);
                Fill(image, new Rect2I(5, 2, 4, 8), UiPalette.Clear);
                Fill(image, new Rect2I(2, 3, 1, 6), UiPalette.Yellow);
                Pixel(image, 9, 1, UiPalette.Yellow);
                Pixel(image, 10, 4, UiPalette.Yellow);
                break;
            case PaintedIcon.ComeOut:
                Box(image, new Rect2I(0, 0, 7, 12), new BoxStyle(ink, UiPalette.BrownLight, UiPalette.Gold, UiPalette.Brown, UiPalette.Clear));
                Fill(image, new Rect2I(5, 5, 6, 2), UiPalette.GreenBright);
                Fill(image, new Rect2I(9, 3, 1, 6), UiPalette.GreenBright);
                Pixel(image, 10, 4, UiPalette.GreenBright);
                Pixel(image, 10, 7, UiPalette.GreenBright);
                Pixel(image, 11, 5, UiPalette.GreenBright);
                Pixel(image, 11, 6, UiPalette.GreenBright);
                break;
            case PaintedIcon.Chief:
                Fill(image, new Rect2I(1, 5, 10, 5), UiPalette.Yellow);
                Outline(image, new Rect2I(1, 5, 10, 5), ink);
                Fill(image, new Rect2I(1, 2, 2, 3), UiPalette.Yellow);
                Fill(image, new Rect2I(5, 1, 2, 4), UiPalette.Yellow);
                Fill(image, new Rect2I(9, 2, 2, 3), UiPalette.Yellow);
                Fill(image, new Rect2I(5, 7, 2, 1), UiPalette.Red);
                break;
            case PaintedIcon.Alliance:
                Ring(image, 0, 2, UiPalette.Red, ink);
                Ring(image, 5, 4, UiPalette.Orange, ink);
                // Where the second ring passes over the first.
                Pixel(image, 6, 5, UiPalette.Orange);
                break;
            case PaintedIcon.Players:
                PlayerLine(image, 1, UiPalette.Orange, ink);
                PlayerLine(image, 5, UiPalette.Red, ink);
                PlayerLine(image, 9, UiPalette.GreenBright, ink);
                break;
            case PaintedIcon.Save:
                Box(image, new Rect2I(0, 0, 12, 12), new BoxStyle(ink, UiPalette.BrownLight, UiPalette.Gold, UiPalette.Brown, UiPalette.Clear));
                Fill(image, new Rect2I(3, 1, 6, 4), UiPalette.Paper);
                Fill(image, new Rect2I(3, 7, 6, 4), UiPalette.Ink);
                break;
        }
        return image;
    }

    /// <summary>A row of the players icon: a 3 x 3 mark of <paramref name="colour"/> outlined in ink, then a line.</summary>
    private static void PlayerLine(Image image, int y, Color colour, Color ink)
    {
        Fill(image, new Rect2I(0, y - 1, 4, 4), ink);
        Fill(image, new Rect2I(1, y, 2, 2), colour);
        Fill(image, new Rect2I(5, y, 7, 2), ink);
    }

    /// <summary>A ring 7 x 7 with an outline, its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static void Ring(Image image, int x, int y, Color colour, Color outline)
    {
        int[] rows = { 2, 1, 0, 0, 0, 1, 2 };
        for (int j = 0; j < 7; j++)
        {
            int inset = rows[j];
            for (int i = inset; i < 7 - inset; i++)
            {
                bool inner = j >= 2 && j <= 4 && i >= 2 && i <= 4;
                if (!inner)
                {
                    Pixel(image, x + i, y + j, colour);
                }
            }
        }
        Pixel(image, x + 3, y + 2, outline);
        Pixel(image, x + 2, y + 3, outline);
        Pixel(image, x + 4, y + 3, outline);
        Pixel(image, x + 3, y + 4, outline);
    }

    private static void Triangle(Image image, int x, Color colour)
    {
        for (int i = 0; i < 5; i++)
        {
            Fill(image, new Rect2I(x + i, 1 + i, 1, 10 - (2 * i)), colour);
        }
    }

    private static void SmallTriangle(Image image, int x, Color colour)
    {
        for (int i = 0; i < 4; i++)
        {
            Fill(image, new Rect2I(x + i, 2 + i, 1, 8 - (2 * i)), colour);
        }
    }
}
