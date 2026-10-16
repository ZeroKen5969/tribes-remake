using System;
using Godot;
using Tribes.Client.App;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The panel about what is selected: one villager, a group, a building, or something that is not
/// the player's. It is rebuilt when what it shows changes (checked on every rule frame) and is
/// hidden when nothing is selected. Beside the card stand its buttons: a cross that lets go of the
/// selection (on touch screens a tap on open ground sends the selected villagers there, so this is
/// the way to deselect on screen), one that folds the panel into a small tab (face and name) and
/// opens it again, and on a villager's card one that shows or hides the skills. Both are
/// settings: the buttons save them, and a change made in the menu shows at once.
/// </summary>
public partial class ContextPanel : PanelContainer
{
    /// <summary>The smallest width of the folded tab's name.</summary>
    private const int FoldedNameWidth = 72;

    /// <summary>The skill icon on the details button (the melee skill's).</summary>
    private const int SkillsIcon = 14;

    private readonly IMatchActions actions;
    private HBoxContainer row = null!;
    private VBoxContainer holder = null!;
    private ScrollContainer scroll = null!;
    private BoxContainer side = null!;
    private Button? deselect, fold, details;
    private int buttonSize;
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
            // The scroll asks for no height of its own; the row's is the buttons'.
            return Math.Max(holder.GetCombinedMinimumSize().Y, row.GetCombinedMinimumSize().Y) + chrome;
        }
    }

    /// <summary>Whether the panel is folded into its tab (the layout makes it only as wide as the tab).</summary>
    public bool Folded => Settings.SelectionPanelFolded;

    /// <summary>The width the folded tab needs.</summary>
    public float FoldedWidth
    {
        get
        {
            StyleBox? style = GetThemeStylebox("panel");
            float chrome = style is null ? 0 : style.GetMinimumSize().X;
            return Mathf.Ceil(holder.GetCombinedMinimumSize().X + side.GetCombinedMinimumSize().X + actions.Metrics.Gap + chrome);
        }
    }

    /// <summary>Buttons of the widest row of orders a card has (a villager's: eat, rest, store, show).</summary>
    public const int WidestOrderRow = 4;

    /// <summary>
    /// The width the panel needs for <see cref="WidestOrderRow"/> buttons on one line beside the
    /// buttons' column, with room for the scroll bar of a card taller than the panel.
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

    /// <summary>The selection version the card shown now was built for.</summary>
    public int ShownSelectionVersion { get; private set; } = -1;

    private static GameSettings Settings => GameApp.Instance.Settings;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "ContextPanel";
        ThemeTypeVariation = PixelTheme.Variations.Sheet;
        MouseFilter = MouseFilterEnum.Stop;
        holder = HudPanels.Column(2);
        // A card taller than the room it gets scrolls instead of running off the screen; the
        // buttons stay beside it, so they never scroll away or cover the card.
        scroll = Tribes.Client.Ui.UiFactory.Scroll(holder);
        scroll.MouseFilter = MouseFilterEnum.Pass;
        row = HudPanels.Row(actions.Metrics.Gap);
        row.AddChild(scroll);
        side = new BoxContainer { Name = "PanelButtons", Vertical = true, MouseFilter = MouseFilterEnum.Ignore };
        side.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(side);
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
        ShownSelectionVersion = selection.Version;
        if (card is not null)
        {
            holder.RemoveChild(card);
            card.QueueFree();
            card = null;
        }
        card = Folded ? BuildTab(selection) : BuildCard(selection);
        Visible = card is not null && !suppressed;
        if (card is not null)
        {
            holder.AddChild(card);
            FitButtons();
            ShowButtons(selection);
        }
        ContentChanged?.Invoke();
    }

    /// <summary>
    /// Builds the buttons (again when their size changed, as after turning the screen); they are
    /// kept while the card is rebuilt so a press on them is never lost.
    /// </summary>
    private void FitButtons()
    {
        HudMetrics m = actions.Metrics;
        if (deselect is not null && buttonSize == m.Button)
        {
            return;
        }
        foreach (Button? old in new[] { deselect, fold, details })
        {
            if (old is not null)
            {
                side.RemoveChild(old);
                old.QueueFree();
            }
        }
        row.AddThemeConstantOverride("separation", m.Gap);
        side.AddThemeConstantOverride("separation", m.Gap);
        deselect = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Close), "hud.deselect",
            () => actions.Selection.Clear(), name: "DeselectButton");
        fold = HudWidgets.IconButton(m, HudArt.Painted(PaintedIcon.Minus), "hud.panel.fold", ToggleFolded, name: "FoldButton");
        details = HudWidgets.IconButton(m, HudArt.Inline(SkillsIcon), "hud.panel.details", ToggleDetailed, name: "DetailsButton");
        details.ToggleMode = true;
        side.AddChild(deselect);
        side.AddChild(fold);
        side.AddChild(details);
        buttonSize = m.Button;
    }

    /// <summary>
    /// The folded tab lines its buttons up beside it; the open panel stacks them. The details
    /// button is there only on a villager's open card.
    /// </summary>
    private void ShowButtons(UnitSelection selection)
    {
        bool folded = Folded;
        side.Vertical = !folded;
        fold!.Icon = HudWidgets.Enlarge(HudArt.Painted(folded ? PaintedIcon.Plus : PaintedIcon.Minus), actions.Metrics.IconScale);
        fold.TooltipText = folded ? "hud.panel.unfold" : "hud.panel.fold";
        details!.Visible = !folded && ShowsOneVillager(selection);
        details.SetPressedNoSignal(Detailed);
    }

    /// <summary>Whether a villager's card shows the skills.</summary>
    private static bool Detailed => Settings.UnitCardDetailed;

    /// <summary>Folds or opens the panel, remembered in the settings.</summary>
    private void ToggleFolded()
    {
        Settings.SelectionPanelFolded = !Settings.SelectionPanelFolded;
        Settings.Save();
        Refresh(force: true);
    }

    /// <summary>Shows or hides a villager's skills, remembered in the settings.</summary>
    private void ToggleDetailed()
    {
        Settings.UnitCardDetailed = !Settings.UnitCardDetailed;
        Settings.Save();
        Refresh(force: true);
    }

    private bool ShowsOneVillager(UnitSelection selection) => selection.Kind switch
    {
        SelectionKind.Units => selection.Units.Count == 1,
        SelectionKind.Other => CellTargets.UnitOf(actions.World, selection.OtherCode) is not null,
        _ => false,
    };

    private int Signature(UnitSelection selection)
    {
        GameWorld world = actions.World;
        var hash = new HashCode();
        hash.Add(selection.Version);
        hash.Add(actions.Metrics.Button);
        hash.Add(Tribes.Client.Ui.Text.UiText.Current);
        hash.Add(Settings.SelectionPanelFolded);
        hash.Add(Detailed);
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
            SelectionKind.Units when selection.Units.Count == 1 => UnitCard.Build(actions, world.Unit(selection.Units[0]), own: true, Detailed),
            SelectionKind.Units => OtherCards.Group(actions, selection.Units),
            SelectionKind.Building => BuildingCard.Build(actions, world.Building(selection.Building), own: true),
            SelectionKind.Other => OtherCards.Other(actions, selection.OtherCode, Detailed),
            _ => null,
        };
    }

    /// <summary>The folded panel's tab: the villager's face and name, the group's size, or what the thing is.</summary>
    private Control? BuildTab(UnitSelection selection)
    {
        GameWorld world = actions.World;
        switch (selection.Kind)
        {
            case SelectionKind.Units when selection.Units.Count == 1:
                return NamedTab(UnitCard.Tab(actions, world.Unit(selection.Units[0])));
            case SelectionKind.Units:
                HBoxContainer group = HudPanels.Row(4);
                group.Name = "GroupTab";
                group.AddChild(HudArt.Icon(HudArt.Piece(HudArt.PopulationPiece)));
                group.AddChild(HudWidgets.Value(Tribes.Client.Ui.Text.UiText.F("hud.panel.group", selection.Units.Count), PixelTheme.Variations.Heading));
                return group;
            case SelectionKind.Building:
                return TextTab(GameTexts.Building(world.Building(selection.Building).Kind));
            case SelectionKind.Other:
                int code = selection.OtherCode;
                if (CellTargets.UnitOf(world, code) is int unit)
                {
                    return NamedTab(UnitCard.Tab(actions, world.Unit(unit)));
                }
                if (CellTargets.BuildingOf(world, code) is int building)
                {
                    return TextTab(GameTexts.Building(world.Building(building).Kind));
                }
                return TextTab(Tribes.Client.Ui.Text.UiText.T(CellTargets.AnimalOf(world, code) is not null ? "hud.other.animal" : "hud.panel.selected"));
            default:
                return null;
        }
    }

    private static Control NamedTab(Control tab)
    {
        if (tab.GetChild(tab.GetChildCount() - 1) is Label name)
        {
            name.CustomMinimumSize = new Vector2(FoldedNameWidth, 0);
        }
        return tab;
    }

    private static Control TextTab(string text)
    {
        Label label = HudWidgets.Value(text, PixelTheme.Variations.Heading);
        label.Name = "TextTab";
        label.CustomMinimumSize = new Vector2(FoldedNameWidth, 0);
        label.ClipText = true;
        return label;
    }
}
