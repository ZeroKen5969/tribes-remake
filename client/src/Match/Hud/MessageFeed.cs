using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The message list: the rules' notices (born, died, building finished, enemy seen, under
/// attack...) and the players' comings and goings, newest at the bottom, each with the
/// original's face icon. A message about a place moves the view there when tapped. Messages fade
/// after a while; at most <see cref="MaxShown"/> are shown.
/// </summary>
public partial class MessageFeed : VBoxContainer
{
    /// <summary>Most messages on screen.</summary>
    public const int MaxShown = 4;

    private const double ShownSeconds = 9, FadeSeconds = 1.5;
    private const int SadFace = 675, WorriedFace = 676, HappyFace = 679;

    private readonly List<(Control Row, double Age)> rows = new();

    /// <summary>A message about a place was tapped.</summary>
    public event Action<int, int>? PlaceTapped;

    /// <summary>The texts shown now, oldest first (for checks).</summary>
    public IEnumerable<string> Texts
    {
        get
        {
            foreach ((Control row, _) in rows)
            {
                if (row.GetChildOrNull<Label>(1) is { } label)
                {
                    yield return label.Text;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "Messages";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 1);
    }

    /// <summary>The original's face for notice kind <paramref name="kind"/>; 0 for none.</summary>
    public static int FaceFor(int kind) => kind switch
    {
        1 or 5 => HappyFace,
        3 or (>= 9 and <= 15) => WorriedFace,
        6 or 7 or 8 => SadFace,
        _ => 0,
    };

    /// <summary>Adds a message; <paramref name="place"/> is the cell it is about, if any.</summary>
    public void Add(string text, int face, (int X, int Y)? place)
    {
        var row = new MessageRow { MouseFilter = place is null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop };
        row.AddThemeConstantOverride("separation", 3);
        var icon = new TextureRect
        {
            Texture = face > 0 ? HudArt.Sprite(face) : null,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(14, 14),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddChild(icon);
        Label label = HudWidgets.Value(text, PixelTheme.Variations.OnDark);
        label.AddThemeColorOverride("font_shadow_color", UiPalette.Ink);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        if (place is { } p)
        {
            row.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } or InputEventScreenTouch { Pressed: true })
                {
                    PlaceTapped?.Invoke(p.X, p.Y);
                    AcceptEvent();
                }
            };
        }
        AddChild(row);
        rows.Add((row, 0));
        while (rows.Count > MaxShown)
        {
            Remove(0);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            (Control row, double age) = rows[i];
            age += delta;
            rows[i] = (row, age);
            if (age > ShownSeconds + FadeSeconds)
            {
                Remove(i);
                continue;
            }
            row.Modulate = new Color(1, 1, 1, age > ShownSeconds ? (float)(1 - ((age - ShownSeconds) / FadeSeconds)) : 1);
        }
    }

    /// <summary>Removes every message.</summary>
    public void Clear()
    {
        while (rows.Count > 0)
        {
            Remove(0);
        }
    }

    private void Remove(int index)
    {
        Control row = rows[index].Row;
        rows.RemoveAt(index);
        RemoveChild(row);
        row.QueueFree();
    }
}

/// <summary>
/// A row of the message list: a tap counts only on its icon and its words. The line runs across
/// the list's whole width (long messages wrap), so the empty rest of a short message's line lets
/// taps and clicks through to the map.
/// </summary>
public partial class MessageRow : HBoxContainer
{
    /// <summary>Pixels past the last letter that still count as the message.</summary>
    private const float Slack = 2;

    /// <inheritdoc/>
    public override bool _HasPoint(Vector2 point) =>
        point.X >= 0 && point.Y >= 0 && point.X < WordsEnd() && point.Y < Size.Y;

    /// <summary>Where the words end in the row: the text's width, or the label's when it wraps.</summary>
    private float WordsEnd()
    {
        if (GetChildOrNull<Label>(1) is not { } label)
        {
            return Size.X;
        }
        Font font = label.GetThemeFont("font");
        float words = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
        return label.Position.X + Mathf.Min(words, label.Size.X) + Slack;
    }
}
