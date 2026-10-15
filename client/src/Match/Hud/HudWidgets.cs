using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>Builds the controls the match interface shares, sized by the <see cref="HudMetrics"/>.</summary>
public static class HudWidgets
{
    private static readonly Dictionary<(ulong, int), Texture2D> Scaled = new();

    /// <summary><paramref name="texture"/> enlarged <paramref name="scale"/> times without smoothing.</summary>
    public static Texture2D Enlarge(Texture2D texture, int scale)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (scale <= 1)
        {
            return texture;
        }
        var key = (texture.GetInstanceId(), scale);
        if (!Scaled.TryGetValue(key, out Texture2D? result))
        {
            Image image = texture.GetImage();
            image.Resize(image.GetWidth() * scale, image.GetHeight() * scale, Image.Interpolation.Nearest);
            result = ImageTexture.CreateFromImage(image);
            Scaled[key] = result;
        }
        return result;
    }

    /// <summary>
    /// A square tool button showing <paramref name="icon"/>; <paramref name="captionKey"/> is its
    /// tooltip, and on touch screens with room also a caption under the icon.
    /// </summary>
    public static Button IconButton(HudMetrics metrics, Texture2D icon, string captionKey, Action pressed, bool caption = false, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(pressed);
        var button = new Button
        {
            Icon = Enlarge(icon, metrics.IconScale),
            TooltipText = captionKey,
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            FocusMode = Control.FocusModeEnum.None,
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Top,
            CustomMinimumSize = new Vector2(metrics.Button, metrics.Button),
            ClipText = true,
        };
        if (caption)
        {
            button.Text = captionKey;
            button.AddThemeFontSizeOverride("font_size", PixelTheme.BodySize);
        }
        else
        {
            button.VerticalIconAlignment = VerticalAlignment.Center;
        }
        if (name is not null)
        {
            button.Name = name;
        }
        button.Pressed += pressed;
        return button;
    }

    /// <summary>A wide button with an icon and a text, for menus and panels.</summary>
    public static Button TextButton(HudMetrics metrics, Texture2D? icon, string textKey, Action pressed, string? name = null, string? variation = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(pressed);
        var button = new Button
        {
            Text = textKey,
            Icon = icon is null ? null : Enlarge(icon, metrics.Touch ? Math.Max(1, metrics.IconScale) : 1),
            ThemeTypeVariation = variation ?? PixelTheme.Variations.Tool,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, Math.Max(PixelTheme.RowHeight, metrics.Button)),
            ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (name is not null)
        {
            button.Name = name;
        }
        button.Pressed += pressed;
        return button;
    }

    /// <summary>A label of <paramref name="text"/> that is not translated (numbers and names).</summary>
    public static Label Value(string text, string? variation = null)
    {
        Label label = UiFactory.Plain(text, variation);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>A label with a key or text that wraps.</summary>
    public static Label Text(string text, string? variation = null, bool translate = true)
    {
        Label label = translate ? UiFactory.Label(text, variation, wrap: true) : UiFactory.Plain(text, variation);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>Removes and frees every child of <paramref name="node"/>.</summary>
    public static void Clear(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}

/// <summary>A meter in the original's colours: a dark frame and a bar from red over yellow to green.</summary>
public partial class MeterBar : Control
{
    private float fraction;
    private Color? colour;

    /// <summary>How full, 0..1.</summary>
    public float Fraction
    {
        get => fraction;
        set
        {
            float clamped = Mathf.Clamp(value, 0, 1);
            if (!Mathf.IsEqualApprox(clamped, fraction))
            {
                fraction = clamped;
                QueueRedraw();
            }
        }
    }

    /// <summary>A fixed bar colour instead of the health colours.</summary>
    public Color? BarColour
    {
        get => colour;
        set
        {
            colour = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        if (CustomMinimumSize == Vector2.Zero)
        {
            CustomMinimumSize = new Vector2(40, 5);
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        var frame = new Rect2(Vector2.Zero, Size);
        DrawRect(frame, UiPalette.Dark);
        DrawRect(frame, UiPalette.Ink, filled: false, width: 1);
        Color fill = colour ?? (fraction > 0.66f ? UiPalette.GreenBright : fraction > 0.33f ? UiPalette.Yellow : UiPalette.Red);
        float width = Mathf.Floor((Size.X - 2) * fraction);
        if (width > 0)
        {
            DrawRect(new Rect2(1, 1, width, Size.Y - 2), fill);
        }
    }
}
