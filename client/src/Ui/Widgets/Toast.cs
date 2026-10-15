using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.Ui.Widgets;

/// <summary>A short note in a speech bubble at the top of the screen that fades away by itself.</summary>
public partial class Toast : PanelContainer
{
    private const double ShowSeconds = 3.5;

    /// <summary>Shows <paramref name="text"/> (plain text or a key) on <paramref name="layer"/>.</summary>
    public static Toast Show(Control layer, string text)
    {
        foreach (Node child in layer.GetChildren())
        {
            if (child is Toast old)
            {
                old.QueueFree();
            }
        }
        var toast = new Toast();
        layer.AddChild(toast);
        toast.label.Text = text;
        return toast;
    }

    private readonly Label label = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <inheritdoc/>
    public override void _Ready()
    {
        ThemeTypeVariation = PixelTheme.Variations.Bubble;
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(label);
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        GrowHorizontal = GrowDirection.Both;
        label.CustomMinimumSize = new Vector2(220, 0);
        OffsetTop = 6;
        Tween tween = CreateTween();
        tween.TweenInterval(ShowSeconds);
        tween.TweenProperty(this, "modulate:a", 0f, 0.5);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
