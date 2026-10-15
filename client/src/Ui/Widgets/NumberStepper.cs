using System;
using System.Globalization;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// A whole number with "−" and "+" buttons beside a field the player can also type into. It has
/// a lowest value; a highest one only when <see cref="Highest"/> is set.
/// </summary>
public partial class NumberStepper : HBoxContainer
{
    private readonly LineEdit field;
    private readonly int minimum;
    private int value;
    private int? highest;

    /// <summary>A stepper starting at <paramref name="initial"/>, never below <paramref name="lowest"/>.</summary>
    public NumberStepper(int initial, int lowest)
    {
        minimum = lowest;
        value = Math.Max(lowest, initial);
        AddThemeConstantOverride("separation", 4);
        Button less = Step("−", -1);
        field = new LineEdit
        {
            Text = Format(value),
            Alignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(48, PixelTheme.RowHeight),
            VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            SelectAllOnFocus = true,
        };
        field.TextSubmitted += _ => Read();
        field.FocusExited += Read;
        Button more = Step("+", 1);
        AddChild(less);
        AddChild(field);
        AddChild(more);
    }

    /// <summary>For the Godot editor.</summary>
    public NumberStepper() : this(0, 0)
    {
    }

    /// <summary>The number shown (what the player typed is read first).</summary>
    public int Value
    {
        get
        {
            Read();
            return value;
        }
        set
        {
            this.value = Keep(value);
            field.Text = Format(this.value);
        }
    }

    /// <summary>The highest value, or null for none.</summary>
    public int? Highest
    {
        get => highest;
        set
        {
            highest = value;
            Value = this.value;
        }
    }

    private int Keep(long number) => (int)Math.Clamp(number, minimum, Math.Max(minimum, highest ?? int.MaxValue));

    private Button Step(string text, int change)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            CustomMinimumSize = new Vector2(PixelTheme.RowHeight, PixelTheme.RowHeight),
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            FocusMode = FocusModeEnum.All,
        };
        button.Pressed += () =>
        {
            Read();
            value = Keep((long)value + change);
            field.Text = Format(value);
        };
        return button;
    }

    private void Read()
    {
        if (int.TryParse(field.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed))
        {
            value = Keep(typed);
        }
        field.Text = Format(value);
    }

    private static string Format(int number) => number.ToString(CultureInfo.InvariantCulture);
}
