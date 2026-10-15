using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The sidecar of a dev snapshot (<c>NAME.json</c> next to <c>NAME.trbw</c>): where the original's
/// camera was and what its screen kept at that frame, so the world view can show the frame as the
/// original did. <see cref="Exclude"/> lists the screen rectangles the original covered with its
/// interface (bars, panels, cursor), which a comparison skips.
/// </summary>
public sealed record SnapshotView
{
    /// <summary>Monkey seed of the run.</summary>
    public int Seed { get; init; }

    /// <summary>Presented frame number.</summary>
    public int Frame { get; init; }

    /// <summary>Map column of the view's left cell.</summary>
    public int CameraColumn { get; init; }

    /// <summary>Map row of the view's top cell.</summary>
    public int CameraRow { get; init; }

    /// <summary>View width in cells.</summary>
    public int Columns { get; init; }

    /// <summary>View height in cells.</summary>
    public int Rows { get; init; }

    /// <summary>Screen y of the view's top-left cell on the original's 240-pixel-wide screen.</summary>
    public int MapTop { get; init; }

    /// <summary>Width of the original's screen.</summary>
    public int ScreenWidth { get; init; } = 240;

    /// <summary>Height of the original's screen.</summary>
    public int ScreenHeight { get; init; } = 320;

    /// <summary>The fast clock.</summary>
    public int Blink { get; init; }

    /// <summary>The animation clock.</summary>
    public int Cycle { get; init; }

    /// <summary>The palette variant selected when the frame started drawing.</summary>
    public int PaletteVariant { get; init; } = -1;

    /// <summary>State of the floating-number jitter generator.</summary>
    public long JitterState { get; init; }

    /// <summary>The effect list (40 slots: effect, frame, anchor, subject, slot).</summary>
    public List<int[]> Effects { get; init; } = new();

    /// <summary>Floating numbers: ring start and end.</summary>
    public int LabelFirst { get; init; }

    /// <summary>Floating numbers: ring start and end.</summary>
    public int LabelEnd { get; init; }

    /// <summary>The ring of floating numbers.</summary>
    public List<SnapshotLabel> Labels { get; init; } = new();

    /// <summary>Changes not shown yet, by subject.</summary>
    public int[] PendingLabels { get; init; } = Array.Empty<int>();

    /// <summary>Arcs of the projectiles in flight.</summary>
    public List<SnapshotArc> Arcs { get; init; } = new();

    /// <summary>Units with the selected-group marker.</summary>
    public int[] SelectedUnits { get; init; } = Array.Empty<int>();

    /// <summary>Units showing health bars as selected.</summary>
    public int[] FocusedUnits { get; init; } = Array.Empty<int>();

    /// <summary>The unit under the pointer.</summary>
    public int HoveredUnit { get; init; }

    /// <summary>Units in the selection rectangle.</summary>
    public int[] BoxedUnits { get; init; } = Array.Empty<int>();

    /// <summary>The map cell under the cursor.</summary>
    public int CursorX { get; init; }

    /// <summary>The map cell under the cursor.</summary>
    public int CursorY { get; init; }

    /// <summary>Task icon mode.</summary>
    public int TaskIconMode { get; init; } = 2;

    /// <summary>Task icons by unit.</summary>
    public Dictionary<int, int> TaskIcons { get; init; } = new();

    /// <summary>Health bars hidden.</summary>
    public bool HideHealthBars { get; init; }

    /// <summary>The unit with the current-unit arrow.</summary>
    public int PointedUnit { get; init; }

    /// <summary>Screen rectangles (x, y, width, height) covered by the original's interface.</summary>
    public List<int[]> Exclude { get; init; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Reads a sidecar.</summary>
    public static SnapshotView Parse(string json) =>
        JsonSerializer.Deserialize<SnapshotView>(json, Json) ?? throw new InvalidDataException("empty snapshot sidecar");

    /// <summary>The sidecar as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json) + "\n";

    /// <summary>The view of the snapshot.</summary>
    [JsonIgnore]
    public ViewRect View => new(CameraColumn, CameraRow, Columns, Rows);

    /// <summary>The selection marks of the snapshot.</summary>
    [JsonIgnore]
    public WorldMarks Marks => new()
    {
        SelectedUnits = SelectedUnits.ToHashSet(),
        FocusedUnits = FocusedUnits.ToHashSet(),
        HoveredUnit = HoveredUnit,
        BoxedUnits = BoxedUnits.ToHashSet(),
        CursorCell = (CursorX, CursorY),
        TaskIconMode = TaskIconMode,
        TaskIcons = TaskIcons,
        HideHealthBars = HideHealthBars,
        PointedUnit = PointedUnit,
    };

    /// <summary>A presentation state holding what the original's screen kept at the frame.</summary>
    public ScenePresentation Presentation()
    {
        var p = new ScenePresentation
        {
            Blink = Blink,
            Cycle = Cycle,
            PaletteVariant = PaletteVariant,
            Jitter = new JavaRandom(0) { State = JitterState },
        };
        for (int i = 0; i < Math.Min(Effects.Count, ScenePresentation.EffectCount); i++)
        {
            int[] e = Effects[i];
            p.Effects[i] = new EffectSlot
            {
                Effect = unchecked((sbyte)e[0]),
                Frame = unchecked((sbyte)e[1]),
                Anchor = unchecked((sbyte)e[2]),
                Subject = unchecked((sbyte)e[3]),
                Slot = unchecked((sbyte)e[4]),
            };
        }
        p.Labels.First = LabelFirst;
        p.Labels.End = LabelEnd;
        for (int i = 0; i < Math.Min(Labels.Count, FloatingLabels.RingSize); i++)
        {
            SnapshotLabel l = Labels[i];
            p.Labels.Ring[i] = new FloatingLabels.Label { Key = l.Key, Rise = (short)l.Rise, Colour = l.Colour, Text = l.Text };
        }
        for (int i = 0; i < Math.Min(PendingLabels.Length, FloatingLabels.Subjects); i++)
        {
            p.Labels.Pending[i] = unchecked((sbyte)PendingLabels[i]);
        }
        foreach (SnapshotArc arc in Arcs)
        {
            p.Arcs[arc.Unit] = new ProjectileArc(arc.Heights, arc.Frames);
        }
        return p;
    }
}

/// <summary>A floating number of a snapshot.</summary>
/// <param name="Key">Unit, or 101 + animal; 0 free.</param>
/// <param name="Rise">How far it has risen.</param>
/// <param name="Colour">Font colour row.</param>
/// <param name="Text">The characters.</param>
public sealed record SnapshotLabel(int Key, int Rise, int Colour, string Text);

/// <summary>A projectile arc of a snapshot.</summary>
/// <param name="Unit">The thrower.</param>
/// <param name="Heights">Height per step.</param>
/// <param name="Frames">Picture offset per step.</param>
public sealed record SnapshotArc(int Unit, int[] Heights, int[] Frames);
