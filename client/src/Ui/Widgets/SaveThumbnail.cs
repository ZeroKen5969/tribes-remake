using System;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Logic;
using static Tribes.Client.Ui.Art.PixelPainter;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// A save's picture in a sunken frame: the PNG the match screen keeps next to the save, or a
/// faded stand-in landscape when there is none or it cannot be read (see <see cref="SaveThumbnails"/>).
/// </summary>
public partial class SaveThumbnail : PanelContainer
{
    /// <summary>How far the stand-in's colours are moved towards the parchment (it reads as "no picture", not as a real one).</summary>
    private const float StandInMuting = 0.45f;

    private static Texture2D? standIn;
    private readonly TextureRect picture;

    /// <summary>A frame showing the picture at <paramref name="path"/> (a res://, user:// or absolute file).</summary>
    public SaveThumbnail(string path)
    {
        Name = "Thumbnail";
        ThemeTypeVariation = PixelTheme.Variations.Inset;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        Texture2D? texture = Load(path);
        IsStandIn = texture is null;
        picture = new TextureRect
        {
            Texture = texture ?? StandIn,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(picture);
        ShowAt(SaveThumbnails.DisplaySize(SaveThumbnails.FullSizeRowWidth));
    }

    /// <summary>Whether the stand-in is shown (no usable picture).</summary>
    public bool IsStandIn { get; }

    /// <summary>The stand-in picture: a faded landscape, painted at half size and shown doubled.</summary>
    public static Texture2D StandIn => standIn ??= ImageTexture.CreateFromImage(PaintStandIn());

    /// <summary>
    /// Shows the picture at <paramref name="size"/> (logical pixels, inside the frame). A real
    /// picture drawn smaller than it is on screen is smoothed; drawn at its size or larger it stays sharp.
    /// </summary>
    public void ShowAt((int Width, int Height) size)
    {
        picture.CustomMinimumSize = new Vector2(size.Width, size.Height);
        float scale = IsInsideTree() ? GetTree().Root.ContentScaleFactor : 1;
        bool shrunk = !IsStandIn && picture.Texture is { } texture && size.Width * scale < texture.GetWidth();
        picture.TextureFilter = shrunk ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest;
    }

    /// <summary>The picture at <paramref name="path"/>, or null when it is missing, too long, not a PNG or broken.</summary>
    public static Texture2D? Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !FileAccess.FileExists(path))
        {
            return null;
        }
        byte[] bytes;
        using (FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            if (file is null || !SaveThumbnails.IsSmallEnough((long)file.GetLength()))
            {
                return null;
            }
            bytes = file.GetBuffer((long)file.GetLength());
        }
        if (!SaveThumbnails.IsUsable(bytes))
        {
            return null;
        }
        using var image = new Image();
        return image.LoadPngFromBuffer(bytes) == Error.Ok && !image.IsEmpty() ? ImageTexture.CreateFromImage(image) : null;
    }

    private static Image PaintStandIn()
    {
        const int width = SaveThumbnails.Width / 2, height = SaveThumbnails.Height / 2;
        Image image = Blank(width, height);
        // Sky in bands, lighter towards the horizon, with a sun.
        Color[] sky = { new("#6fa8e8"), new("#7fb8f4"), UiPalette.Sky, new("#a8d4ff") };
        for (int band = 0; band < sky.Length; band++)
        {
            Fill(image, new Rect2I(0, band * 7, width, 7), sky[band]);
        }
        Fill(image, new Rect2I(width - 20, 6, 6, 6), UiPalette.YellowLight);
        Fill(image, new Rect2I(width - 21, 7, 8, 4), UiPalette.YellowLight);
        // Two rows of hills in steps, the far ones lighter, and the grass in front.
        for (int x = 0; x < width; x++)
        {
            int far = 22 + (int)Math.Round(3 * Math.Sin(x / 7.0) + 2 * Math.Sin(x / 3.1));
            int near = 30 + (int)Math.Round(3 * Math.Sin((x + 11) / 9.0));
            Fill(image, new Rect2I(x, far, 1, height - far), UiPalette.Green);
            Fill(image, new Rect2I(x, near, 1, height - near), new Color("#2f8a2c"));
        }
        Fill(image, new Rect2I(0, height - 6, width, 6), UiPalette.Ground);
        // A hut on the near hill: the save's tribe without its picture.
        Fill(image, new Rect2I(16, 30, 9, 5), UiPalette.BrownLight);
        Line(image, 14, 30, 20, 25, UiPalette.Brown);
        Line(image, 20, 25, 26, 30, UiPalette.Brown);
        Fill(image, new Rect2I(15, 29, 11, 1), UiPalette.Brown);
        Fill(image, new Rect2I(19, 32, 3, 3), UiPalette.Ink);
        return Muted(image, UiPalette.ParchmentDark, StandInMuting);
    }

    /// <summary>A copy of <paramref name="image"/> with each pixel moved <paramref name="amount"/> of the way to <paramref name="tone"/>.</summary>
    private static Image Muted(Image image, Color tone, float amount)
    {
        Image copy = (Image)image.Duplicate();
        for (int y = 0; y < copy.GetHeight(); y++)
        {
            for (int x = 0; x < copy.GetWidth(); x++)
            {
                copy.SetPixel(x, y, copy.GetPixel(x, y).Lerp(tone, amount));
            }
        }
        return copy;
    }
}
