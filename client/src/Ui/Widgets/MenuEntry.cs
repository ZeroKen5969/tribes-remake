using System;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// A menu entry as the original draws it: large yellow outlined text and, on the entry being
/// chosen (pointed at or focused), the animated menu arrows on both sides (effects 2 and 1,
/// data/client/ui.json "menuArrows").
/// </summary>
public partial class MenuEntry : Button
{
    private const int RightArrowEffect = 2;
    private const int LeftArrowEffect = 1;
    private const double FrameSeconds = 0.12;
    private const int ArrowGap = 9;

    private double clock;
    private int frame;

    /// <summary>A menu entry showing translation key <paramref name="textKey"/>.</summary>
    public MenuEntry(string textKey)
    {
        Text = textKey;
        ThemeTypeVariation = PixelTheme.Variations.MenuItem;
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(0, NormalHeight);
        MouseEntered += () => { if (!Disabled) { GrabFocus(); } };
        FocusEntered += QueueRedraw;
        FocusExited += QueueRedraw;
        if (DisplayServer.IsTouchscreenAvailable())
        {
            // A touch leaves the entry "hovered"; on touch screens only the chosen (focused) entry is marked.
            AddThemeStyleboxOverride("hover", new StyleBoxEmpty { ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 1, ContentMarginBottom = 1 });
        }
    }

    /// <summary>For the Godot editor.</summary>
    public MenuEntry() : this("")
    {
    }

    /// <summary>The mouse moves the focus, so the focused entry is the chosen one for mouse, keys and touch alike.</summary>
    private bool Chosen => !Disabled && HasFocus();


    private const int NormalHeight = 28;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!Chosen)
        {
            return;
        }
        clock += delta;
        if (clock >= FrameSeconds)
        {
            clock -= FrameSeconds;
            frame++;
            QueueRedraw();
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (!Chosen)
        {
            return;
        }
        Font font = GetThemeFont("font");
        int size = GetThemeFontSize("font_size");
        float width = font.GetStringSize(Tr(Text), HorizontalAlignment.Left, -1, size).X;
        float middle = Size.Y / 2;
        float left = MathF.Floor((Size.X - width) / 2) - ArrowGap;
        float right = MathF.Ceiling((Size.X + width) / 2) + ArrowGap;
        OriginalArt.Shared.DrawEffect(this, RightArrowEffect, frame, new Vector2(left - 2, middle));
        OriginalArt.Shared.DrawEffect(this, LeftArrowEffect, frame, new Vector2(right + 2, middle));
    }
}
