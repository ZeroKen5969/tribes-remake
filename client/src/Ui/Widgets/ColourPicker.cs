using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Net;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// The 8 tribe colours as icon bar slots: the chosen colour sits in the highlighted slot with the
/// green tick; colours another tribe has are greyed with the red cross and cannot be picked.
/// </summary>
public partial class ColourPicker : GridContainer
{
    private const int SwatchSize = 26;

    private readonly List<Swatch> swatches = new();
    private Action<int>? chosen;

    /// <summary>A picker calling <paramref name="onChosen"/> with the colour id the player taps.</summary>
    public ColourPicker(Action<int> onChosen)
    {
        chosen = onChosen;
        Columns = 4;
        AddThemeConstantOverride("h_separation", 4);
        AddThemeConstantOverride("v_separation", 4);
        foreach (TribeColour colour in TribeColours.All())
        {
            var swatch = new Swatch(colour) { CustomMinimumSize = new Vector2(SwatchSize, SwatchSize) };
            swatch.Pressed += () =>
            {
                if (!swatch.Taken)
                {
                    chosen?.Invoke(swatch.Colour.Id);
                }
            };
            AddChild(swatch);
            swatches.Add(swatch);
        }
    }

    /// <summary>For the Godot editor.</summary>
    public ColourPicker() : this(_ => { })
    {
    }

    /// <summary>Shows <paramref name="selected"/> as chosen and <paramref name="taken"/> as unavailable.</summary>
    public void SetState(int selected, ICollection<int> taken, bool enabled = true)
    {
        foreach (Swatch swatch in swatches)
        {
            swatch.Selected = swatch.Colour.Id == selected;
            swatch.Taken = !swatch.Selected && taken.Contains(swatch.Colour.Id);
            swatch.Disabled = !enabled || swatch.Taken;
            swatch.QueueRedraw();
        }
    }

    private sealed partial class Swatch : Button
    {
        public Swatch(TribeColour colour)
        {
            Colour = colour;
            TooltipText = colour.Name;
            AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            ThemeTypeVariation = PixelTheme.Variations.Tool;
            FocusMode = FocusModeEnum.All;
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }

        public Swatch() : this(new TribeColour(0, Colors.Gray, ""))
        {
        }

        public TribeColour Colour { get; }

        public bool Selected { get; set; }

        public bool Taken { get; set; }

        public override void _Draw()
        {
            Rect2 inner = new(new Vector2(5, 5), Size - new Vector2(11, 12));
            Color fill = Taken ? Colour.Swatch.Darkened(0.55f) : Colour.Swatch;
            DrawRect(inner.Grow(1), UiPalette.Ink);
            DrawRect(inner, fill);
            DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X, 1)), fill.Lightened(0.35f));
            if (TribeColours.ArePlaceholders)
            {
                // Placeholder colours: the number instead of a real tribe colour name.
                Font font = GetThemeFont("font");
                string number = (Colour.Id + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                DrawString(font, inner.Position + new Vector2(inner.Size.X / 2 - 3, inner.Size.Y / 2 + 5), number, HorizontalAlignment.Left, -1, 12, UiPalette.Ink);
            }
            if (Taken)
            {
                DrawTexture(PixelTheme.CrossIcon, new Vector2(MathF.Floor((Size.X - PixelTheme.CrossIcon.GetWidth()) / 2), MathF.Floor((Size.Y - PixelTheme.CrossIcon.GetHeight()) / 2) - 1));
            }
            if (Selected)
            {
                Rect2 frame = new(new Vector2(3, 3), Size - new Vector2(7, 8));
                DrawRect(new Rect2(frame.Position, new Vector2(frame.Size.X, 1)), UiPalette.Yellow);
                DrawRect(new Rect2(frame.Position + new Vector2(0, frame.Size.Y - 1), new Vector2(frame.Size.X, 1)), UiPalette.Yellow);
                DrawRect(new Rect2(frame.Position, new Vector2(1, frame.Size.Y)), UiPalette.Yellow);
                DrawRect(new Rect2(frame.Position + new Vector2(frame.Size.X - 1, 0), new Vector2(1, frame.Size.Y)), UiPalette.Yellow);
                DrawTexture(PixelTheme.TickIcon, new Vector2(Size.X - PixelTheme.TickIcon.GetWidth() + 3, -2));
            }
        }
    }
}
