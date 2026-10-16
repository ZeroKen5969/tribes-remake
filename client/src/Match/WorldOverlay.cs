using System.Collections.Generic;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.World;
using Tribes.Core.Content;
using Tribes.Graphics.Composition;

namespace Tribes.Client.MatchPlay;

/// <summary>What the overlay shows at the pointer.</summary>
public enum OverlayMode
{
    /// <summary>Nothing (normal play).</summary>
    None,

    /// <summary>A building's placement ghost with its footprint cells.</summary>
    Building,

    /// <summary>A trap's placement ghost.</summary>
    Trap,

    /// <summary>The army's gathering point.</summary>
    ArmyPoint,

    /// <summary>A spell's target (a unit or a place).</summary>
    SpellTarget,
}

/// <summary>
/// Drawn over the world view in map pixels: the ghost of a building or trap being placed with each
/// footprint cell marked free or blocked, the pointer marks of target picking, the original's
/// order feedback, and the corner marks of the selected building. The ghost is drawn as one
/// picture in one pass, so it is always complete wherever the camera is.
/// </summary>
public partial class WorldOverlay : Node2D
{
    private const int CannotBuildEffect = 40, CanBuildEffect = 41, FirstSpellCursor = 48, ArmyPointEffect = 105;
    private const int OrderFeedbackEffect = 35, AttackFeedbackEffect = 36, FeedbackFrames = 6;
    private const int TopLeftCorner = 6, TopRightCorner = 7, BottomLeftCorner = 8, BottomRightCorner = 9;
    private static readonly Color FreeTint = new(UiPalette.GreenBright, 0.32f);
    private static readonly Color BlockedTint = new(UiPalette.Red, 0.42f);

    private readonly List<FootprintCell> cells = new();
    private IReadOnlyList<DrawPart> ghost = System.Array.Empty<DrawPart>();
    private int ghostKind = -1;
    private int clock;
    private double clockSeconds;
    private (GridPoint Cell, bool Attack, int Frames, int? Mark)? feedback;
    private (GridPoint Cell, int Cursor, int? Mark)? pointer;
    private Rect2I? markedBuilding;

    /// <summary>What is shown at the pointer.</summary>
    public OverlayMode Mode { get; private set; }

    /// <summary>The footprint's top-left cell (building and trap modes) or the pointed cell.</summary>
    public GridPoint Cell { get; private set; }

    /// <summary>Whether every footprint cell is free.</summary>
    public bool AllFree { get; private set; }

    /// <summary>The spell being aimed (spell target mode).</summary>
    public int Spell { get; private set; }

    /// <summary>The building whose corners are marked, as its footprint in cells; null for none (the marks go at once).</summary>
    public Rect2I? MarkedBuilding
    {
        get => markedBuilding;
        set
        {
            if (value != markedBuilding)
            {
                markedBuilding = value;
                QueueRedraw();
            }
        }
    }

    /// <summary>The cells of the footprint shown and whether each is free (for tests and the HUD).</summary>
    public IReadOnlyList<FootprintCell> Footprint => cells;

    /// <summary>The ghost's parts (relative to the footprint origin), for checks.</summary>
    public IReadOnlyList<DrawPart> GhostParts => ghost;

    /// <summary>Shows the placement ghost of building or trap <paramref name="kind"/> at <paramref name="origin"/>.</summary>
    public void ShowPlacement(int kind, GridPoint origin, IReadOnlyList<FootprintCell> footprint)
    {
        Mode = RuleNumbers.IsTrap(kind) ? OverlayMode.Trap : OverlayMode.Building;
        if (kind != ghostKind)
        {
            ghostKind = kind;
            ghost = OriginalArt.Shared.Composers.Objects.Compose(ObjectRequest.Preview(kind)).Drawing.Parts;
        }
        Cell = origin;
        cells.Clear();
        cells.AddRange(footprint);
        AllFree = cells.TrueForAll(c => c.Free);
        QueueRedraw();
    }

    /// <summary>Shows the army point or a spell's target marker at <paramref name="cell"/>.</summary>
    public void ShowTarget(OverlayMode mode, GridPoint cell, int spell = 0)
    {
        Mode = mode;
        Cell = cell;
        Spell = spell;
        cells.Clear();
        QueueRedraw();
    }

    /// <summary>Stops showing a ghost or target.</summary>
    public void Clear()
    {
        if (Mode == OverlayMode.None)
        {
            return;
        }
        Mode = OverlayMode.None;
        cells.Clear();
        QueueRedraw();
    }

    /// <summary>
    /// Plays the original's order feedback at <paramref name="cell"/> (an attack shows the other
    /// mark), with the target's <paramref name="mark"/> (<see cref="Logic.TargetMarks"/>) over it.
    /// </summary>
    public void ShowOrderFeedback(GridPoint cell, bool attack, int? mark = null)
    {
        feedback = (cell, attack, 0, mark);
        QueueRedraw();
    }

