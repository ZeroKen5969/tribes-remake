using System;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.Ui;

/// <summary>Common string keys used by several screens.</summary>
public static class UiKeys
{
    /// <summary>"確定".</summary>
    public const string Ok = "common.ok";

    /// <summary>"取消".</summary>
    public const string Cancel = "common.cancel";

    /// <summary>"返回".</summary>
    public const string Back = "common.back";

    /// <summary>"是".</summary>
    public const string Yes = "common.yes";

    /// <summary>"否".</summary>
    public const string No = "common.no";
}

/// <summary>Builds the controls the screens share, already styled.</summary>
public static class UiFactory
{
    /// <summary>Width the back button takes (for centring titles).</summary>
    public const int BackButtonWidth = 64;

    private static Texture2D? backIcon;

    /// <summary>A label showing <paramref name="text"/> (a key or plain text) in theme variation <paramref name="variation"/>.</summary>
    public static Label Label(string text, string? variation = null, bool wrap = false)
    {
        var label = new Label { Text = text };
        if (variation is not null)
        {
            label.ThemeTypeVariation = variation;
        }
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
        return label;
    }

    /// <summary>A text label that never translates (names, numbers typed by players).</summary>
    public static Label Plain(string text, string? variation = null)
    {
        Label label = Label(text, variation);
        label.AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled;
        return label;
    }

    /// <summary>A plain label for list rows: text that does not fit ends with "…" instead of widening the screen.</summary>
    public static Label Cell(string text, string? variation = null)
    {
        Label label = Plain(text, variation);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    /// <summary>A small label on a badge (for example 「對局中」 on a room in a match).</summary>
    public static PanelContainer Badge(string textKey)
    {
        Label text = Label(textKey);
        text.VerticalAlignment = VerticalAlignment.Center;
        PanelContainer badge = Panel(PixelTheme.Variations.Badge, text);
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return badge;
    }

    /// <summary>A button with text <paramref name="textKey"/> calling <paramref name="pressed"/>.</summary>
    public static Button Button(string textKey, Action? pressed = null, string? variation = null)
    {
        var button = new Button
        {
            Text = textKey,
            CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
            FocusMode = Control.FocusModeEnum.All,
        };
        if (variation is not null)
        {
            button.ThemeTypeVariation = variation;
        }
        if (pressed is not null)
        {
            button.Pressed += pressed;
        }
        return button;
    }

    /// <summary>The back button with the original left arrow.</summary>
    public static Button BackButton()
    {
        backIcon ??= ArrowIcon(1);
        Button button = Button(UiKeys.Back);
        button.Icon = backIcon;
        button.CustomMinimumSize = new Vector2(BackButtonWidth, PixelTheme.RowHeight);
        return button;
    }

    /// <summary>Frame 0 of original effect <paramref name="effect"/> (1 left arrow, 2 right arrow) as an icon.</summary>
    public static Texture2D ArrowIcon(int effect)
    {
        OriginalArt art = OriginalArt.Shared;
        Image image = art.Rasterize(art.Composers.Sprites.ComposeEffect(effect, 0).Parts, out _);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>An empty control that takes the remaining space.</summary>
    public static Control Spacer(bool vertical = true) => new()
    {
        SizeFlagsVertical = vertical ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        SizeFlagsHorizontal = vertical ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>A labelled row: <paramref name="labelKey"/> on the left, <paramref name="field"/> filling the rest.</summary>
    public static HBoxContainer Row(string labelKey, Control field, int labelWidth = 96)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Label label = Label(labelKey);
        label.CustomMinimumSize = new Vector2(labelWidth, PixelTheme.RowHeight);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(field);
        return row;
    }

    /// <summary>An option button listing <paramref name="itemKeys"/> with <paramref name="selected"/> chosen.</summary>
    public static OptionButton Options(string[] itemKeys, int selected, Action<int> chosen)
    {
        var options = new OptionButton { CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight) };
        foreach (string key in itemKeys)
        {
            options.AddItem(key);
        }
        options.Selected = Math.Clamp(selected, 0, Math.Max(0, itemKeys.Length - 1));
        options.ItemSelected += index => chosen((int)index);
        return options;
    }

    /// <summary>A vertical scroll area holding <paramref name="content"/>, scrolling by touch drag as well.</summary>
    public static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    /// <summary>A panel of theme variation <paramref name="variation"/> around <paramref name="content"/>.</summary>
    public static PanelContainer Panel(string variation, Control content)
    {
        var panel = new PanelContainer { ThemeTypeVariation = variation };
        panel.AddChild(content);
        return panel;
    }

    /// <summary>A vertical box with <paramref name="separation"/> pixels between children.</summary>
    public static VBoxContainer Column(int separation = 6)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>A horizontal box with <paramref name="separation"/> pixels between children.</summary>
    public static HBoxContainer Line(int separation = 6)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }
}
