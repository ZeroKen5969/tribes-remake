using Godot;
using static Tribes.Client.Ui.Art.PixelPainter;

namespace Tribes.Client.Ui.Art;

/// <summary>Small icons the original lacks, painted in its palette.</summary>
public static class Icons
{
    private static Texture2D? lockIcon;
    private static Texture2D? computerIcon;
    private static Texture2D? personIcon;

    /// <summary>A padlock: locked missions.</summary>
    public static Texture2D Lock => lockIcon ??= ImageTexture.CreateFromImage(PaintLock());

    /// <summary>A stone tablet with eyes: a computer player.</summary>
    public static Texture2D Computer => computerIcon ??= ImageTexture.CreateFromImage(PaintComputer());

    /// <summary>A head: a person.</summary>
    public static Texture2D Person => personIcon ??= ImageTexture.CreateFromImage(PaintPerson());

    private static Image PaintLock()
    {
        Image image = Blank(11, 13);
        // Shackle.
        Fill(image, new Rect2I(3, 0, 5, 1), UiPalette.Ink);
        Fill(image, new Rect2I(2, 1, 1, 5), UiPalette.Ink);
        Fill(image, new Rect2I(8, 1, 1, 5), UiPalette.Ink);
        Fill(image, new Rect2I(3, 1, 5, 1), UiPalette.ParchmentPale);
        Fill(image, new Rect2I(3, 2, 1, 4), UiPalette.ParchmentPale);
        Fill(image, new Rect2I(7, 2, 1, 4), UiPalette.ParchmentDark);
        // Body.
        Box(image, new Rect2I(0, 5, 11, 8), new BoxStyle(UiPalette.Ink, UiPalette.Gold, UiPalette.Yellow, UiPalette.BrownLight, UiPalette.Clear));
        Fill(image, new Rect2I(5, 7, 1, 3), UiPalette.Ink);
        Pixel(image, 4, 7, UiPalette.Ink);
        Pixel(image, 6, 7, UiPalette.Ink);
        return image;
    }

    private static Image PaintComputer()
    {
        Image image = Blank(13, 13);
        Box(image, new Rect2I(0, 0, 13, 12), new BoxStyle(UiPalette.Ink, new Color("#8a8a80"), new Color("#b8b8ae"), new Color("#5c5c54"), new Color(UiPalette.Ink, 0.5f)));
        Fill(image, new Rect2I(3, 4, 2, 2), UiPalette.Red);
        Fill(image, new Rect2I(8, 4, 2, 2), UiPalette.Red);
        Fill(image, new Rect2I(4, 8, 5, 1), UiPalette.Ink);
        return image;
    }

    private static Image PaintPerson()
    {
        Image image = Blank(11, 13);
        Color skin = new("#d08850");
        Color hair = new("#3a2010");
        Fill(image, new Rect2I(3, 1, 5, 6), skin);
        Fill(image, new Rect2I(2, 0, 7, 2), hair);
        Fill(image, new Rect2I(2, 2, 1, 3), hair);
        Fill(image, new Rect2I(8, 2, 1, 3), hair);
        Pixel(image, 4, 3, UiPalette.Ink);
        Pixel(image, 6, 3, UiPalette.Ink);
        Fill(image, new Rect2I(1, 8, 9, 5), UiPalette.Yellow);
        Fill(image, new Rect2I(1, 8, 9, 1), UiPalette.Orange);
        Outline(image, new Rect2I(0, 7, 11, 6), UiPalette.Ink);
        return image;
    }
}
