using System;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The panel about what is selected: one villager, a group, a building, or something that is not
/// the player's. It is rebuilt when what it shows changes (checked on every rule frame) and is
/// hidden when nothing is selected. A cross at its top right lets go of the selection: on touch
/// screens a tap on open ground sends the selected villagers there, so this is the way to deselect
/// on screen.
/// </summary>
public partial class ContextPanel : PanelContainer
{
    private readonly IMatchActions actions;
    private HBoxContainer row = null!;
    private VBoxContainer holder = null!;
    private ScrollContainer scroll = null!;
    private Button? deselect;
    private int deselectSize;
    private Control? card;
    private int shownSignature = int.MinValue;

    /// <summary>A panel for the match <paramref name="actions"/> plays.</summary>
    public ContextPanel(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The panel's content changed size (the layout may move things).</summary>
    public event Action? ContentChanged;

    /// <summary>The height the panel needs to show its whole card (the layout gives it at most what fits).</summary>
    public float WantedHeight
    {
        get
        {
            StyleBox? style = GetThemeStylebox("panel");
            float chrome = style is null ? 0 : style.GetMinimumSize().Y;
            // The scroll asks for no height of its own; the row's is the cross's.
            return Math.Max(holder.GetCombinedMinimumSize().Y, row.GetCombinedMinimumSize().Y) + chrome;
        }
    }

    /// <summary>Buttons of the widest row of orders a card has (a villager's: eat, rest, store, show).</summary>
    public const int WidestOrderRow = 4;

    /// <summary>
    /// The width the panel needs for <see cref="WidestOrderRow"/> buttons on one line beside the
    /// cross, with room for the scroll bar of a card taller than the panel.
    /// </summary>
    public float OrdersWidth(HudMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        StyleBox? style = GetThemeStylebox("panel");
        float chrome = style is null ? 0 : style.GetMinimumSize().X;
        float bar = scroll.GetVScrollBar().GetCombinedMinimumSize().X;
        float buttons = (WidestOrderRow * metrics.Button) + ((WidestOrderRow - 1) * metrics.Gap);
        return Mathf.Ceil(chrome + buttons + bar + metrics.Gap + metrics.Button + metrics.Gap);
    }

    /// <summary>The card shown now, or null.</summary>
    public Control? Card => card;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "ContextPanel";
        ThemeTypeVariation = PixelTheme.Variations.Sheet;
        MouseFilter = MouseFilterEnum.Stop;
        holder = HudPanels.Column(2);
        // A card taller than the room it gets scrolls instead of running off the screen; the cross
        // stays beside it, so it never scrolls away or covers the card.
        scroll = Tribes.Client.Ui.UiFactory.Scroll(holder);
        scroll.MouseFilter = MouseFilterEnum.Pass;
        row = HudPanels.Row(actions.Metrics.Gap);
        row.AddChild(scroll);
        AddChild(row);
        Visible = false;
    }

    /// <summary>Kept hidden (while a story scene plays) whatever is selected.</summary>
    public bool Suppressed
    {
        get => suppressed;
        set
        {
            suppressed = value;
            Visible = card is not null && !suppressed;
        }
    }

    private bool suppressed;

    /// <summary>Rebuilds the card when what it shows changed; <paramref name="force"/> rebuilds anyway.</summary>
    public void Refresh(bool force = false)
    {
        UnitSelection selection = actions.Selection;
        int signature = Signature(selection);
        if (!force && signature == shownSignature)
        {
            return;
        }
        shownSignature = signature;
        if (card is not null)
        {
            holder.RemoveChild(card);
            card.QueueFree();
            card = null;
        }
        card = BuildCard(selection);
        Visible = card is not null && !suppressed;
        if (card is not null)
        {
            holder.AddChild(card);
            FitDeselect();
        }
        ContentChanged?.Invoke();
    }

    /// <summary>
    /// Builds the cross (again when the button size changed, as after turning the screen); it is
    /// kept while the card is rebuilt so a press on it is never lost.
    /// </summary>
    private void FitDeselect()
    {
        HudMetrics m = actions.Metrics;
        if (deselect is not null && deselectSize == m.Button)
        {
            return;
        }
        if (deselect is not null)
        {
            row.RemoveChild(deselect);
            deselect.QueueFree();
        }
        row.AddThemeConstantOverride("separation", m.Gap);
        deselect = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Close), "hud.deselect",
            () => actions.Selection.Clear(), name: "DeselectButton");
        deselect.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(deselect);
        deselectSize = m.Button;
    }

    private int Signature(UnitSelection selection)
    {
        GameWorld world = actions.World;
        var hash = new HashCode();
        hash.Add(selection.Version);
        hash.Add(actions.Metrics.Button);
        hash.Add(Tribes.Client.Ui.Text.UiText.Current);
        switch (selection.Kind)
        {
            case SelectionKind.Units when selection.Units.Count == 1:
                hash.Add(UnitCard.Signature(world.Unit(selection.Units[0])));
                break;
            case SelectionKind.Units:
                foreach (int unit in selection.Units)
                {
                    hash.Add(world.Unit(unit).Portrait);
                }
                break;
            case SelectionKind.Building:
                hash.Add(BuildingCard.Signature(world, world.Building(selection.Building)));
                break;
            case SelectionKind.Other:
                int code = selection.OtherCode;
                if (CellTargets.UnitOf(world, code) is int otherUnit)
                {
                    hash.Add(UnitCard.Signature(world.Unit(otherUnit)));
                }
                else if (CellTargets.BuildingOf(world, code) is int otherBuilding)
                {
                    hash.Add(BuildingCard.Signature(world, world.Building(otherBuilding)));
                }
                hash.Add(world.Frame / 8);
                break;
        }
        return hash.ToHashCode();
    }

    private Control? BuildCard(UnitSelection selection)
    {
        GameWorld world = actions.World;
        return selection.Kind switch
        {
            SelectionKind.Units when selection.Units.Count == 1 => UnitCard.Build(actions, world.Unit(selection.Units[0]), own: true),
            SelectionKind.Units => OtherCards.Group(actions, selection.Units),
            SelectionKind.Building => BuildingCard.Build(actions, world.Building(selection.Building), own: true),
            SelectionKind.Other => OtherCards.Other(actions, selection.OtherCode),
            _ => null,
        };
    }
}