    /// <summary>Shows the original's cursor <paramref name="cursor"/> and target <paramref name="mark"/> at the cell under the mouse.</summary>
    public void ShowPointer(GridPoint cell, int cursor, int? mark)
    {
        if (pointer is { } p && p.Cell == cell && p.Cursor == cursor && p.Mark == mark)
        {
            return;
        }
        pointer = (cell, cursor, mark);
        QueueRedraw();
    }

    /// <summary>Stops showing the cursor and target mark under the mouse.</summary>
    public void HidePointer()
    {
        if (pointer is null)
        {
            return;
        }
        pointer = null;
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // The pointer marks animate on the original's cursor clock (one step per 120 ms).
        clockSeconds += delta;
        if (clockSeconds < 0.12)
        {
            return;
        }
        clockSeconds = 0;
        clock++;
        bool feedbackEnded = false;
        if (feedback is { } f)
        {
            feedback = f.Frames + 1 >= FeedbackFrames ? null : (f.Cell, f.Attack, f.Frames + 1, f.Mark);
            feedbackEnded = feedback is null;
        }
        // One more picture after the order feedback ends, so its last frame does not stay on screen.
        if (Mode != OverlayMode.None || feedback is not null || feedbackEnded || MarkedBuilding is not null || HoveredBuilding is not null || pointer is not null)
        {
            QueueRedraw();
        }
    }

    /// <summary>The building under the mouse pointer, framed as the original frames the one under its cursor; null for none.</summary>
    public Rect2I? HoveredBuilding
    {
        get => hoveredBuilding;
        set
        {
            if (value != hoveredBuilding)
            {
                hoveredBuilding = value;
                QueueRedraw();
            }
        }
    }

    private Rect2I? hoveredBuilding;

    /// <inheritdoc/>
    public override void _Draw()
    {
        OriginalArt art = OriginalArt.Shared;
        if (MarkedBuilding is { } b)
        {
            DrawCorners(art, b);
        }
        if (HoveredBuilding is { } h)
        {
            DrawCorners(art, h);
        }
        switch (Mode)
        {
            case OverlayMode.Building:
            case OverlayMode.Trap:
                DrawPlacement(art);
                break;
            case OverlayMode.ArmyPoint:
                art.DrawEffect(this, ArmyPointEffect, clock, CellCorner(Cell));
                break;
            case OverlayMode.SpellTarget:
                art.DrawEffect(this, FirstSpellCursor + Spell, clock, CellCorner(Cell));
                break;
        }
        if (Mode == OverlayMode.None && pointer is { } p)
        {
            // The original draws its cursor and the target mark on the cell under it, on the cursor clock.
            art.DrawEffect(this, p.Cursor, clock, CellCorner(p.Cell));
            if (p.Mark is { } mark)
            {
                art.DrawEffect(this, mark, clock, CellCorner(p.Cell));
            }
        }
        if (feedback is { } f)
        {
            art.DrawEffect(this, f.Attack ? AttackFeedbackEffect : OrderFeedbackEffect, f.Frames, CellCorner(f.Cell));
            if (f.Mark is { } ordered)
            {
                art.DrawEffect(this, ordered, f.Frames, CellCorner(f.Cell));
            }
        }
    }

    private void DrawPlacement(OriginalArt art)
    {
        foreach (FootprintCell cell in cells)
        {
            Vector2 at = CellCorner(cell.Cell);
            DrawRect(new Rect2(at, WorldView.CellWidth, WorldView.CellHeight), cell.Free ? FreeTint : BlockedTint);
        }
        // The whole preview at once, slightly see-through so the cell marks under it stay readable.
        art.Draw(this, ghost, CellCorner(Cell), new Color(1, 1, 1, 0.85f));
        foreach (FootprintCell cell in cells)
        {
            Vector2 at = CellCorner(cell.Cell);
            DrawRect(new Rect2(at, WorldView.CellWidth, WorldView.CellHeight), cell.Free ? UiPalette.GreenBright : UiPalette.Red, filled: false, width: 1);
        }
        art.DrawEffect(this, AllFree ? CanBuildEffect : CannotBuildEffect, clock, CellCorner(Cell));
    }

    private void DrawCorners(OriginalArt art, Rect2I footprint)
    {
        var (tl, tr, bl, br) = BuildingFrame.Corners(footprint.Position.X, footprint.Position.Y, footprint.Size.X, footprint.Size.Y,
            WorldView.CellWidth, WorldView.CellHeight);
        art.DrawEffect(this, TopLeftCorner, clock, new Vector2(tl.X, tl.Y));
        art.DrawEffect(this, TopRightCorner, clock, new Vector2(tr.X, tr.Y));
        art.DrawEffect(this, BottomLeftCorner, clock, new Vector2(bl.X, bl.Y));
        art.DrawEffect(this, BottomRightCorner, clock, new Vector2(br.X, br.Y));
    }

    private static Vector2 CellCorner(GridPoint cell) => new(cell.X * WorldView.CellWidth, cell.Y * WorldView.CellHeight);
}
