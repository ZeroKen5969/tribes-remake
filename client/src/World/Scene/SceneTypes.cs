using System;
using System.Collections.Generic;
using Tribes.Graphics.Composition;

namespace Tribes.Client.World.Scene;

/// <summary>The cells a view shows: the map cell at its top-left corner and its size in cells.</summary>
/// <param name="Column">Map column of the view's left cell.</param>
/// <param name="Row">Map row of the view's top cell.</param>
/// <param name="Columns">Width in cells.</param>
/// <param name="Rows">Height in cells.</param>
public readonly record struct ViewRect(int Column, int Row, int Columns, int Rows)
{
    /// <summary>Whether map cell (<paramref name="x"/>, <paramref name="y"/>) is inside the view.</summary>
    public bool Contains(int x, int y) => x >= Column && x < Column + Columns && y >= Row && y < Row + Rows;
}

/// <summary>What moves a scene item with it when motion between rule frames is smoothed.</summary>
public enum MoverKind : byte
{
    /// <summary>Stays where it is drawn.</summary>
    None,

    /// <summary>Follows a unit (its body and what is drawn on it).</summary>
    Unit,

    /// <summary>Follows a wild animal.</summary>
    Animal,

    /// <summary>Follows a unit's projectile in flight.</summary>
    Projectile,
}

/// <summary>
/// One thing to draw in the object layer, in view pixels (the view's top-left cell starts at 0, 0).
/// </summary>
/// <param name="Part">The part, already placed in view pixels.</param>
/// <param name="SkinTribe">The tribe whose skin colour the part shows (a villager's body part), or -1.</param>
/// <param name="Mover">What the part moves with.</param>
/// <param name="MoverId">The unit, animal or thrower slot of <paramref name="Mover"/>.</param>
public readonly record struct SceneItem(DrawPart Part, int SkinTribe, MoverKind Mover, int MoverId);

/// <summary>The object layer of one frame: parts in drawing order.</summary>
public sealed class SceneList
{
    private readonly List<SceneItem> items = new();

    /// <summary>The items in drawing order.</summary>
    public IReadOnlyList<SceneItem> Items => items;

    /// <summary>Number of items.</summary>
    public int Count => items.Count;

    /// <summary>Item <paramref name="index"/>.</summary>
    public SceneItem this[int index] => items[index];

    /// <summary>Forgets every item.</summary>
    public void Clear() => items.Clear();

    /// <summary>Adds the parts of <paramref name="parts"/> drawn with their anchor at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void Add(IReadOnlyList<DrawPart> parts, int x, int y, int skinTribe = -1, MoverKind mover = MoverKind.None, int moverId = 0)
    {
        ArgumentNullException.ThrowIfNull(parts);
        foreach (DrawPart part in parts)
        {
            items.Add(new SceneItem(part.Offset(x, y), skinTribe, mover, moverId));
        }
    }

    /// <summary>Adds one part already in view pixels.</summary>
    public void Add(DrawPart part, int skinTribe = -1, MoverKind mover = MoverKind.None, int moverId = 0)
    {
        // A fill right beside the fill drawn just before it, in the same colour and rows, is drawn as
        // one rectangle (the black of unexplored land comes cell by cell): the same pixels, fewer parts.
        if (part.Kind == DrawPartKind.Fill && mover == MoverKind.None && items.Count > 0)
        {
            SceneItem last = items[^1];
            DrawPart before = last.Part;
            if (before.Kind == DrawPartKind.Fill && last.Mover == MoverKind.None && before.Color == part.Color
                && before.Y == part.Y && before.Height == part.Height && part.Width > 0 && before.Width > 0)
            {
                if (part.X + part.Width == before.X)
                {
                    items[^1] = last with { Part = before with { X = part.X, Width = before.Width + part.Width } };
                    return;
                }
                if (before.X + before.Width == part.X)
                {
                    items[^1] = last with { Part = before with { Width = before.Width + part.Width } };
                    return;
                }
            }
        }
        items.Add(new SceneItem(part, skinTribe, mover, moverId));
    }
}

/// <summary>
/// Map-screen marks that follow the player's selection rather than the world: the original draws
/// them with the units (highlighted shadows, group markers, health bars of selected units).
/// </summary>
public sealed record WorldMarks
{
    /// <summary>No selection, no cursor.</summary>
    public static WorldMarks None { get; } = new();

    /// <summary>Units drawn with the selected-group marker under their shadow.</summary>
    public IReadOnlySet<int> SelectedUnits { get; init; } = new HashSet<int>();

    /// <summary>Units that show their health bar and task icon because they are selected (alone or with their group).</summary>
    public IReadOnlySet<int> FocusedUnits { get; init; } = new HashSet<int>();

    /// <summary>The unit under the pointer (its shadow is highlighted), or 0.</summary>
    public int HoveredUnit { get; init; }

    /// <summary>The player's units inside a selection rectangle being dragged.</summary>
    public IReadOnlySet<int> BoxedUnits { get; init; } = new HashSet<int>();

    /// <summary>The map cell under the original's cursor (its unit or building shows its health bar), or null.</summary>
    public (int X, int Y)? CursorCell { get; init; }

    /// <summary>
    /// Buildings drawn as if the original's cursor were on them (health bar, place squares, the
    /// marks of the villagers working inside): in a match the selected building and, with a mouse,
    /// the one under the pointer.
    /// </summary>
    public IReadOnlySet<int> PointedBuildings { get; init; } = new HashSet<int>();

    /// <summary>When the task icons of the player's units are drawn: 0 always, 1 for marked units, 2 never.</summary>
    public int TaskIconMode { get; init; } = 2;

    /// <summary>Task icon (sprite 577 + n) of each unit that has one.</summary>
    public IReadOnlyDictionary<int, int> TaskIcons { get; init; } = new Dictionary<int, int>();

    /// <summary>Health bars are hidden (the original hides them while the unit info screen is up).</summary>
    public bool HideHealthBars { get; init; }

    /// <summary>The unit drawn with the "current unit" arrow above it, or 0.</summary>
    public int PointedUnit { get; init; }
}
